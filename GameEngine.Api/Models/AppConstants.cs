using System;
using System.IO;

namespace GameEngine.Api.Models
{
    public static class AppConstants
    {
        public static readonly string AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "DoubleDeckCancellationHearts");

        public static readonly string ModelsDirectory = Path.Combine(AppDataDirectory, "models");

        // Models take a while to download, so we store them in LocalApplicationData
        public static string GetFullModelPath(AiModelSize size) => Path.Combine(ModelsDirectory, GetModelFileName(size));

        public static string GetModelFileName(AiModelSize size)
        {
            return size switch
            {
                AiModelSize.Fast2B => "gemma-4-E2B-it-Q4_K_M.gguf",
                AiModelSize.Balanced4B => "gemma-4-E4B-it-Q4_K_M.gguf",
                _ => "gemma-4-E4B-it-Q4_K_M.gguf"
            };
        }

        public static string GetModelDownloadUrl(AiModelSize size)
        {
            return size switch
            {
                AiModelSize.Fast2B => "https://huggingface.co/unsloth/gemma-4-E2B-it-GGUF/resolve/main/gemma-4-E2B-it-Q4_K_M.gguf",
                AiModelSize.Balanced4B => "https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-Q4_K_M.gguf",
                _ => "https://huggingface.co/unsloth/gemma-4-E4B-it-GGUF/resolve/main/gemma-4-E4B-it-Q4_K_M.gguf"
            };
        }
    }
}
