using Microsoft.Extensions.Logging;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;
using System.Collections.Concurrent;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// System monitoring service with batch processing and intelligent change detection
    /// </summary>
    public class FileSystemMonitorService : IFileSystemMonitorService, IDisposable
    {
        private readonly ILogger<FileSystemMonitorService> _logger;
        private readonly IAuditService _auditService;
        private readonly List<FileSystemWatcher> _watchers;
        private readonly ConcurrentDictionary<string, FileChangeInfo> _pendingChanges;
        private readonly Timer _batchTimer;
        private readonly object _lockObject = new object();
        private readonly SemaphoreSlim _processingLock;
        
        private bool _isMonitoring;
        private int _batchDelayMs = 5000;
        private List<string> _excludePatterns = new();

        public event EventHandler<FileChangeInfo>? FileChanged;
        public event EventHandler<List<FileChangeInfo>>? BatchFileChanged;

        public bool IsMonitoring => _isMonitoring;

        public FileSystemMonitorService(ILogger<FileSystemMonitorService> logger, IAuditService auditService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _watchers = new List<FileSystemWatcher>();
            _pendingChanges = new ConcurrentDictionary<string, FileChangeInfo>();
            _processingLock = new SemaphoreSlim(1, 1);
            
            // Initialize batch timer (will be started when monitoring begins)
            _batchTimer = new Timer(ProcessBatchChanges, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Starts monitoring specified paths with exclusion patterns
        /// </summary>
        public async Task StartMonitoringAsync(List<string> watchPaths, List<string> excludePatterns, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_isMonitoring)
                {
                    _logger.LogWarning("File system monitoring is already active");
                    return;
                }

                _logger.LogInformation("Starting file system monitoring for {PathCount} paths", watchPaths.Count);

                _excludePatterns = excludePatterns ?? new List<string>();
                
                // Validate and setup watchers for each path
                foreach (var path in watchPaths)
                {
                    await SetupWatcherAsync(path, cancellationToken);
                }

                if (_watchers.Any())
                {
                    _isMonitoring = true;
                    
                    // Start the batch processing timer
                    _batchTimer.Change(_batchDelayMs, Timeout.Infinite);
                    
                    await _auditService.LogCustomEventAsync("FileSystemMonitor", 
                        $"File system monitoring started for {_watchers.Count} paths", 
                        new Dictionary<string, object>
                        {
                            { "WatchedPaths", watchPaths },
                            { "ExcludePatterns", excludePatterns },
                            { "ActiveWatchers", _watchers.Count }
                        }, 
                        cancellationToken);

                    _logger.LogInformation("File system monitoring started successfully. Watching {WatcherCount} paths", _watchers.Count);
                }
                else
                {
                    _logger.LogWarning("No valid paths to monitor. File system monitoring not started");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start file system monitoring");
                await StopMonitoringAsync(cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Sets up a file system watcher for a specific path
        /// </summary>
        private async Task SetupWatcherAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    _logger.LogWarning("Watch path does not exist: {Path}", path);
                    return;
                }

                _logger.LogDebug("Setting up file system watcher for path: {Path}", path);

                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                                 NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = false
                };

                // Subscribe to events
                watcher.Created += OnFileSystemEvent;
                watcher.Changed += OnFileSystemEvent;
                watcher.Deleted += OnFileSystemEvent;
                watcher.Renamed += OnFileSystemRenamed;
                watcher.Error += OnFileSystemError;

                // Start monitoring
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);

                _logger.LogInformation("File system watcher started for path: {Path}", path);
                
                await _auditService.LogCustomEventAsync("FileSystemMonitor", 
                    $"Watcher setup completed for path: {path}", 
                    new Dictionary<string, object> { { "Path", path } }, 
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup file system watcher for path: {Path}", path);
            }
        }

        /// <summary>
        /// Stops all file system monitoring
        /// </summary>
        public async Task StopMonitoringAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_isMonitoring)
                {
                    _logger.LogDebug("File system monitoring is not active");
                    return;
                }

                _logger.LogInformation("Stopping file system monitoring");

                _isMonitoring = false;

                // Stop the batch timer
                _batchTimer.Change(Timeout.Infinite, Timeout.Infinite);

                // Process any remaining pending changes
                await ProcessBatchChangesAsync(cancellationToken);

                // Dispose all watchers
                lock (_lockObject)
                {
                    foreach (var watcher in _watchers)
                    {
                        try
                        {
                            watcher.EnableRaisingEvents = false;
                            watcher.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error disposing file system watcher");
                        }
                    }
                    _watchers.Clear();
                }

                await _auditService.LogCustomEventAsync("FileSystemMonitor", 
                    "File system monitoring stopped", 
                    new Dictionary<string, object> { { "PendingChanges", _pendingChanges.Count } }, 
                    cancellationToken);

                _logger.LogInformation("File system monitoring stopped successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping file system monitoring");
            }
        }

        /// <summary>
        /// Handles file system events (Created, Changed, Deleted)
        /// </summary>
        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFileSystemEventAsync(e.FullPath, ConvertChangeType(e.ChangeType));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file system event for {Path}", e.FullPath);
                }
            });
        }

        /// <summary>
        /// Handles file system rename events
        /// </summary>
        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFileSystemEventAsync(e.FullPath, FileChangeType.Renamed, e.OldFullPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file system rename event for {Path}", e.FullPath);
                }
            });
        }

        /// <summary>
        /// Handles file system watcher errors
        /// </summary>
        private void OnFileSystemError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.GetException(), "File system watcher error occurred");
            
            _ = Task.Run(async () =>
            {
                await _auditService.LogCustomEventAsync("FileSystemMonitor", 
                    "File system watcher error", 
                    new Dictionary<string, object> 
                    { 
                        { "Error", e.GetException().Message },
                        { "StackTrace", e.GetException().StackTrace ?? "" }
                    });
            });
        }

        /// <summary>
        /// Processes individual file system events
        /// </summary>
        private async Task ProcessFileSystemEventAsync(string filePath, FileChangeType changeType, string? oldFilePath = null)
        {
            try
            {
                // Check if file should be excluded
                if (FileSystemUtils.IsFileExcluded(filePath, _excludePatterns))
                {
                    _logger.LogTrace("File excluded from monitoring: {Path}", filePath);
                    return;
                }

                // Create file change info
                var fileInfo = new FileChangeInfo
                {
                    FilePath = filePath,
                    ChangeType = changeType,
                    DetectedAt = DateTime.UtcNow,
                    OldFilePath = oldFilePath
                };

                // Try to get file size and hash for existing files
                if (changeType != FileChangeType.Deleted && File.Exists(filePath))
                {
                    try
                    {
                        var fileInfoObj = new FileInfo(filePath);
                        fileInfo.FileSize = fileInfoObj.Length;
                        fileInfo.FileHash = await FileSystemUtils.GetFileHashAsync(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Could not get file info for {Path}", filePath);
                    }
                }

                // Add to pending changes (this will replace any existing entry for the same file)
                _pendingChanges.AddOrUpdate(filePath, fileInfo, (key, existing) => fileInfo);

                // Fire individual file changed event
                FileChanged?.Invoke(this, fileInfo);

                _logger.LogTrace("File change detected: {ChangeType} - {Path}", changeType, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file system event for {Path}", filePath);
            }
        }

        /// <summary>
        /// Timer callback for batch processing
        /// </summary>
        private void ProcessBatchChanges(object? state)
        {
            if (!_isMonitoring)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessBatchChangesAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in batch change processing");
                }
                finally
                {
                    // Schedule next batch processing if still monitoring
                    if (_isMonitoring)
                    {
                        _batchTimer.Change(_batchDelayMs, Timeout.Infinite);
                    }
                }
            });
        }

        /// <summary>
        /// Processes batch of file changes
        /// </summary>
        private async Task ProcessBatchChangesAsync(CancellationToken cancellationToken)
        {
            if (_pendingChanges.IsEmpty)
                return;

            await _processingLock.WaitAsync(cancellationToken);
            
            try
            {
                var changes = new List<FileChangeInfo>();
                
                // Collect all pending changes
                var keys = _pendingChanges.Keys.ToList();
                foreach (var key in keys)
                {
                    if (_pendingChanges.TryRemove(key, out var change))
                    {
                        changes.Add(change);
                    }
                }

                if (changes.Any())
                {
                    _logger.LogInformation("Processing batch of {ChangeCount} file changes", changes.Count);

                    // Group changes by change type for logging
                    var changeGroups = changes.GroupBy(c => c.ChangeType);
                    foreach (var group in changeGroups)
                    {
                        _logger.LogDebug("{ChangeType}: {Count} files", group.Key, group.Count());
                    }

                    // Fire batch event
                    BatchFileChanged?.Invoke(this, changes);

                    // Log the batch processing
                    await _auditService.LogCustomEventAsync("FileSystemMonitor", 
                        $"Batch processed {changes.Count} file changes", 
                        new Dictionary<string, object>
                        {
                            { "ChangeCount", changes.Count },
                            { "ChangesByType", changeGroups.ToDictionary(g => g.Key.ToString(), g => g.Count()) },
                            { "ProcessedAt", DateTime.UtcNow }
                        }, 
                        cancellationToken);
                }
            }
            finally
            {
                _processingLock.Release();
            }
        }

        /// <summary>
        /// Manually detects changes in a path since a specific date
        /// </summary>
        public async Task<List<FileChangeInfo>> DetectChangesAsync(string path, DateTime? since = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var changes = new List<FileChangeInfo>();
                var sinceDate = since ?? DateTime.UtcNow.AddMinutes(-5); // Default to last 5 minutes

                if (!Directory.Exists(path))
                {
                    _logger.LogWarning("Path does not exist for change detection: {Path}", path);
                    return changes;
                }

                _logger.LogDebug("Detecting changes in {Path} since {Since}", path, sinceDate);

                var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
                
                foreach (var file in files)
                {
                    try
                    {
                        if (FileSystemUtils.IsFileExcluded(file, _excludePatterns))
                            continue;

                        var fileInfo = new FileInfo(file);
                        
                        // Check if file was modified since the specified date
                        if (fileInfo.LastWriteTime > sinceDate || fileInfo.CreationTime > sinceDate)
                        {
                            var changeInfo = new FileChangeInfo
                            {
                                FilePath = file,
                                ChangeType = fileInfo.CreationTime > sinceDate ? FileChangeType.Created : FileChangeType.Modified,
                                DetectedAt = DateTime.UtcNow,
                                FileSize = fileInfo.Length,
                                FileHash = await FileSystemUtils.GetFileHashAsync(file)
                            };

                            changes.Add(changeInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error checking file {File} for changes", file);
                    }
                }

                _logger.LogInformation("Detected {ChangeCount} changes in {Path} since {Since}", 
                    changes.Count, path, sinceDate);

                return changes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting changes in path {Path}", path);
                return new List<FileChangeInfo>();
            }
        }

        /// <summary>
        /// Sets the batch delay for grouping file changes
        /// </summary>
        public void SetBatchDelay(int delayMs)
        {
            if (delayMs < 1000 || delayMs > 60000)
            {
                throw new ArgumentOutOfRangeException(nameof(delayMs), "Batch delay must be between 1000 and 60000 milliseconds");
            }

            _batchDelayMs = delayMs;
            _logger.LogDebug("Batch delay set to {DelayMs} milliseconds", delayMs);
        }

        /// <summary>
        /// Converts FileSystemWatcher ChangeType to our FileChangeType
        /// </summary>
        private static FileChangeType ConvertChangeType(WatcherChangeTypes changeType)
        {
            return changeType switch
            {
                WatcherChangeTypes.Created => FileChangeType.Created,
                WatcherChangeTypes.Changed => FileChangeType.Modified,
                WatcherChangeTypes.Deleted => FileChangeType.Deleted,
                WatcherChangeTypes.Renamed => FileChangeType.Renamed,
                _ => FileChangeType.Modified
            };
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            try
            {
                StopMonitoringAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during file system monitor disposal");
            }

            _batchTimer?.Dispose();
            _processingLock?.Dispose();
        }
    }
}
