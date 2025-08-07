using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// Main hosted service that orchestrates deployment operations
    /// </summary>
    public class DeploymentOrchestrationHostedService : BackgroundService, IDeploymentOrchestrationService
    {
        private readonly ILogger<DeploymentOrchestrationHostedService> _logger;
        private readonly IConfigurationService _configurationService;
        private readonly IFileSystemMonitorService _fileSystemMonitorService;
        private readonly IIISManagerService _iisManagerService;
        private readonly IPermissionValidationService _permissionValidationService;
        private readonly IAuditService _auditService;
        
        private DeploymentConfiguration? _configuration;
        private readonly Dictionary<string, DateTime> _lastDeployments = new();
        private readonly Dictionary<string, DeploymentOperation> _activeOperations = new();
        private readonly SemaphoreSlim _deploymentSemaphore;
        private bool _isRunning;

        public bool IsRunning => _isRunning;

        public DeploymentOrchestrationHostedService(
            ILogger<DeploymentOrchestrationHostedService> logger,
            IConfigurationService configurationService,
            IFileSystemMonitorService fileSystemMonitorService,
            IIISManagerService iisManagerService,
            IPermissionValidationService permissionValidationService,
            IAuditService auditService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            _fileSystemMonitorService = fileSystemMonitorService ?? throw new ArgumentNullException(nameof(fileSystemMonitorService));
            _iisManagerService = iisManagerService ?? throw new ArgumentNullException(nameof(iisManagerService));
            _permissionValidationService = permissionValidationService ?? throw new ArgumentNullException(nameof(permissionValidationService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            
            _deploymentSemaphore = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Main execution method for the hosted service
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Starting Deployment Orchestration Service");
                
                await StartOrchestrationAsync(stoppingToken);
                
                // Keep the service running until cancellation is requested
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        
                        // Perform periodic health checks
                        await PerformHealthCheck(stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when service is stopping
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in orchestration service execution loop");
                        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in Deployment Orchestration Service");
                throw;
            }
            finally
            {
                await StopOrchestrationAsync(stoppingToken);
            }
        }

        /// <summary>
        /// Starts the deployment orchestration service
        /// </summary>
        public async Task StartOrchestrationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Initializing deployment orchestration service");

                // Load configuration
                _configuration = await _configurationService.LoadConfigurationAsync(cancellationToken: cancellationToken);
                
                // Subscribe to file system events
                _fileSystemMonitorService.BatchFileChanged += OnBatchFileChanged;
                
                // Start file system monitoring
                var watchPaths = _configuration.Applications
                    .Where(app => app.IsEnabled)
                    .SelectMany(app => app.WatchFolders)
                    .Distinct()
                    .ToList();
                
                var excludePatterns = _configuration.Applications
                    .SelectMany(app => app.ExcludePatterns)
                    .Distinct()
                    .ToList();

                if (watchPaths.Any())
                {
                    await _fileSystemMonitorService.StartMonitoringAsync(watchPaths, excludePatterns, cancellationToken);
                    _logger.LogInformation("File system monitoring started for {PathCount} paths", watchPaths.Count);
                }

                _isRunning = true;
                
                await _auditService.LogCustomEventAsync(
                    "Orchestration", 
                    "Deployment orchestration service started",
                    new Dictionary<string, object>
                    {
                        { "ConfiguredApplications", _configuration.Applications.Count },
                        { "EnabledApplications", _configuration.Applications.Count(a => a.IsEnabled) },
                        { "WatchedPaths", watchPaths.Count }
                    },
                    cancellationToken);

                _logger.LogInformation("Deployment orchestration service started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start deployment orchestration service");
                throw;
            }
        }

        /// <summary>
        /// Stops the deployment orchestration service
        /// </summary>
        public async Task StopOrchestrationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Stopping deployment orchestration service");
                
                _isRunning = false;
                
                // Unsubscribe from events
                _fileSystemMonitorService.BatchFileChanged -= OnBatchFileChanged;
                
                // Stop file system monitoring
                await _fileSystemMonitorService.StopMonitoringAsync(cancellationToken);
                
                // Wait for active deployments to complete or timeout
                var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                
                while (_activeOperations.Any() && !combinedCts.Token.IsCancellationRequested)
                {
                    _logger.LogInformation("Waiting for {ActiveCount} active deployments to complete", _activeOperations.Count);
                    await Task.Delay(1000, combinedCts.Token);
                }
                
                await _auditService.LogCustomEventAsync(
                    "Orchestration", 
                    "Deployment orchestration service stopped",
                    new Dictionary<string, object>
                    {
                        { "ActiveOperationsRemaining", _activeOperations.Count }
                    },
                    cancellationToken);

                _logger.LogInformation("Deployment orchestration service stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping deployment orchestration service");
            }
        }

        /// <summary>
        /// Handles batch file change events
        /// </summary>
        private void OnBatchFileChanged(object? sender, List<FileChangeInfo> changes)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessFileChanges(changes);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file changes");
                }
            });
        }

        /// <summary>
        /// Processes file changes and triggers deployments
        /// </summary>
        private async Task ProcessFileChanges(List<FileChangeInfo> changes)
        {
            if (_configuration == null)
            {
                _logger.LogWarning("Configuration not loaded, cannot process file changes");
                return;
            }

            try
            {
                _logger.LogInformation("Processing {ChangeCount} file changes", changes.Count);

                // Group changes by affected applications
                var appChanges = new Dictionary<ApplicationConfiguration, List<FileChangeInfo>>();

                foreach (var change in changes)
                {
                    var affectedApps = _configuration.Applications
                        .Where(app => app.IsEnabled && IsFileInApplication(change.FilePath, app))
                        .ToList();

                    foreach (var app in affectedApps)
                    {
                        if (!appChanges.ContainsKey(app))
                        {
                            appChanges[app] = new List<FileChangeInfo>();
                        }
                        appChanges[app].Add(change);
                    }
                }

                // Process deployments for each affected application
                var deploymentTasks = appChanges.Select(kvp => 
                    ProcessApplicationDeployment(kvp.Key, kvp.Value)).ToList();

                await Task.WhenAll(deploymentTasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file changes");
            }
        }

        /// <summary>
        /// Checks if a file path belongs to an application's watch folders
        /// </summary>
        private static bool IsFileInApplication(string filePath, ApplicationConfiguration app)
        {
            return app.WatchFolders.Any(watchFolder => 
                filePath.StartsWith(watchFolder, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Processes deployment for a specific application
        /// </summary>
        private async Task ProcessApplicationDeployment(ApplicationConfiguration app, List<FileChangeInfo> changes)
        {
            try
            {
                // Check if application is already being deployed
                if (_activeOperations.ContainsKey(app.Name))
                {
                    _logger.LogInformation("Deployment already in progress for application {AppName}", app.Name);
                    return;
                }

                // Check minimum time between deployments
                if (_lastDeployments.TryGetValue(app.Name, out var lastDeployment))
                {
                    var timeSinceLastDeployment = DateTime.UtcNow - lastDeployment;
                    if (timeSinceLastDeployment < TimeSpan.FromMinutes(1))
                    {
                        _logger.LogDebug("Skipping deployment for {AppName} - too soon since last deployment", app.Name);
                        return;
                    }
                }

                await _deploymentSemaphore.WaitAsync();
                
                try
                {
                    // Create deployment operation
                    var operation = new DeploymentOperation
                    {
                        ApplicationName = app.Name,
                        ApplicationPoolName = app.ApplicationPoolName,
                        StartTime = DateTime.UtcNow,
                        Status = DeploymentStatus.InProgress,
                        FileChanges = changes,
                        UserContext = Environment.UserName,
                        MachineName = Environment.MachineName
                    };

                    _activeOperations[app.Name] = operation;

                    _logger.LogInformation("Starting deployment for application {AppName} with {ChangeCount} file changes", 
                        app.Name, changes.Count);

                    // Execute deployment
                    var success = await ExecuteDeployment(operation, app);
                    
                    operation.EndTime = DateTime.UtcNow;
                    operation.Status = success ? DeploymentStatus.Completed : DeploymentStatus.Failed;
                    
                    // Log deployment operation
                    await _auditService.LogDeploymentOperationAsync(operation);
                    
                    _lastDeployments[app.Name] = DateTime.UtcNow;
                    
                    _logger.LogInformation("Deployment for application {AppName} {Status} in {Duration}",
                        app.Name, operation.Status, operation.Duration);
                }
                finally
                {
                    _activeOperations.Remove(app.Name);
                    _deploymentSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing deployment for application {AppName}", app.Name);
            }
        }

        /// <summary>
        /// Executes the actual deployment process
        /// </summary>
        private async Task<bool> ExecuteDeployment(DeploymentOperation operation, ApplicationConfiguration app)
        {
            try
            {
                // Step 1: Stop Application Pool
                var stopStep = new DeploymentStep
                {
                    Name = "Stop Application Pool",
                    Description = $"Stopping application pool {app.ApplicationPoolName}",
                    StartTime = DateTime.UtcNow,
                    Status = StepStatus.InProgress
                };
                operation.Steps.Add(stopStep);

                var stopSuccess = await _iisManagerService.StopApplicationPoolAsync(app.ApplicationPoolName);
                
                stopStep.EndTime = DateTime.UtcNow;
                stopStep.Status = stopSuccess ? StepStatus.Completed : StepStatus.Failed;
                
                if (!stopSuccess)
                {
                    stopStep.ErrorMessage = "Failed to stop application pool";
                    operation.ErrorMessage = "Failed to stop application pool";
                    return false;
                }

                // Step 2: Copy Files (if enabled)
                bool copySuccess = true;
                if (app.EnableFileCopy)
                {
                    var copyStep = new DeploymentStep
                    {
                        Name = "Copy Files",
                        Description = $"Copying files from {app.SourcePath} to {app.DestinationPath}",
                        StartTime = DateTime.UtcNow,
                        Status = StepStatus.InProgress
                    };
                    operation.Steps.Add(copyStep);

                    copySuccess = await CopyApplicationFiles(app, operation.FileChanges);
                    
                    copyStep.EndTime = DateTime.UtcNow;
                    copyStep.Status = copySuccess ? StepStatus.Completed : StepStatus.Failed;
                    
                    if (!copySuccess)
                    {
                        copyStep.ErrorMessage = "Failed to copy application files";
                        operation.ErrorMessage = "Failed to copy application files";
                    }
                }
                else
                {
                    // File copy is disabled - just log the event
                    var copyStep = new DeploymentStep
                    {
                        Name = "Skip File Copy",
                        Description = "File copying is disabled for this application",
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Status = StepStatus.Skipped
                    };
                    operation.Steps.Add(copyStep);
                    
                    _logger.LogInformation("File copying is disabled for application {AppName}. Only application pool restart will be performed.", app.Name);
                }

                // Step 3: Start Application Pool (always attempt, even if copy failed)
                var startStep = new DeploymentStep
                {
                    Name = "Start Application Pool",
                    Description = $"Starting application pool {app.ApplicationPoolName}",
                    StartTime = DateTime.UtcNow,
                    Status = StepStatus.InProgress
                };
                operation.Steps.Add(startStep);

                var startSuccess = await _iisManagerService.StartApplicationPoolAsync(app.ApplicationPoolName);
                
                startStep.EndTime = DateTime.UtcNow;
                startStep.Status = startSuccess ? StepStatus.Completed : StepStatus.Failed;
                
                if (!startSuccess)
                {
                    startStep.ErrorMessage = "Failed to start application pool";
                    if (string.IsNullOrEmpty(operation.ErrorMessage))
                    {
                        operation.ErrorMessage = "Failed to start application pool";
                    }
                }

                return copySuccess && startSuccess;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing deployment for {AppName}", app.Name);
                operation.ErrorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Copies application files from source to destination
        /// </summary>
        private async Task<bool> CopyApplicationFiles(ApplicationConfiguration app, List<FileChangeInfo> changes)
        {
            try
            {
                _logger.LogDebug("Copying {FileCount} changed files for application {AppName}", changes.Count, app.Name);

                var copyTasks = changes.Select(async change =>
                {
                    try
                    {
                        if (change.ChangeType == FileChangeType.Deleted)
                        {
                            // Handle file deletion
                            var relativePath = Path.GetRelativePath(app.SourcePath, change.FilePath);
                            var destPath = Path.Combine(app.DestinationPath, relativePath);
                            
                            if (File.Exists(destPath))
                            {
                                await FileSystemUtils.SafeDeleteFileAsync(destPath, logger: _logger);
                            }
                        }
                        else
                        {
                            // Handle file creation/modification
                            var relativePath = Path.GetRelativePath(app.SourcePath, change.FilePath);
                            var destPath = Path.Combine(app.DestinationPath, relativePath);
                            
                            return await FileSystemUtils.SafeCopyFileAsync(change.FilePath, destPath, logger: _logger);
                        }
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error copying file {FilePath}", change.FilePath);
                        return false;
                    }
                });

                var results = await Task.WhenAll(copyTasks);
                return results.All(r => r);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying application files for {AppName}", app.Name);
                return false;
            }
        }

        /// <summary>
        /// Performs periodic health checks
        /// </summary>
        private async Task PerformHealthCheck(CancellationToken cancellationToken)
        {
            try
            {
                if (_configuration == null) return;

                // Check application pool states
                foreach (var app in _configuration.Applications.Where(a => a.IsEnabled))
                {
                    var state = await _iisManagerService.GetApplicationPoolStateAsync(app.ApplicationPoolName, cancellationToken);
                    
                    if (state != AppPoolState.Started)
                    {
                        _logger.LogWarning("Application pool {PoolName} for application {AppName} is in state {State}", 
                            app.ApplicationPoolName, app.Name, state);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error performing health check");
            }
        }

        /// <summary>
        /// Triggers manual deployment for a specific application
        /// </summary>
        public async Task TriggerManualDeploymentAsync(string applicationName, CancellationToken cancellationToken = default)
        {
            if (_configuration == null)
            {
                throw new InvalidOperationException("Service not started or configuration not loaded");
            }

            var app = _configuration.Applications.FirstOrDefault(a => 
                a.Name.Equals(applicationName, StringComparison.OrdinalIgnoreCase) && a.IsEnabled);

            if (app == null)
            {
                throw new ArgumentException($"Application '{applicationName}' not found or not enabled");
            }

            // Detect recent changes
            var changes = new List<FileChangeInfo>();
            foreach (var watchFolder in app.WatchFolders)
            {
                var folderChanges = await _fileSystemMonitorService.DetectChangesAsync(
                    watchFolder, DateTime.UtcNow.AddHours(-1), cancellationToken);
                changes.AddRange(folderChanges);
            }

            if (!changes.Any())
            {
                _logger.LogInformation("No recent changes detected for manual deployment of {AppName}", applicationName);
            }

            await ProcessApplicationDeployment(app, changes);
        }

        /// <summary>
        /// Gets current deployment operations
        /// </summary>
        public async Task<List<DeploymentOperation>> GetCurrentOperationsAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return _activeOperations.Values.ToList();
        }

        /// <summary>
        /// Gets overall deployment statistics
        /// </summary>
        public async Task<DeploymentStatistics> GetOverallStatisticsAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            
            // This would typically be implemented with persistent storage
            return new DeploymentStatistics
            {
                TotalDeployments = _lastDeployments.Count,
                LastDeployment = _lastDeployments.Values.DefaultIfEmpty(DateTime.MinValue).Max(),
                ApplicationDeploymentCounts = _lastDeployments.ToDictionary(kvp => kvp.Key, kvp => 1)
            };
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public override void Dispose()
        {
            _deploymentSemaphore?.Dispose();
            base.Dispose();
        }
    }
}
