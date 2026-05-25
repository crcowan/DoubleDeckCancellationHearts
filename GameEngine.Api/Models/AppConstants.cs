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
        public static readonly string BinDirectory = Path.Combine(AppDataDirectory, "bin");

        public static string GetFullModelPath()
        {
            var fileName = "hearts-bot-v1.gguf";
            var localPath = Path.Combine(AppContext.BaseDirectory, "models", fileName);
            if (File.Exists(localPath)) return localPath;
            return Path.Combine(ModelsDirectory, fileName);
        }

        public static string GetLlamaServerPath()
        {
            return Path.Combine(BinDirectory, "llama-server.exe");
        }

        // We use a specific pre-compiled Vulkan build from the official llama.cpp repo
        public static readonly string LlamaServerDownloadUrl = "https://github.com/ggerganov/llama.cpp/releases/download/b4382/llama-b4382-bin-win-vulkan-x64.zip";
    }
}
