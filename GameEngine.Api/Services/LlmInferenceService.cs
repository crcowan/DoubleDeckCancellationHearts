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
        private UserConfig _config;

        public bool UseOllama => _config.UseOllama;
        public string OllamaEndpoint => _config.OllamaEndpoint;
        public string OllamaModel => _config.OllamaModel;

        public LlmInferenceService()
        {
            _config = LoadConfig();
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri("http://127.0.0.1:8080/");
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        private string GetConfigFilePath()
        {
            return Path.Combine(AppConstants.AppDataDirectory, "user_config.json");
        }

        private UserConfig LoadConfig()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<UserConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new UserConfig();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to load config, using defaults: {ex.Message}");
            }
            return new UserConfig();
        }

        private void SaveConfig(UserConfig config)
        {
            try
            {
                string path = GetConfigFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                Console.WriteLine($"[Config] Saved config to {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Config] Failed to save config: {ex.Message}");
            }
        }

        public UserConfig GetConfig()
        {
            lock (_initLock)
            {
                return _config;
            }
        }

        public void UpdateConfig(UserConfig config)
        {
            lock (_initLock)
            {
                _config = config;
                SaveConfig(config);

                // Shut down local server if it is running
                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    try 
                    { 
                        _serverProcess.Kill(); 
                        _serverProcess.WaitForExit(2000);
                    } 
                    catch { }
                    _serverProcess.Dispose();
                    _serverProcess = null;
                }

                _isInitialized = false; // Reset so next AI call re-initializes
            }
        }

        /// <summary>
        /// Detects whether the system has a discrete GPU (NVIDIA, AMD Radeon RX, Intel Arc A).
        /// Returns true if a discrete GPU is found, false for integrated-only systems.
        /// </summary>
        private bool DetectDiscreteGpu()
        {
            string output = "";
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
                if (proc != null)
                {
                    output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
                // Fallback to powershell CIM query if wmic is missing
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-NoProfile -Command \"Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        output = proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit(3000);
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"[GPU] PowerShell fallback detection failed ({ex2.Message}).");
                }
            }

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

        public void Initialize()
        {
            lock (_initLock)
            {
                if (_isInitialized) return;

                if (_config.UseOllama)
                {
                    _isInitialized = true;
                    Console.WriteLine($"[LLM] Configured to use Ollama server at {_config.OllamaEndpoint} with model {_config.OllamaModel}");
                    return;
                }
                
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.LlmTimeoutSeconds > 0 ? _config.LlmTimeoutSeconds : 45));

            try
            {
                if (_config.UseOllama)
                {
                    var requestBody = new
                    {
                        model = _config.OllamaModel,
                        messages = new[]
                        {
                            new { role = "user", content = formattedPrompt }
                        },
                        stream = false,
                        options = new
                        {
                            temperature = temperature,
                            num_predict = maxTokens
                        }
                    };

                    var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                    HttpResponseMessage response;
                    await _slotSemaphore.WaitAsync();
                    try
                    {
                        string endpoint = _config.OllamaEndpoint;
                        if (!endpoint.EndsWith("/")) endpoint += "/";
                        var targetUri = new Uri(new Uri(endpoint), "api/chat");
                        response = await _httpClient.PostAsync(targetUri, jsonContent, cts.Token);
                        response.EnsureSuccessStatusCode();
                    }
                    finally
                    {
                        _slotSemaphore.Release();
                    }

                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    var content = doc.RootElement
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    return content ?? "{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Empty Ollama response.\"}";
                }
                else
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
                        response = await _httpClient.PostAsync("v1/chat/completions", jsonContent, cts.Token);
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
            }
            catch (Exception ex)
            {
                if (!_config.UseOllama)
                {
                    lock (_initLock)
                    {
                        if (_serverProcess != null && !_serverProcess.HasExited)
                        {
                            try { _serverProcess.Kill(); } catch { }
                            _serverProcess.Dispose();
                            _serverProcess = null;
                        }
                        _isInitialized = false;
                    }
                }
                return $"{{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Inference failure (auto-restarting LLM): {ex.Message}\"}}";
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
