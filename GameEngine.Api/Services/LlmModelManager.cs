using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GameEngine.Api.Models;

namespace GameEngine.Api.Services
{
    public class LlmModelManager
    {
        public delegate void DownloadProgressChangedEventHandler(double percentage, string statusMessage);
        public event DownloadProgressChangedEventHandler? OnDownloadProgressChanged;

        public bool IsModelDownloaded(AiModelSize size)
        {
            return File.Exists(AppConstants.GetFullModelPath(size));
        }

        public async Task DownloadModelAsync(AiModelSize size, CancellationToken cancellationToken = default)
        {
            if (IsModelDownloaded(size))
                return;

            if (!Directory.Exists(AppConstants.ModelsDirectory))
            {
                Directory.CreateDirectory(AppConstants.ModelsDirectory);
            }

            string tempFilePath = AppConstants.GetFullModelPath(size) + ".download";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromHours(2); // Models take a while to download

                using var response = await client.GetAsync(AppConstants.GetModelDownloadUrl(size), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[81920]; // 80kb blocks
                long totalRead = 0;
                int bytesRead;
                DateTime lastReportTime = DateTime.MinValue;

                OnDownloadProgressChanged?.Invoke(0, "Starting download...");

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (canReportProgress && (DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 500)
                    {
                        var percentage = Math.Round((double)totalRead / totalBytes * 100, 1);
                        var downloadedMb = Math.Round((double)totalRead / (1024 * 1024), 2);
                        var totalMb = Math.Round((double)totalBytes / (1024 * 1024), 2);

                        OnDownloadProgressChanged?.Invoke(percentage, $"Downloading AI Model... {downloadedMb}MB / {totalMb}MB ({percentage}%)");
                        lastReportTime = DateTime.UtcNow;
                    }
                }

                OnDownloadProgressChanged?.Invoke(100, "Download complete. Verifying...");
            }
            catch (Exception ex)
            {
                // Clean up partial download on failure
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                throw new Exception($"Failed to download the AI model: {ex.Message}", ex);
            }

            // Move temp file to final destination once successfully downloaded
            if (File.Exists(tempFilePath))
            {
                File.Move(tempFilePath, AppConstants.GetFullModelPath(size), overwrite: true);
            }
        }
    }
}
