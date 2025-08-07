using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace IISDeploymentAutomation.Models
{
    /// <summary>
    /// Represents a deployment operation
    /// </summary>
    public class DeploymentOperation
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ApplicationName { get; set; } = string.Empty;
        public string ApplicationPoolName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;
        public List<FileChangeInfo> FileChanges { get; set; } = new();
        public List<DeploymentStep> Steps { get; set; } = new();
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public string UserContext { get; set; } = Environment.UserName;
        public string MachineName { get; set; } = Environment.MachineName;
        
        [JsonIgnore]
        public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Represents file change information
    /// </summary>
    public class FileChangeInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public FileChangeType ChangeType { get; set; }
        public DateTime DetectedAt { get; set; }
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string? OldFilePath { get; set; } // For rename operations
    }

    /// <summary>
    /// Represents a deployment step
    /// </summary>
    public class DeploymentStep
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public StepStatus Status { get; set; } = StepStatus.Pending;
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        
        [JsonIgnore]
        public TimeSpan Duration => EndTime?.Subtract(StartTime) ?? TimeSpan.Zero;
    }

    /// <summary>
    /// Application pool operation details
    /// </summary>
    public class AppPoolOperation
    {
        public string PoolName { get; set; } = string.Empty;
        public AppPoolAction Action { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
        public AppPoolState? PreviousState { get; set; }
        public AppPoolState? NewState { get; set; }
    }

    /// <summary>
    /// Permission validation result
    /// </summary>
    public class PermissionValidationResult
    {
        public bool IsValid { get; set; }
        public List<PermissionCheck> Checks { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Individual permission check
    /// </summary>
    public class PermissionCheck
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Passed { get; set; }
        public string? ErrorMessage { get; set; }
        public PermissionLevel RequiredLevel { get; set; }
        public PermissionLevel ActualLevel { get; set; }
    }

    /// <summary>
    /// Health check result
    /// </summary>
    public class HealthCheckResult
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public bool IsHealthy { get; set; }
        public int ResponseTimeMs { get; set; }
        public int StatusCode { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CheckTime { get; set; }
    }

    /// <summary>
    /// Deployment audit log entry
    /// </summary>
    public class DeploymentAuditLog
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Level { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ApplicationName { get; set; }
        public string? OperationId { get; set; }
        public Dictionary<string, object> Properties { get; set; } = new();
        public string UserContext { get; set; } = Environment.UserName;
        public string MachineName { get; set; } = Environment.MachineName;
    }

    /// <summary>
    /// Deployment statistics
    /// </summary>
    public class DeploymentStatistics
    {
        public int TotalDeployments { get; set; }
        public int SuccessfulDeployments { get; set; }
        public int FailedDeployments { get; set; }
        public TimeSpan AverageDeploymentTime { get; set; }
        public DateTime LastDeployment { get; set; }
        public Dictionary<string, int> ApplicationDeploymentCounts { get; set; } = new();
        public Dictionary<string, TimeSpan> ApplicationAverageDeploymentTimes { get; set; } = new();
    }

    #region Enums

    public enum DeploymentStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Cancelled,
        PartiallyCompleted
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted,
        Renamed
    }

    public enum StepStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    public enum AppPoolAction
    {
        Start,
        Stop,
        Restart,
        Recycle
    }

    public enum AppPoolState
    {
        Starting,
        Started,
        Stopping,
        Stopped,
        Unknown
    }

    public enum PermissionLevel
    {
        None,
        Read,
        Write,
        Admin,
        Full
    }

    #endregion
}
