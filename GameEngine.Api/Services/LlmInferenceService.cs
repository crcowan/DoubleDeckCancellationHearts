using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameEngine.Api.Models;

namespace GameEngine.Api.Services
{
    public class LlmInferenceService : IDisposable
    {
        private Process? _serverProcess;
        private readonly HttpClient _httpClient;
        private bool _isInitialized = false;
        private readonly object _initLock = new object();
        private readonly SemaphoreSlim _slotSemaphore = new SemaphoreSlim(1, 1);
        private bool _usingDiscreteGpu = false;
        private string _logPath = string.Empty;

        public LlmInferenceService()
        {
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://127.0.0.1:8080/");
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        /// <summary>
        /// Detects whether the system has a discrete GPU (NVIDIA, AMD Radeon RX, Intel Arc A).
        /// Returns true if a discrete GPU is found, false for integrated-only systems.
        /// </summary>
        private bool DetectDiscreteGpu()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "wmic",
                    Arguments = "path win32_videocontroller get name",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(5000);

                string upper = output.ToUpperInvariant();

                // Discrete GPU indicators
                if (upper.Contains("GEFORCE") || upper.Contains("NVIDIA") ||
                    upper.Contains("RADEON RX") || upper.Contains("ARC A"))
                {
                    Console.WriteLine($"[GPU] Discrete GPU detected. Using full GPU offload (-ngl 33).");
                    return true;
                }

                Console.WriteLine($"[GPU] No discrete GPU found. Using CPU-only inference (-ngl 0).");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GPU] Detection failed ({ex.Message}). Defaulting to CPU-only.");
                return false;
            }
        }

        public void Initialize()
        {
            lock (_initLock)
            {
                if (_isInitialized) return;
                
                string serverPath = AppConstants.GetLlamaServerPath();
                string modelPath = AppConstants.GetFullModelPath();
                
                if (!File.Exists(serverPath) || !File.Exists(modelPath)) return;

                _usingDiscreteGpu = DetectDiscreteGpu();
                int gpuLayers = _usingDiscreteGpu ? 33 : 0;

                // Determine log path relative to the server executable
                string serverDir = Path.GetDirectoryName(serverPath) ?? AppContext.BaseDirectory;
                _logPath = Path.Combine(serverDir, "llama-server.log");

                try 
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = serverPath,
                        Arguments = $"-m \"{modelPath}\" -c 2048 -np 1 -ngl {gpuLayers} --cache-reuse 256 --port 8080 --host 127.0.0.1",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    Console.WriteLine($"[LLM] Launching: {startInfo.FileName} {startInfo.Arguments}");

                    _serverProcess = Process.Start(startInfo);
                    
                    if (_serverProcess != null)
                    {
                        _serverProcess.OutputDataReceived += (s, e) => {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                try { File.AppendAllText(_logPath, e.Data + Environment.NewLine); } catch { }
                            }
                        };
                        _serverProcess.ErrorDataReceived += (s, e) => {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                try { File.AppendAllText(_logPath, e.Data + Environment.NewLine); } catch { }
                            }
                        };
                        _serverProcess.BeginOutputReadLine();
                        _serverProcess.BeginErrorReadLine();
                    }
                    
                    // Poll /health endpoint until the server is ready (up to 30 seconds)
                    bool serverReady = WaitForServerReady(TimeSpan.FromSeconds(30));
                    if (!serverReady)
                    {
                        Console.WriteLine("[LLM] WARNING: Server did not become ready within 30 seconds. Proceeding anyway.");
                    }
                    
                    _isInitialized = true;
                    Console.WriteLine($"[LLM] Server initialized. GPU layers: {gpuLayers}, Cache reuse: 256, Context: 2048, Slots: 1");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] Backend Server Init Failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Polls the llama-server /health endpoint every 500ms until it reports ready.
        /// </summary>
        private bool WaitForServerReady(TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            using var healthClient = new HttpClient();
            healthClient.BaseAddress = new Uri("http://127.0.0.1:8080/");
            healthClient.Timeout = TimeSpan.FromSeconds(2);

            while (sw.Elapsed < timeout)
            {
                try
                {
                    var response = healthClient.GetAsync("health").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var body = response.Content.ReadAsStringAsync().Result;
                        if (body.Contains("ok", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine($"[LLM] Server ready in {sw.ElapsedMilliseconds}ms");
                            return true;
                        }
                    }
                }
                catch
                {
                    // Server not up yet, keep polling
                }

                Thread.Sleep(500);
            }

            return false;
        }

        public async Task<string> GenerateMoveIntentAsync(string playerId, string formattedPrompt, float temperature = 0.5f, int maxTokens = 60, string? grammar = null)
        {
            if (!_isInitialized) Initialize();
            if (!_isInitialized) 
            {
                return "{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Engine fallback: Backend not loaded.\"}";
            }

            try
            {
                var requestBody = new
                {
                    messages = new[]
                    {
                        new { role = "user", content = formattedPrompt }
                    },
                    temperature = temperature,
                    max_tokens = maxTokens,
                    stream = false
                };

                var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                await _slotSemaphore.WaitAsync();
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.PostAsync("v1/chat/completions", jsonContent);
                    response.EnsureSuccessStatusCode();
                }
                finally
                {
                    _slotSemaphore.Release();
                }

                var responseString = await response.Content.ReadAsStringAsync();
                
                using var doc = JsonDocument.Parse(responseString);
                var content = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return content ?? "{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Empty API response\"}";
            }
            catch (Exception ex)
            {
                return $"{{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Inference failure: {ex.Message}\"}}";
            }
        }

        public void ClearBotCache(string playerId) { }
        public void ClearAllBotCaches() { }

        public void Dispose()
        {
            _httpClient.Dispose();
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                try { _serverProcess.Kill(); } catch { }
                _serverProcess.Dispose();
            }
        }
    }
}
