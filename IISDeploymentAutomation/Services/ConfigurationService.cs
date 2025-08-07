using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;
using System.ComponentModel.DataAnnotations;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// Configuration management service with validation and default configuration generation
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly string _defaultConfigPath;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _defaultConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        }

        public string DefaultConfigPath => _defaultConfigPath;

        /// <summary>
        /// Loads deployment configuration from file
        /// </summary>
        public async Task<DeploymentConfiguration> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default)
        {
            var filePath = configPath ?? _defaultConfigPath;
            
            try
            {
                _logger.LogInformation("Loading configuration from {ConfigPath}", filePath);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("Configuration file {ConfigPath} not found. Creating default configuration", filePath);
                    var defaultConfig = await GetDefaultConfigurationAsync(cancellationToken);
                    await SaveConfigurationAsync(defaultConfig, filePath, cancellationToken);
                    return defaultConfig;
                }

                var jsonContent = await File.ReadAllTextAsync(filePath, cancellationToken);
                
                if (string.IsNullOrWhiteSpace(jsonContent))
                {
                    _logger.LogWarning("Configuration file {ConfigPath} is empty. Using default configuration", filePath);
                    return await GetDefaultConfigurationAsync(cancellationToken);
                }

                var config = JsonConvert.DeserializeObject<DeploymentConfiguration>(jsonContent);
                
                if (config == null)
                {
                    _logger.LogError("Failed to deserialize configuration from {ConfigPath}. Using default configuration", filePath);
                    return await GetDefaultConfigurationAsync(cancellationToken);
                }

                // Validate the loaded configuration
                var isValid = await ValidateConfigurationAsync(config, cancellationToken);
                if (!isValid)
                {
                    _logger.LogError("Configuration validation failed for {ConfigPath}. Using default configuration", filePath);
                    return await GetDefaultConfigurationAsync(cancellationToken);
                }

                _logger.LogInformation("Configuration loaded successfully from {ConfigPath}. Found {AppCount} applications", 
                    filePath, config.Applications.Count);

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration from {ConfigPath}. Using default configuration", filePath);
                return await GetDefaultConfigurationAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Saves deployment configuration to file
        /// </summary>
        public async Task SaveConfigurationAsync(DeploymentConfiguration config, string? configPath = null, CancellationToken cancellationToken = default)
        {
            var filePath = configPath ?? _defaultConfigPath;
            
            try
            {
                _logger.LogInformation("Saving configuration to {ConfigPath}", filePath);

                // Validate before saving
                var isValid = await ValidateConfigurationAsync(config, cancellationToken);
                if (!isValid)
                {
                    throw new InvalidOperationException("Cannot save invalid configuration");
                }

                var jsonContent = JsonConvert.SerializeObject(config, Formatting.Indented);
                
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(filePath, jsonContent, cancellationToken);
                
                _logger.LogInformation("Configuration saved successfully to {ConfigPath}", filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save configuration to {ConfigPath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// Validates deployment configuration
        /// </summary>
        public async Task<bool> ValidateConfigurationAsync(DeploymentConfiguration config, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Validating deployment configuration");

                if (config == null)
                {
                    _logger.LogError("Configuration is null");
                    return false;
                }

                var validationResults = new List<ValidationResult>();
                var validationContext = new ValidationContext(config);
                
                // Validate root configuration
                if (!Validator.TryValidateObject(config, validationContext, validationResults, true))
                {
                    foreach (var result in validationResults)
                    {
                        _logger.LogError("Configuration validation error: {ErrorMessage}", result.ErrorMessage);
                    }
                    return false;
                }

                // Validate each application configuration
                for (int i = 0; i < config.Applications.Count; i++)
                {
                    var app = config.Applications[i];
                    if (!await ValidateApplicationConfigurationAsync(app, i, cancellationToken))
                    {
                        return false;
                    }
                }

                // Validate global settings
                if (!ValidateGlobalSettings(config.GlobalSettings))
                {
                    return false;
                }

                // Validate logging configuration
                if (!ValidateLoggingConfiguration(config.Logging))
                {
                    return false;
                }

                // Validate notification settings if enabled
                if (config.NotificationSettings.Enabled && !ValidateNotificationSettings(config.NotificationSettings))
                {
                    return false;
                }

                _logger.LogInformation("Configuration validation completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Configuration validation failed with exception");
                return false;
            }
        }

        /// <summary>
        /// Validates individual application configuration
        /// </summary>
        private async Task<bool> ValidateApplicationConfigurationAsync(ApplicationConfiguration app, int index, CancellationToken cancellationToken)
        {
            _logger.LogDebug("Validating application configuration {Index}: {AppName}", index, app.Name);

            // Validate application pool name
            var poolNameValidation = ValidationUtils.ValidateApplicationPoolName(app.ApplicationPoolName);
            if (!poolNameValidation.IsValid)
            {
                _logger.LogError("Invalid application pool name for app {AppName}: {Error}", app.Name, poolNameValidation.ErrorMessage);
                return false;
            }

            // Validate site name
            var siteNameValidation = ValidationUtils.ValidateSiteName(app.SiteName);
            if (!siteNameValidation.IsValid)
            {
                _logger.LogError("Invalid site name for app {AppName}: {Error}", app.Name, siteNameValidation.ErrorMessage);
                return false;
            }

            // Validate source path
            var sourcePathValidation = ValidationUtils.ValidatePath(app.SourcePath, true);
            if (!sourcePathValidation.IsValid)
            {
                _logger.LogError("Invalid source path for app {AppName}: {Error}", app.Name, sourcePathValidation.ErrorMessage);
                return false;
            }

            // Validate destination path (might not exist yet)
            var destPathValidation = ValidationUtils.ValidatePath(app.DestinationPath, false);
            if (!destPathValidation.IsValid)
            {
                _logger.LogError("Invalid destination path for app {AppName}: {Error}", app.Name, destPathValidation.ErrorMessage);
                return false;
            }

            // Validate watch folders
            foreach (var watchFolder in app.WatchFolders)
            {
                var watchPathValidation = ValidationUtils.ValidatePath(watchFolder, true);
                if (!watchPathValidation.IsValid)
                {
                    _logger.LogError("Invalid watch folder for app {AppName}: {Path} - {Error}", app.Name, watchFolder, watchPathValidation.ErrorMessage);
                    return false;
                }
            }

            // Validate timeout and retry settings
            if (app.TimeoutSeconds <= 0 || app.TimeoutSeconds > 3600)
            {
                _logger.LogError("Invalid timeout seconds for app {AppName}: {Timeout}. Must be between 1 and 3600", app.Name, app.TimeoutSeconds);
                return false;
            }

            if (app.MaxRetries < 0 || app.MaxRetries > 10)
            {
                _logger.LogError("Invalid max retries for app {AppName}: {MaxRetries}. Must be between 0 and 10", app.Name, app.MaxRetries);
                return false;
            }

            if (app.Priority < 1 || app.Priority > 10)
            {
                _logger.LogError("Invalid priority for app {AppName}: {Priority}. Must be between 1 and 10", app.Name, app.Priority);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates global settings
        /// </summary>
        private bool ValidateGlobalSettings(GlobalSettings settings)
        {
            if (settings.BatchDelayMilliseconds < 1000 || settings.BatchDelayMilliseconds > 60000)
            {
                _logger.LogError("Invalid batch delay: {Delay}. Must be between 1000 and 60000 milliseconds", settings.BatchDelayMilliseconds);
                return false;
            }

            if (settings.MaxConcurrentDeployments < 1 || settings.MaxConcurrentDeployments > 10)
            {
                _logger.LogError("Invalid max concurrent deployments: {Max}. Must be between 1 and 10", settings.MaxConcurrentDeployments);
                return false;
            }

            if (settings.BackupEnabled)
            {
                var backupPathValidation = ValidationUtils.ValidatePath(settings.BackupPath, false);
                if (!backupPathValidation.IsValid)
                {
                    _logger.LogError("Invalid backup path: {Error}", backupPathValidation.ErrorMessage);
                    return false;
                }
            }

            if (settings.MaxBackupDays < 1 || settings.MaxBackupDays > 365)
            {
                _logger.LogError("Invalid max backup days: {Days}. Must be between 1 and 365", settings.MaxBackupDays);
                return false;
            }

            if (settings.HealthCheckTimeoutSeconds < 10 || settings.HealthCheckTimeoutSeconds > 300)
            {
                _logger.LogError("Invalid health check timeout: {Timeout}. Must be between 10 and 300 seconds", settings.HealthCheckTimeoutSeconds);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates logging configuration
        /// </summary>
        private bool ValidateLoggingConfiguration(LoggingConfiguration logging)
        {
            var validLogLevels = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };
            if (!validLogLevels.Contains(logging.LogLevel))
            {
                _logger.LogError("Invalid log level: {LogLevel}. Must be one of: {ValidLevels}", 
                    logging.LogLevel, string.Join(", ", validLogLevels));
                return false;
            }

            if (logging.RetainDays < 1 || logging.RetainDays > 365)
            {
                _logger.LogError("Invalid log retain days: {Days}. Must be between 1 and 365", logging.RetainDays);
                return false;
            }

            if (logging.MaxFileSizeMB < 1 || logging.MaxFileSizeMB > 1000)
            {
                _logger.LogError("Invalid max file size: {Size}MB. Must be between 1 and 1000", logging.MaxFileSizeMB);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates notification settings
        /// </summary>
        private bool ValidateNotificationSettings(NotificationSettings settings)
        {
            if (settings.EmailSettings != null)
            {
                if (string.IsNullOrWhiteSpace(settings.EmailSettings.SmtpServer))
                {
                    _logger.LogError("SMTP server is required for email notifications");
                    return false;
                }

                if (!ValidationUtils.IsValidEmail(settings.EmailSettings.FromEmail))
                {
                    _logger.LogError("Invalid from email address: {Email}", settings.EmailSettings.FromEmail);
                    return false;
                }

                foreach (var email in settings.EmailSettings.ToEmails)
                {
                    if (!ValidationUtils.IsValidEmail(email))
                    {
                        _logger.LogError("Invalid to email address: {Email}", email);
                        return false;
                    }
                }
            }

            if (settings.TeamsSettings != null)
            {
                if (!ValidationUtils.IsValidUrl(settings.TeamsSettings.WebhookUrl))
                {
                    _logger.LogError("Invalid Teams webhook URL: {Url}", settings.TeamsSettings.WebhookUrl);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Creates a default configuration template
        /// </summary>
        public async Task<DeploymentConfiguration> GetDefaultConfigurationAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask; // Make async for consistency

            return new DeploymentConfiguration
            {
                Applications = new List<ApplicationConfiguration>
                {
                    new ApplicationConfiguration
                    {
                        Name = "MainApplication",
                        ApplicationPoolName = "MainAppPool",
                        SiteName = "Default Web Site",
                        SourcePath = @"C:\Source\MainApp",
                        DestinationPath = @"C:\inetpub\wwwroot\MainApp",
                        WatchFolders = new List<string> { @"C:\Source\MainApp" },
                        ExcludePatterns = new List<string> { "*.log", "*.tmp", "bin/Debug/*", "obj/*", ".git/*" },
                        PreDeploymentSteps = new List<string>(),
                        PostDeploymentSteps = new List<string>(),
                        IsEnabled = true,
                        Priority = 1,
                        MaxRetries = 3,
                        TimeoutSeconds = 300
                    },
                    new ApplicationConfiguration
                    {
                        Name = "ApiApplication",
                        ApplicationPoolName = "ApiAppPool",
                        SiteName = "Default Web Site",
                        SourcePath = @"C:\Source\ApiApp",
                        DestinationPath = @"C:\inetpub\wwwroot\ApiApp",
                        WatchFolders = new List<string> { @"C:\Source\ApiApp" },
                        ExcludePatterns = new List<string> { "*.log", "*.tmp", "bin/Debug/*", "obj/*", ".git/*" },
                        PreDeploymentSteps = new List<string>(),
                        PostDeploymentSteps = new List<string>(),
                        IsEnabled = true,
                        Priority = 2,
                        MaxRetries = 3,
                        TimeoutSeconds = 300
                    }
                },
                GlobalSettings = new GlobalSettings
                {
                    EnableFileSystemWatcher = true,
                    BatchDelayMilliseconds = 5000,
                    MaxConcurrentDeployments = 3,
                    BackupEnabled = true,
                    BackupPath = @"C:\DeploymentBackups",
                    MaxBackupDays = 7,
                    RequireAdminPrivileges = true,
                    ValidateIISPermissions = true,
                    EnableHealthCheck = true,
                    HealthCheckTimeoutSeconds = 60
                },
                NotificationSettings = new NotificationSettings
                {
                    Enabled = false,
                    NotifyOnSuccess = false,
                    NotifyOnFailure = true,
                    NotifyOnStart = false,
                    EmailSettings = new EmailSettings
                    {
                        SmtpServer = "smtp.company.com",
                        Port = 587,
                        EnableSsl = true,
                        FromEmail = "deployment@company.com",
                        ToEmails = new List<string> { "admin@company.com" }
                    }
                },
                Logging = new LoggingConfiguration
                {
                    LogLevel = "Information",
                    LogToFile = true,
                    LogToConsole = true,
                    LogFilePath = @".\Logs\deployment-{Date}.log",
                    RetainDays = 30,
                    MaxFileSizeMB = 100,
                    EnableStructuredLogging = true
                }
            };
        }
    }
}
