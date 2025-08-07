using IISDeploymentAutomation.Models;

namespace IISDeploymentAutomation.Interfaces
{
    /// <summary>
    /// Interface for IIS management operations
    /// </summary>
    public interface IIISManagerService
    {
        Task<bool> StopApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default);
        Task<bool> StartApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default);
        Task<bool> RestartApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default);
        Task<AppPoolState> GetApplicationPoolStateAsync(string poolName, CancellationToken cancellationToken = default);
        Task<bool> ApplicationPoolExistsAsync(string poolName, CancellationToken cancellationToken = default);
        Task<List<string>> GetApplicationPoolsAsync(CancellationToken cancellationToken = default);
        Task<bool> ValidateIISAccessAsync(CancellationToken cancellationToken = default);
        Task<List<string>> GetSitesAsync(CancellationToken cancellationToken = default);
        Task<bool> SiteExistsAsync(string siteName, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for file system monitoring and change detection
    /// </summary>
    public interface IFileSystemMonitorService
    {
        event EventHandler<FileChangeInfo> FileChanged;
        event EventHandler<List<FileChangeInfo>> BatchFileChanged;
        
        Task StartMonitoringAsync(List<string> watchPaths, List<string> excludePatterns, CancellationToken cancellationToken = default);
        Task StopMonitoringAsync(CancellationToken cancellationToken = default);
        bool IsMonitoring { get; }
        Task<List<FileChangeInfo>> DetectChangesAsync(string path, DateTime? since = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for deployment operations
    /// </summary>
    public interface IDeploymentService
    {
        Task<DeploymentOperation> StartDeploymentAsync(ApplicationConfiguration appConfig, List<FileChangeInfo> fileChanges, CancellationToken cancellationToken = default);
        Task<bool> ExecuteDeploymentAsync(DeploymentOperation operation, CancellationToken cancellationToken = default);
        Task<List<DeploymentOperation>> GetActiveDeploymentsAsync(CancellationToken cancellationToken = default);
        Task<DeploymentOperation?> GetDeploymentAsync(string operationId, CancellationToken cancellationToken = default);
        Task CancelDeploymentAsync(string operationId, CancellationToken cancellationToken = default);
        Task<DeploymentStatistics> GetStatisticsAsync(DateTime? since = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for permission validation
    /// </summary>
    public interface IPermissionValidationService
    {
        Task<PermissionValidationResult> ValidatePermissionsAsync(CancellationToken cancellationToken = default);
        Task<bool> HasAdminPrivilegesAsync(CancellationToken cancellationToken = default);
        Task<bool> HasIISAccessAsync(CancellationToken cancellationToken = default);
        Task<bool> HasFileSystemAccessAsync(string path, CancellationToken cancellationToken = default);
        Task<bool> CanControlApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for configuration management
    /// </summary>
    public interface IConfigurationService
    {
        Task<DeploymentConfiguration> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default);
        Task SaveConfigurationAsync(DeploymentConfiguration config, string? configPath = null, CancellationToken cancellationToken = default);
        Task<bool> ValidateConfigurationAsync(DeploymentConfiguration config, CancellationToken cancellationToken = default);
        Task<DeploymentConfiguration> GetDefaultConfigurationAsync(CancellationToken cancellationToken = default);
        string DefaultConfigPath { get; }
    }

    /// <summary>
    /// Interface for notification services
    /// </summary>
    public interface INotificationService
    {
        Task SendNotificationAsync(string subject, string message, NotificationType type, CancellationToken cancellationToken = default);
        Task SendDeploymentStartedAsync(DeploymentOperation operation, CancellationToken cancellationToken = default);
        Task SendDeploymentCompletedAsync(DeploymentOperation operation, CancellationToken cancellationToken = default);
        Task SendDeploymentFailedAsync(DeploymentOperation operation, CancellationToken cancellationToken = default);
        Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for health check services
    /// </summary>
    public interface IHealthCheckService
    {
        Task<HealthCheckResult> CheckApplicationHealthAsync(ApplicationConfiguration appConfig, CancellationToken cancellationToken = default);
        Task<List<HealthCheckResult>> CheckAllApplicationsHealthAsync(List<ApplicationConfiguration> appConfigs, CancellationToken cancellationToken = default);
        Task<bool> WaitForApplicationHealthyAsync(ApplicationConfiguration appConfig, TimeSpan timeout, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for logging and audit services
    /// </summary>
    public interface IAuditService
    {
        Task LogDeploymentOperationAsync(DeploymentOperation operation, CancellationToken cancellationToken = default);
        Task LogAppPoolOperationAsync(AppPoolOperation operation, CancellationToken cancellationToken = default);
        Task LogPermissionCheckAsync(PermissionValidationResult result, CancellationToken cancellationToken = default);
        Task LogCustomEventAsync(string category, string message, Dictionary<string, object>? properties = null, CancellationToken cancellationToken = default);
        Task<List<DeploymentAuditLog>> GetAuditLogsAsync(DateTime? since = null, string? category = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Interface for backup services
    /// </summary>
    public interface IBackupService
    {
        Task<string> CreateBackupAsync(ApplicationConfiguration appConfig, CancellationToken cancellationToken = default);
        Task<bool> RestoreBackupAsync(string backupPath, ApplicationConfiguration appConfig, CancellationToken cancellationToken = default);
        Task CleanupOldBackupsAsync(int retainDays, CancellationToken cancellationToken = default);
        Task<List<string>> GetAvailableBackupsAsync(string applicationName, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Main orchestration service interface
    /// </summary>
    public interface IDeploymentOrchestrationService
    {
        Task StartOrchestrationAsync(CancellationToken cancellationToken = default);
        Task StopOrchestrationAsync(CancellationToken cancellationToken = default);
        Task TriggerManualDeploymentAsync(string applicationName, CancellationToken cancellationToken = default);
        Task<List<DeploymentOperation>> GetCurrentOperationsAsync(CancellationToken cancellationToken = default);
        Task<DeploymentStatistics> GetOverallStatisticsAsync(CancellationToken cancellationToken = default);
        bool IsRunning { get; }
    }

    public enum NotificationType
    {
        Information,
        Warning,
        Error,
        Success
    }
}
