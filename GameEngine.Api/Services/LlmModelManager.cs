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

        public bool IsServerDownloaded()
        {
            return File.Exists(AppConstants.GetLlamaServerPath());
        }

        public async Task DownloadServerAsync(CancellationToken cancellationToken = default)
        {
            if (IsServerDownloaded())
                return;

            if (!Directory.Exists(AppConstants.BinDirectory))
            {
                Directory.CreateDirectory(AppConstants.BinDirectory);
            }

            string tempFilePath = Path.Combine(AppConstants.BinDirectory, "llama-server.zip");

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromHours(1);

                using var response = await client.GetAsync(AppConstants.LlamaServerDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[81920]; // 80kb blocks
                long totalRead = 0;
                int bytesRead;
                DateTime lastReportTime = DateTime.MinValue;

                OnDownloadProgressChanged?.Invoke(0, "Starting backend download...");

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                    totalRead += bytesRead;

                    if (canReportProgress && (DateTime.UtcNow - lastReportTime).TotalMilliseconds >= 500)
                    {
                        var percentage = Math.Round((double)totalRead / totalBytes * 100, 1);
                        var downloadedMb = Math.Round((double)totalRead / (1024 * 1024), 2);
                        var totalMb = Math.Round((double)totalBytes / (1024 * 1024), 2);

                        OnDownloadProgressChanged?.Invoke(percentage, $"Downloading Backend... {downloadedMb}MB / {totalMb}MB ({percentage}%)");
                        lastReportTime = DateTime.UtcNow;
                    }
                }

                OnDownloadProgressChanged?.Invoke(100, "Download complete. Extracting...");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
                throw new Exception($"Failed to download backend: {ex.Message}", ex);
            }

            // Unzip the file
            try
            {
                // We need to use System.IO.Compression.ZipFile
                System.IO.Compression.ZipFile.ExtractToDirectory(tempFilePath, AppConstants.BinDirectory, overwriteFiles: true);
                File.Delete(tempFilePath);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to extract backend zip: {ex.Message}", ex);
            }
        }
    }
}
