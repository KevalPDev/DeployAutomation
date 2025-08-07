using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace IISDeploymentAutomation.Models
{
    /// <summary>
    /// Main deployment configuration model containing all application and deployment settings
    /// </summary>
    public class DeploymentConfiguration
    {
        [Required]
        [JsonProperty("applications")]
        public List<ApplicationConfiguration> Applications { get; set; } = new();

        [JsonProperty("globalSettings")]
        public GlobalSettings GlobalSettings { get; set; } = new();

        [JsonProperty("notificationSettings")]
        public NotificationSettings NotificationSettings { get; set; } = new();

        [JsonProperty("logging")]
        public LoggingConfiguration Logging { get; set; } = new();
    }

    /// <summary>
    /// Configuration for individual applications
    /// </summary>
    public class ApplicationConfiguration
    {
        [Required]
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [JsonProperty("applicationPoolName")]
        public string ApplicationPoolName { get; set; } = string.Empty;

        [Required]
        [JsonProperty("siteName")]
        public string SiteName { get; set; } = string.Empty;

        [Required]
        [JsonProperty("sourcePath")]
        public string SourcePath { get; set; } = string.Empty;

        [Required]
        [JsonProperty("destinationPath")]
        public string DestinationPath { get; set; } = string.Empty;

        [JsonProperty("watchFolders")]
        public List<string> WatchFolders { get; set; } = new();

        [JsonProperty("excludePatterns")]
        public List<string> ExcludePatterns { get; set; } = new();

        [JsonProperty("preDeploymentSteps")]
        public List<string> PreDeploymentSteps { get; set; } = new();

        [JsonProperty("postDeploymentSteps")]
        public List<string> PostDeploymentSteps { get; set; } = new();

        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("enableFileCopy")]
        public bool EnableFileCopy { get; set; } = true;

        [JsonProperty("priority")]
        public int Priority { get; set; } = 1;

        [JsonProperty("maxRetries")]
        public int MaxRetries { get; set; } = 3;

        [JsonProperty("timeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 300;
    }

    /// <summary>
    /// Global deployment settings
    /// </summary>
    public class GlobalSettings
    {
        [JsonProperty("enableFileSystemWatcher")]
        public bool EnableFileSystemWatcher { get; set; } = true;

        [JsonProperty("batchDelayMilliseconds")]
        public int BatchDelayMilliseconds { get; set; } = 5000;

        [JsonProperty("maxConcurrentDeployments")]
        public int MaxConcurrentDeployments { get; set; } = 3;

        [JsonProperty("backupEnabled")]
        public bool BackupEnabled { get; set; } = true;

        [JsonProperty("backupPath")]
        public string BackupPath { get; set; } = @"C:\DeploymentBackups";

        [JsonProperty("maxBackupDays")]
        public int MaxBackupDays { get; set; } = 7;

        [JsonProperty("requireAdminPrivileges")]
        public bool RequireAdminPrivileges { get; set; } = true;

        [JsonProperty("validateIISPermissions")]
        public bool ValidateIISPermissions { get; set; } = true;

        [JsonProperty("enableHealthCheck")]
        public bool EnableHealthCheck { get; set; } = true;

        [JsonProperty("healthCheckTimeoutSeconds")]
        public int HealthCheckTimeoutSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Notification configuration for deployment events
    /// </summary>
    public class NotificationSettings
    {
        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonProperty("emailSettings")]
        public EmailSettings? EmailSettings { get; set; }

        [JsonProperty("teamsSettings")]
        public TeamsSettings? TeamsSettings { get; set; }

        [JsonProperty("smsSettings")]
        public SmsSettings? SmsSettings { get; set; }

        [JsonProperty("notifyOnSuccess")]
        public bool NotifyOnSuccess { get; set; } = false;

        [JsonProperty("notifyOnFailure")]
        public bool NotifyOnFailure { get; set; } = true;

        [JsonProperty("notifyOnStart")]
        public bool NotifyOnStart { get; set; } = false;
    }

    public class EmailSettings
    {
        [JsonProperty("smtpServer")]
        public string SmtpServer { get; set; } = string.Empty;

        [JsonProperty("port")]
        public int Port { get; set; } = 587;

        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("password")]
        public string Password { get; set; } = string.Empty;

        [JsonProperty("fromEmail")]
        public string FromEmail { get; set; } = string.Empty;

        [JsonProperty("toEmails")]
        public List<string> ToEmails { get; set; } = new();

        [JsonProperty("enableSsl")]
        public bool EnableSsl { get; set; } = true;
    }

    public class TeamsSettings
    {
        [JsonProperty("webhookUrl")]
        public string WebhookUrl { get; set; } = string.Empty;

        [JsonProperty("channelName")]
        public string ChannelName { get; set; } = string.Empty;
    }

    public class SmsSettings
    {
        [JsonProperty("provider")]
        public string Provider { get; set; } = string.Empty;

        [JsonProperty("apiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonProperty("phoneNumbers")]
        public List<string> PhoneNumbers { get; set; } = new();
    }

    /// <summary>
    /// Logging configuration
    /// </summary>
    public class LoggingConfiguration
    {
        [JsonProperty("logLevel")]
        public string LogLevel { get; set; } = "Information";

        [JsonProperty("logToFile")]
        public bool LogToFile { get; set; } = true;

        [JsonProperty("logToConsole")]
        public bool LogToConsole { get; set; } = true;

        [JsonProperty("logFilePath")]
        public string LogFilePath { get; set; } = @".\Logs\deployment-{Date}.log";

        [JsonProperty("retainDays")]
        public int RetainDays { get; set; } = 30;

        [JsonProperty("maxFileSizeMB")]
        public int MaxFileSizeMB { get; set; } = 100;

        [JsonProperty("enableStructuredLogging")]
        public bool EnableStructuredLogging { get; set; } = true;
    }
}
