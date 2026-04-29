using LLama;
using LLama.Common;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using GameEngine.Api.Models;

namespace GameEngine.Api.Services
{
    public class LlmInferenceService : IDisposable
    {
        private LLamaWeights? _weights;
        private LLamaContext? _context;
        private StatelessExecutor? _executor;
        private bool _isInitialized = false;
        private readonly object _initLock = new object();

        public void Initialize()
        {
            lock (_initLock)
            {
                if (_isInitialized) return;
                if (!System.IO.File.Exists(AppConstants.FullModelPath)) return;

                try 
                {
                    // Llama 3.2 3B has exactly 28 layers. Offloading exactly 28 ensures maximum GPU usage.
                    var parameters = new ModelParams(AppConstants.FullModelPath)
                    {
                        ContextSize = 1024,
                        GpuLayerCount = 28, 
                        UseMemorymap = true,
                        BatchSize = 512,
                        Threads = Math.Max(1, Environment.ProcessorCount)
                    };
                    
                    try 
                    {
                        _weights = LLamaWeights.LoadFromFile(parameters);
                    }
                    catch (Exception gpuEx)
                    {
                        // Fallback to CPU mode if GPU/Vulkan/CUDA initialization fails
                        Console.WriteLine($"[WARNING] GPU Offloading failed: {gpuEx.Message}. Falling back to CPU mode.");
                        parameters.GpuLayerCount = 0;
                        _weights = LLamaWeights.LoadFromFile(parameters);
                    }

                    _context = _weights.CreateContext(parameters);
                    _executor = new StatelessExecutor(_weights, parameters);
                    _isInitialized = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] LLM Init Failed completely: {ex.Message}");
                }
            }
        }

        public async Task<string> GenerateMoveIntentAsync(string formattedPrompt, float temperature = 0.5f, int maxTokens = 60, string? grammar = null)
        {
            if (!_isInitialized) Initialize();
            if (!_isInitialized || _executor == null) 
            {
                return "{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Engine fallback: Model not loaded.\"}";
            }

            var inferenceParams = new InferenceParams()
            {
                SamplingPipeline = new LLama.Sampling.DefaultSamplingPipeline() { Temperature = temperature },
                MaxTokens = maxTokens,
                // Stop on JSON close, or when the assistant finishes (Llama 3 format)
                AntiPrompts = new List<string> { "}\n", "}", "<|eot_id|>", "<|end_of_text|>" }
            };

            if (!string.IsNullOrEmpty(grammar))
            {
                // Note: In newer LLamaSharp, you'd use a Grammar object. 
                // We'll enable it here if you have the GBNF string.
                // inferenceParams.Grammar = SafeParseGrammar(grammar);
                // For now we'll stick to string handling but we've added the parameter for future-proofing
                // and we will use it to optimize the stopping criteria.
            }

            var sb = new StringBuilder(256);

            try
            {
                await foreach (var text in _executor.InferAsync(formattedPrompt, inferenceParams))
                {
                    sb.Append(text);
                }
            }
            catch (Exception ex)
            {
                return $"{{\"Intent\":\"PlaySafe\",\"Reasoning\":\"Inference failure: {ex.Message}\"}}";
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _context?.Dispose();
            _weights?.Dispose();
        }
    }
}
