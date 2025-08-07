using Microsoft.Extensions.Logging;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// Service for comprehensive logging and audit trail management
    /// </summary>
    public class AuditService : IAuditService
    {
        private readonly ILogger<AuditService> _logger;
        private readonly string _auditLogPath;
        private readonly ConcurrentQueue<DeploymentAuditLog> _auditQueue;
        private readonly Timer _flushTimer;
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly object _lockObject = new object();

        public AuditService(ILogger<AuditService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Audit");
            _auditQueue = new ConcurrentQueue<DeploymentAuditLog>();
            _writeSemaphore = new SemaphoreSlim(1, 1);
            
            // Ensure audit log directory exists
            if (!Directory.Exists(_auditLogPath))
            {
                Directory.CreateDirectory(_auditLogPath);
            }

            // Setup periodic flush timer (every 30 seconds)
            _flushTimer = new Timer(FlushAuditLogs, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        /// <summary>
        /// Logs a deployment operation with comprehensive details
        /// </summary>
        public async Task LogDeploymentOperationAsync(DeploymentOperation operation, CancellationToken cancellationToken = default)
        {
            try
            {
                var auditLog = new DeploymentAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    Level = operation.Status == DeploymentStatus.Failed ? "Error" : "Information",
                    Category = "Deployment",
                    Message = $"Deployment operation {operation.Status.ToString().ToLower()} for application {operation.ApplicationName}",
                    ApplicationName = operation.ApplicationName,
                    OperationId = operation.Id,
                    Properties = new Dictionary<string, object>
                    {
                        { "OperationId", operation.Id },
                        { "ApplicationName", operation.ApplicationName },
                        { "ApplicationPoolName", operation.ApplicationPoolName },
                        { "Status", operation.Status.ToString() },
                        { "StartTime", operation.StartTime },
                        { "EndTime", operation.EndTime },
                        { "Duration", operation.Duration.ToString() },
                        { "FileChangeCount", operation.FileChanges.Count },
                        { "StepCount", operation.Steps.Count },
                        { "UserContext", operation.UserContext },
                        { "MachineName", operation.MachineName }
                    }
                };

                if (!string.IsNullOrEmpty(operation.ErrorMessage))
                {
                    auditLog.Properties["ErrorMessage"] = operation.ErrorMessage;
                }

                // Add file change details
                if (operation.FileChanges.Any())
                {
                    auditLog.Properties["FileChanges"] = operation.FileChanges.Select(fc => new
                    {
                        FilePath = fc.FilePath,
                        ChangeType = fc.ChangeType.ToString(),
                        DetectedAt = fc.DetectedAt,
                        FileSize = fc.FileSize
                    }).ToList();
                }

                // Add step details
                if (operation.Steps.Any())
                {
                    auditLog.Properties["Steps"] = operation.Steps.Select(s => new
                    {
                        Name = s.Name,
                        Status = s.Status.ToString(),
                        Duration = s.Duration.ToString(),
                        ErrorMessage = s.ErrorMessage
                    }).ToList();
                }

                await QueueAuditLogAsync(auditLog, cancellationToken);

                _logger.LogInformation("Deployment operation audit logged: {OperationId} - {Status}", 
                    operation.Id, operation.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log deployment operation audit for {OperationId}", operation.Id);
            }
        }

        /// <summary>
        /// Logs application pool operations with detailed metadata
        /// </summary>
        public async Task LogAppPoolOperationAsync(AppPoolOperation operation, CancellationToken cancellationToken = default)
        {
            try
            {
                var auditLog = new DeploymentAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    Level = operation.Success ? "Information" : "Warning",
                    Category = "ApplicationPool",
                    Message = $"Application pool {operation.Action.ToString().ToLower()} operation {(operation.Success ? "succeeded" : "failed")} for {operation.PoolName}",
                    Properties = new Dictionary<string, object>
                    {
                        { "PoolName", operation.PoolName },
                        { "Action", operation.Action.ToString() },
                        { "Success", operation.Success },
                        { "Duration", operation.Duration.ToString() },
                        { "Timestamp", operation.Timestamp },
                        { "PreviousState", operation.PreviousState?.ToString() ?? "Unknown" },
                        { "NewState", operation.NewState?.ToString() ?? "Unknown" }
                    }
                };

                if (!string.IsNullOrEmpty(operation.ErrorMessage))
                {
                    auditLog.Properties["ErrorMessage"] = operation.ErrorMessage;
                }

                await QueueAuditLogAsync(auditLog, cancellationToken);

                _logger.LogDebug("Application pool operation audit logged: {PoolName} - {Action} - {Success}", 
                    operation.PoolName, operation.Action, operation.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log application pool operation audit for {PoolName}", operation.PoolName);
            }
        }

        /// <summary>
        /// Logs permission validation results
        /// </summary>
        public async Task LogPermissionCheckAsync(PermissionValidationResult result, CancellationToken cancellationToken = default)
        {
            try
            {
                var auditLog = new DeploymentAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    Level = result.IsValid ? "Information" : "Warning",
                    Category = "PermissionValidation",
                    Message = $"Permission validation {(result.IsValid ? "passed" : "failed")}. {result.Checks.Count} checks performed",
                    Properties = new Dictionary<string, object>
                    {
                        { "IsValid", result.IsValid },
                        { "TotalChecks", result.Checks.Count },
                        { "PassedChecks", result.Checks.Count(c => c.Passed) },
                        { "FailedChecks", result.Checks.Count(c => !c.Passed) }
                    }
                };

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    auditLog.Properties["ErrorMessage"] = result.ErrorMessage;
                }

                // Add detailed check results
                auditLog.Properties["CheckResults"] = result.Checks.Select(c => new
                {
                    Name = c.Name,
                    Description = c.Description,
                    Passed = c.Passed,
                    RequiredLevel = c.RequiredLevel.ToString(),
                    ActualLevel = c.ActualLevel.ToString(),
                    ErrorMessage = c.ErrorMessage
                }).ToList();

                await QueueAuditLogAsync(auditLog, cancellationToken);

                _logger.LogInformation("Permission validation audit logged: {IsValid} - {PassedCount}/{TotalCount} checks passed", 
                    result.IsValid, result.Checks.Count(c => c.Passed), result.Checks.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log permission validation audit");
            }
        }

        /// <summary>
        /// Logs custom events with flexible properties
        /// </summary>
        public async Task LogCustomEventAsync(string category, string message, Dictionary<string, object>? properties = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var auditLog = new DeploymentAuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    Level = "Information",
                    Category = category,
                    Message = message,
                    Properties = properties ?? new Dictionary<string, object>()
                };

                await QueueAuditLogAsync(auditLog, cancellationToken);

                _logger.LogDebug("Custom audit event logged: {Category} - {Message}", category, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log custom audit event: {Category} - {Message}", category, message);
            }
        }

        /// <summary>
        /// Retrieves audit logs with filtering capabilities
        /// </summary>
        public async Task<List<DeploymentAuditLog>> GetAuditLogsAsync(DateTime? since = null, string? category = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var logs = new List<DeploymentAuditLog>();
                var startDate = since ?? DateTime.UtcNow.AddDays(-7); // Default to last 7 days

                // Get all audit log files in date range
                var auditFiles = Directory.GetFiles(_auditLogPath, "audit-*.json")
                    .Where(f => 
                    {
                        var fileName = Path.GetFileNameWithoutExtension(f);
                        if (fileName.StartsWith("audit-") && fileName.Length >= 16)
                        {
                            var dateStr = fileName.Substring(6, 10); // Extract YYYY-MM-DD
                            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                            {
                                return fileDate >= startDate.Date;
                            }
                        }
                        return false;
                    })
                    .OrderByDescending(f => f);

                foreach (var file in auditFiles)
                {
                    try
                    {
                        var fileContent = await File.ReadAllTextAsync(file, cancellationToken);
                        var lines = fileContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (var line in lines)
                        {
                            try
                            {
                                var log = JsonConvert.DeserializeObject<DeploymentAuditLog>(line);
                                if (log != null && 
                                    log.Timestamp >= startDate && 
                                    (string.IsNullOrEmpty(category) || log.Category.Equals(category, StringComparison.OrdinalIgnoreCase)))
                                {
                                    logs.Add(log);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to deserialize audit log line from {File}", file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read audit log file {File}", file);
                    }
                }

                return logs.OrderByDescending(l => l.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve audit logs");
                return new List<DeploymentAuditLog>();
            }
        }

        /// <summary>
        /// Queues an audit log entry for asynchronous writing
        /// </summary>
        private async Task QueueAuditLogAsync(DeploymentAuditLog auditLog, CancellationToken cancellationToken)
        {
            _auditQueue.Enqueue(auditLog);
            
            // If queue is getting large, flush immediately
            if (_auditQueue.Count > 100)
            {
                await FlushAuditLogsAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Timer callback for periodic audit log flushing
        /// </summary>
        private void FlushAuditLogs(object? state)
        {
            try
            {
                _ = Task.Run(async () => await FlushAuditLogsAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in audit log flush timer");
            }
        }

        /// <summary>
        /// Flushes queued audit logs to disk
        /// </summary>
        private async Task FlushAuditLogsAsync(CancellationToken cancellationToken)
        {
            if (_auditQueue.IsEmpty)
                return;

            await _writeSemaphore.WaitAsync(cancellationToken);
            
            try
            {
                var logsToWrite = new List<DeploymentAuditLog>();
                
                // Dequeue all pending logs
                while (_auditQueue.TryDequeue(out var log))
                {
                    logsToWrite.Add(log);
                }

                if (!logsToWrite.Any())
                    return;

                // Group logs by date for efficient file writing
                var logsByDate = logsToWrite.GroupBy(l => l.Timestamp.Date);

                foreach (var dateGroup in logsByDate)
                {
                    var fileName = $"audit-{dateGroup.Key:yyyy-MM-dd}.json";
                    var filePath = Path.Combine(_auditLogPath, fileName);
                    
                    var jsonLines = dateGroup.Select(log => JsonConvert.SerializeObject(log, Formatting.None));
                    
                    // Append to existing file or create new one
                    await File.AppendAllLinesAsync(filePath, jsonLines, cancellationToken);
                }

                _logger.LogDebug("Flushed {LogCount} audit logs to disk", logsToWrite.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush audit logs to disk");
                
                // Re-queue the logs if write failed
                foreach (var log in _auditQueue)
                {
                    _auditQueue.Enqueue(log);
                }
            }
            finally
            {
                _writeSemaphore.Release();
            }
        }

        /// <summary>
        /// Cleanup old audit log files
        /// </summary>
        public async Task CleanupOldAuditLogsAsync(int retainDays, CancellationToken cancellationToken = default)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-retainDays).Date;
                var auditFiles = Directory.GetFiles(_auditLogPath, "audit-*.json");

                foreach (var file in auditFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (fileName.StartsWith("audit-") && fileName.Length >= 16)
                        {
                            var dateStr = fileName.Substring(6, 10); // Extract YYYY-MM-DD
                            if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                            {
                                if (fileDate < cutoffDate)
                                {
                                    File.Delete(file);
                                    _logger.LogInformation("Deleted old audit log file: {File}", file);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete old audit log file: {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup old audit logs");
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _flushTimer?.Dispose();
            
            // Flush any remaining logs
            try
            {
                FlushAuditLogsAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing audit logs during disposal");
            }
            
            _writeSemaphore?.Dispose();
        }
    }
}
