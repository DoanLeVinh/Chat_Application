using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace ChatServer.Services
{
    public class FileCleanupBackgroundService : BackgroundService
    {
        private readonly ILogger<FileCleanupBackgroundService> _logger;
        private readonly string _uploadPath;
        private readonly int _daysToKeep;
        private readonly TimeSpan _checkInterval;

        public FileCleanupBackgroundService(ILogger<FileCleanupBackgroundService> logger, IConfiguration configuration)
        {
            _logger = logger;
            // Get upload path from configuration or default to "wwwroot/uploads"
            _uploadPath = configuration["UploadSettings:Path"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            
            // Keep files for 7 days by default
            _daysToKeep = int.Parse(configuration["UploadSettings:DaysToKeep"] ?? "7");
            
            // Run cleanup every 24 hours
            _checkInterval = TimeSpan.FromHours(24);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🧹 File Cleanup Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("🧹 Starting cleanup cycle...");
                    CleanupOldFiles();
                    _logger.LogInformation("✅ Cleanup cycle completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error occurred during file cleanup.");
                }

                // Wait for next cycle
                await Task.Delay(_checkInterval, stoppingToken);
            }

            _logger.LogInformation("🧹 File Cleanup Service is stopping.");
        }

        private void CleanupOldFiles()
        {
            if (!Directory.Exists(_uploadPath))
            {
                _logger.LogWarning($"⚠️ Upload directory not found: {_uploadPath}");
                return;
            }

            var cutoffTime = DateTime.Now.AddDays(-_daysToKeep);
            var files = Directory.GetFiles(_uploadPath);
            int deletedCount = 0;
            long freedSpace = 0;

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    // Check if file is older than cutoff time
                    if (fileInfo.CreationTime < cutoffTime)
                    {
                        long size = fileInfo.Length;
                        fileInfo.Delete();
                        deletedCount++;
                        freedSpace += size;
                        _logger.LogDebug($"🗑️ Deleted old file: {fileInfo.Name} (Size: {size} bytes)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"❌ Failed to delete file: {file}");
                }
            }

            if (deletedCount > 0)
            {
                double freedMb = freedSpace / (1024.0 * 1024.0);
                _logger.LogInformation($"🧹 Cleanup Result: Deleted {deletedCount} files, Freed {freedMb:F2} MB");
            }
            else
            {
                _logger.LogInformation("🧹 No old files found to delete.");
            }
        }
    }
}
