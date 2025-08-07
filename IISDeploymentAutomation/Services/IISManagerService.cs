using Microsoft.Web.Administration;
using Microsoft.Extensions.Logging;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;
using System.Collections.Concurrent;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// IIS management service with comprehensive error handling and performance monitoring
    /// </summary>
    public class IISManagerService : IIISManagerService
    {
        private readonly ILogger<IISManagerService> _logger;
        private readonly IAuditService _auditService;
        private readonly SemaphoreSlim _iisOperationSemaphore;
        private readonly ConcurrentDictionary<string, DateTime> _lastOperationTimes;
        
        public IISManagerService(ILogger<IISManagerService> logger, IAuditService auditService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _iisOperationSemaphore = new SemaphoreSlim(1, 1); // Serialize IIS operations
            _lastOperationTimes = new ConcurrentDictionary<string, DateTime>();
        }

        /// <summary>
        /// Stops an application pool with comprehensive error handling and monitoring
        /// </summary>
        public async Task<bool> StopApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default)
        {
            var operation = new AppPoolOperation
            {
                PoolName = poolName,
                Action = AppPoolAction.Stop,
                Timestamp = DateTime.UtcNow
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting application pool stop operation for {PoolName}", poolName);

                if (!await ApplicationPoolExistsAsync(poolName, cancellationToken))
                {
                    _logger.LogWarning("Application pool {PoolName} does not exist", poolName);
                    operation.Success = false;
                    operation.ErrorMessage = "Application pool does not exist";
                    return false;
                }

                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    var appPool = serverManager.ApplicationPools[poolName];
                    
                    if (appPool == null)
                    {
                        _logger.LogWarning("Application pool {PoolName} not found in ServerManager", poolName);
                        operation.Success = false;
                        operation.ErrorMessage = "Application pool not found";
                        return false;
                    }

                    operation.PreviousState = ConvertToAppPoolState(appPool.State);
                    
                    // Check if already stopped
                    if (appPool.State == ObjectState.Stopped)
                    {
                        _logger.LogInformation("Application pool {PoolName} is already stopped", poolName);
                        operation.Success = true;
                        operation.NewState = AppPoolState.Stopped;
                        return true;
                    }

                    // Stop the application pool
                    appPool.Stop();
                    serverManager.CommitChanges();

                    // Wait for the pool to actually stop with timeout
                    var timeout = TimeSpan.FromSeconds(30);
                    var startTime = DateTime.UtcNow;
                    
                    while (appPool.State != ObjectState.Stopped && DateTime.UtcNow - startTime < timeout)
                    {
                        await Task.Delay(500, cancellationToken);
                        
                        // Refresh the state
                        using var refreshManager = new ServerManager();
                        appPool = refreshManager.ApplicationPools[poolName];
                    }

                    operation.NewState = ConvertToAppPoolState(appPool.State);
                    operation.Success = appPool.State == ObjectState.Stopped;

                    if (operation.Success)
                    {
                        _logger.LogInformation("Application pool {PoolName} stopped successfully in {Duration}ms", 
                            poolName, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.LogWarning("Application pool {PoolName} did not stop within timeout. Current state: {State}", 
                            poolName, appPool.State);
                        operation.ErrorMessage = $"Pool did not stop within timeout. Current state: {appPool.State}";
                    }

                    return operation.Success;
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to stop application pool {PoolName}", poolName);
                operation.Success = false;
                operation.ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                stopwatch.Stop();
                operation.Duration = stopwatch.Elapsed;
                _lastOperationTimes[poolName] = DateTime.UtcNow;
                
                // Log the operation for audit
                await _auditService.LogAppPoolOperationAsync(operation, cancellationToken);
            }
        }

        /// <summary>
        /// Starts an application pool with comprehensive validation and monitoring
        /// </summary>
        public async Task<bool> StartApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default)
        {
            var operation = new AppPoolOperation
            {
                PoolName = poolName,
                Action = AppPoolAction.Start,
                Timestamp = DateTime.UtcNow
            };

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                _logger.LogInformation("Starting application pool start operation for {PoolName}", poolName);

                if (!await ApplicationPoolExistsAsync(poolName, cancellationToken))
                {
                    _logger.LogWarning("Application pool {PoolName} does not exist", poolName);
                    operation.Success = false;
                    operation.ErrorMessage = "Application pool does not exist";
                    return false;
                }

                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    var appPool = serverManager.ApplicationPools[poolName];
                    
                    if (appPool == null)
                    {
                        _logger.LogWarning("Application pool {PoolName} not found in ServerManager", poolName);
                        operation.Success = false;
                        operation.ErrorMessage = "Application pool not found";
                        return false;
                    }

                    operation.PreviousState = ConvertToAppPoolState(appPool.State);
                    
                    // Check if already started
                    if (appPool.State == ObjectState.Started)
                    {
                        _logger.LogInformation("Application pool {PoolName} is already started", poolName);
                        operation.Success = true;
                        operation.NewState = AppPoolState.Started;
                        return true;
                    }

                    // Start the application pool
                    appPool.Start();
                    serverManager.CommitChanges();

                    // Wait for the pool to actually start with timeout
                    var timeout = TimeSpan.FromSeconds(60); // Starting can take longer than stopping
                    var startTime = DateTime.UtcNow;
                    
                    while (appPool.State != ObjectState.Started && DateTime.UtcNow - startTime < timeout)
                    {
                        await Task.Delay(1000, cancellationToken);
                        
                        // Refresh the state
                        using var refreshManager = new ServerManager();
                        appPool = refreshManager.ApplicationPools[poolName];
                        
                        // Check for error states
                        if (appPool.State == ObjectState.Stopped)
                        {
                            _logger.LogWarning("Application pool {PoolName} stopped during start operation", poolName);
                            break;
                        }
                    }

                    operation.NewState = ConvertToAppPoolState(appPool.State);
                    operation.Success = appPool.State == ObjectState.Started;

                    if (operation.Success)
                    {
                        _logger.LogInformation("Application pool {PoolName} started successfully in {Duration}ms", 
                            poolName, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger.LogWarning("Application pool {PoolName} did not start within timeout. Current state: {State}", 
                            poolName, appPool.State);
                        operation.ErrorMessage = $"Pool did not start within timeout. Current state: {appPool.State}";
                    }

                    return operation.Success;
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start application pool {PoolName}", poolName);
                operation.Success = false;
                operation.ErrorMessage = ex.Message;
                return false;
            }
            finally
            {
                stopwatch.Stop();
                operation.Duration = stopwatch.Elapsed;
                _lastOperationTimes[poolName] = DateTime.UtcNow;
                
                // Log the operation for audit
                await _auditService.LogAppPoolOperationAsync(operation, cancellationToken);
            }
        }

        /// <summary>
        /// Restarts an application pool (stop then start)
        /// </summary>
        public async Task<bool> RestartApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting application pool restart operation for {PoolName}", poolName);

            // Stop first
            var stopResult = await StopApplicationPoolAsync(poolName, cancellationToken);
            if (!stopResult)
            {
                _logger.LogError("Failed to stop application pool {PoolName} during restart", poolName);
                return false;
            }

            // Wait a bit before starting
            await Task.Delay(2000, cancellationToken);

            // Start
            var startResult = await StartApplicationPoolAsync(poolName, cancellationToken);
            if (!startResult)
            {
                _logger.LogError("Failed to start application pool {PoolName} during restart", poolName);
                return false;
            }

            _logger.LogInformation("Application pool {PoolName} restarted successfully", poolName);
            return true;
        }

        /// <summary>
        /// Gets the current state of an application pool
        /// </summary>
        public async Task<AppPoolState> GetApplicationPoolStateAsync(string poolName, CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    var appPool = serverManager.ApplicationPools[poolName];
                    
                    if (appPool == null)
                        return AppPoolState.Unknown;

                    return ConvertToAppPoolState(appPool.State);
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get application pool state for {PoolName}", poolName);
                return AppPoolState.Unknown;
            }
        }

        /// <summary>
        /// Checks if an application pool exists
        /// </summary>
        public async Task<bool> ApplicationPoolExistsAsync(string poolName, CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    return serverManager.ApplicationPools[poolName] != null;
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if application pool exists: {PoolName}", poolName);
                return false;
            }
        }

        /// <summary>
        /// Gets all application pools
        /// </summary>
        public async Task<List<string>> GetApplicationPoolsAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    return serverManager.ApplicationPools.Select(pool => pool.Name).ToList();
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get application pools");
                return new List<string>();
            }
        }

        /// <summary>
        /// Validates that IIS is accessible and manageable
        /// </summary>
        public async Task<bool> ValidateIISAccessAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    
                    // Try to access application pools collection
                    var poolCount = serverManager.ApplicationPools.Count;
                    
                    // Try to access sites collection  
                    var siteCount = serverManager.Sites.Count;
                    
                    _logger.LogInformation("IIS access validated. Found {PoolCount} application pools and {SiteCount} sites", 
                        poolCount, siteCount);
                    
                    return true;
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "Access denied to IIS. Ensure the application is running with administrator privileges");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate IIS access");
                return false;
            }
        }

        /// <summary>
        /// Gets all sites
        /// </summary>
        public async Task<List<string>> GetSitesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    return serverManager.Sites.Select(site => site.Name).ToList();
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get sites");
                return new List<string>();
            }
        }

        /// <summary>
        /// Checks if a site exists
        /// </summary>
        public async Task<bool> SiteExistsAsync(string siteName, CancellationToken cancellationToken = default)
        {
            try
            {
                await _iisOperationSemaphore.WaitAsync(cancellationToken);
                
                try
                {
                    using var serverManager = new ServerManager();
                    return serverManager.Sites[siteName] != null;
                }
                finally
                {
                    _iisOperationSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if site exists: {SiteName}", siteName);
                return false;
            }
        }

        /// <summary>
        /// Converts IIS ObjectState to our AppPoolState enum
        /// </summary>
        private static AppPoolState ConvertToAppPoolState(ObjectState state)
        {
            return state switch
            {
                ObjectState.Starting => AppPoolState.Starting,
                ObjectState.Started => AppPoolState.Started,
                ObjectState.Stopping => AppPoolState.Stopping,
                ObjectState.Stopped => AppPoolState.Stopped,
                _ => AppPoolState.Unknown
            };
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            _iisOperationSemaphore?.Dispose();
        }
    }
}
