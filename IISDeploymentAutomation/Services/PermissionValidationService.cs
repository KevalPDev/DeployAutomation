using Microsoft.Extensions.Logging;
using IISDeploymentAutomation.Interfaces;
using IISDeploymentAutomation.Models;
using IISDeploymentAutomation.Utils;
using Microsoft.Web.Administration;
using System.Security.Principal;

namespace IISDeploymentAutomation.Services
{
    /// <summary>
    /// Permission validation service with comprehensive security checks
    /// </summary>
    public class PermissionValidationService : IPermissionValidationService
    {
        private readonly ILogger<PermissionValidationService> _logger;
        private readonly IAuditService _auditService;

        public PermissionValidationService(ILogger<PermissionValidationService> logger, IAuditService auditService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        }

        /// <summary>
        /// Performs comprehensive permission validation for deployment operations
        /// </summary>
        public async Task<PermissionValidationResult> ValidatePermissionsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Starting comprehensive permission validation");

            var result = new PermissionValidationResult
            {
                Checks = new List<PermissionCheck>()
            };

            try
            {
                // Check 1: Administrator Privileges
                var adminCheck = await ValidateAdminPrivilegesAsync(cancellationToken);
                result.Checks.Add(adminCheck);

                // Check 2: IIS Access
                var iisCheck = await ValidateIISAccessAsync(cancellationToken);
                result.Checks.Add(iisCheck);

                // Check 3: File System Access
                var fileSystemCheck = await ValidateFileSystemAccessAsync(cancellationToken);
                result.Checks.Add(fileSystemCheck);

                // Check 4: Windows Service Access
                var serviceCheck = await ValidateWindowsServiceAccessAsync(cancellationToken);
                result.Checks.Add(serviceCheck);

                // Check 5: Registry Access (for IIS configuration)
                var registryCheck = await ValidateRegistryAccessAsync(cancellationToken);
                result.Checks.Add(registryCheck);

                // Check 6: Process Management Access
                var processCheck = await ValidateProcessManagementAccessAsync(cancellationToken);
                result.Checks.Add(processCheck);

                // Determine overall result
                result.IsValid = result.Checks.All(c => c.Passed);
                
                if (!result.IsValid)
                {
                    var failedChecks = result.Checks.Where(c => !c.Passed).Select(c => c.Name);
                    result.ErrorMessage = $"Permission validation failed for: {string.Join(", ", failedChecks)}";
                }

                _logger.LogInformation("Permission validation completed. Result: {IsValid}. Failed checks: {FailedCount}", 
                    result.IsValid, result.Checks.Count(c => !c.Passed));

                // Log the permission validation results
                await _auditService.LogPermissionCheckAsync(result, cancellationToken);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Permission validation failed with exception");
                result.IsValid = false;
                result.ErrorMessage = $"Permission validation failed: {ex.Message}";
                return result;
            }
        }

        /// <summary>
        /// Validates administrator privileges
        /// </summary>
        private async Task<PermissionCheck> ValidateAdminPrivilegesAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "Administrator Privileges",
                Description = "Verify the application is running with administrator privileges",
                RequiredLevel = PermissionLevel.Admin
            };

            try
            {
                var isAdmin = SecurityUtils.IsRunningAsAdministrator();
                check.Passed = isAdmin;
                check.ActualLevel = isAdmin ? PermissionLevel.Admin : PermissionLevel.None;
                
                if (!isAdmin)
                {
                    check.ErrorMessage = "Application must be run as Administrator to manage IIS application pools and sites";
                }

                _logger.LogDebug("Administrator privileges check: {Passed}", check.Passed);
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"Failed to check administrator privileges: {ex.Message}";
                _logger.LogError(ex, "Failed to validate administrator privileges");
            }

            return check;
        }

        /// <summary>
        /// Validates IIS management access
        /// </summary>
        private async Task<PermissionCheck> ValidateIISAccessAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "IIS Management Access",
                Description = "Verify access to IIS management APIs and configuration",
                RequiredLevel = PermissionLevel.Full
            };

            try
            {
                using var serverManager = new ServerManager();
                
                // Test read access
                var appPoolCount = serverManager.ApplicationPools.Count;
                var siteCount = serverManager.Sites.Count;
                
                // Test if we can access application pool properties
                if (serverManager.ApplicationPools.Count > 0)
                {
                    var firstPool = serverManager.ApplicationPools.First();
                    var state = firstPool.State;
                    var name = firstPool.Name;
                }

                check.Passed = true;
                check.ActualLevel = PermissionLevel.Full;
                
                _logger.LogDebug("IIS access check passed. Found {AppPoolCount} app pools and {SiteCount} sites", 
                    appPoolCount, siteCount);
            }
            catch (UnauthorizedAccessException ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = "Access denied to IIS configuration. Ensure administrator privileges and IIS management tools are installed";
                _logger.LogError(ex, "Access denied to IIS management");
            }
            catch (System.Runtime.InteropServices.COMException ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = "IIS management service not available or accessible";
                _logger.LogError(ex, "IIS management COM error");
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"IIS access validation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to validate IIS access");
            }

            return check;
        }

        /// <summary>
        /// Validates file system access permissions
        /// </summary>
        private async Task<PermissionCheck> ValidateFileSystemAccessAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "File System Access",
                Description = "Verify read/write access to common deployment directories",
                RequiredLevel = PermissionLevel.Write
            };

            try
            {
                var testPaths = new[]
                {
                    @"C:\inetpub\wwwroot",
                    Path.GetTempPath(),
                    AppDomain.CurrentDomain.BaseDirectory
                };

                var hasWriteAccess = true;
                var accessResults = new List<string>();

                foreach (var path in testPaths)
                {
                    try
                    {
                        if (Directory.Exists(path))
                        {
                            var hasRead = SecurityUtils.HasDirectoryPermission(path, System.Security.AccessControl.FileSystemRights.Read);
                            var hasWrite = SecurityUtils.HasDirectoryPermission(path, System.Security.AccessControl.FileSystemRights.Write);
                            
                            accessResults.Add($"{path}: Read={hasRead}, Write={hasWrite}");
                            
                            if (!hasWrite)
                            {
                                hasWriteAccess = false;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        accessResults.Add($"{path}: Error - {ex.Message}");
                        hasWriteAccess = false;
                    }
                }

                check.Passed = hasWriteAccess;
                check.ActualLevel = hasWriteAccess ? PermissionLevel.Write : PermissionLevel.Read;
                
                if (!hasWriteAccess)
                {
                    check.ErrorMessage = $"Insufficient file system permissions. Results: {string.Join("; ", accessResults)}";
                }

                _logger.LogDebug("File system access check: {Passed}. Details: {AccessResults}", 
                    check.Passed, string.Join("; ", accessResults));
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"File system access validation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to validate file system access");
            }

            return check;
        }

        /// <summary>
        /// Validates Windows service access permissions
        /// </summary>
        private async Task<PermissionCheck> ValidateWindowsServiceAccessAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "Windows Service Access",
                Description = "Verify access to Windows service management",
                RequiredLevel = PermissionLevel.Admin
            };

            try
            {
                // Check if we can query service status
                var serviceNames = new[] { "W3SVC", "WAS" }; // IIS services
                var accessibleServices = 0;

                foreach (var serviceName in serviceNames)
                {
                    try
                    {
                        using var service = new System.ServiceProcess.ServiceController(serviceName);
                        var status = service.Status;
                        accessibleServices++;
                        _logger.LogDebug("Service {ServiceName} status: {Status}", serviceName, status);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Cannot access service {ServiceName}: {Error}", serviceName, ex.Message);
                    }
                }

                check.Passed = accessibleServices > 0;
                check.ActualLevel = check.Passed ? PermissionLevel.Admin : PermissionLevel.None;
                
                if (!check.Passed)
                {
                    check.ErrorMessage = "Cannot access Windows services. Administrator privileges required";
                }
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"Windows service access validation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to validate Windows service access");
            }

            return check;
        }

        /// <summary>
        /// Validates registry access permissions
        /// </summary>
        private async Task<PermissionCheck> ValidateRegistryAccessAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "Registry Access",
                Description = "Verify access to Windows registry for IIS configuration",
                RequiredLevel = PermissionLevel.Read
            };

            try
            {
                // Try to access IIS registry keys
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
                
                if (key != null)
                {
                    var version = key.GetValue("VersionString");
                    check.Passed = true;
                    check.ActualLevel = PermissionLevel.Read;
                    _logger.LogDebug("Registry access check passed. IIS version: {Version}", version);
                }
                else
                {
                    check.Passed = false;
                    check.ActualLevel = PermissionLevel.None;
                    check.ErrorMessage = "Cannot access IIS registry configuration";
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = "Access denied to registry. Administrator privileges required";
                _logger.LogError(ex, "Registry access denied");
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"Registry access validation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to validate registry access");
            }

            return check;
        }

        /// <summary>
        /// Validates process management access
        /// </summary>
        private async Task<PermissionCheck> ValidateProcessManagementAccessAsync(CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            
            var check = new PermissionCheck
            {
                Name = "Process Management Access",
                Description = "Verify ability to manage processes and worker processes",
                RequiredLevel = PermissionLevel.Admin
            };

            try
            {
                // Check if we can enumerate processes
                var processes = System.Diagnostics.Process.GetProcesses();
                var iisProcesses = processes.Where(p => 
                    p.ProcessName.Equals("w3wp", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Equals("iisexpress", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                check.Passed = true;
                check.ActualLevel = PermissionLevel.Admin;
                
                _logger.LogDebug("Process management check passed. Found {IISProcessCount} IIS-related processes", 
                    iisProcesses.Count);
            }
            catch (Exception ex)
            {
                check.Passed = false;
                check.ActualLevel = PermissionLevel.None;
                check.ErrorMessage = $"Process management access validation failed: {ex.Message}";
                _logger.LogError(ex, "Failed to validate process management access");
            }

            return check;
        }

        /// <summary>
        /// Checks if the current process has administrator privileges
        /// </summary>
        public async Task<bool> HasAdminPrivilegesAsync(CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            return SecurityUtils.IsRunningAsAdministrator();
        }

        /// <summary>
        /// Checks if the current process has IIS access
        /// </summary>
        public async Task<bool> HasIISAccessAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var serverManager = new ServerManager();
                var count = serverManager.ApplicationPools.Count;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks file system access for a specific path
        /// </summary>
        public async Task<bool> HasFileSystemAccessAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path))
                    return false;

                return SecurityUtils.HasDirectoryPermission(path, System.Security.AccessControl.FileSystemRights.Write);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the current process can control a specific application pool
        /// </summary>
        public async Task<bool> CanControlApplicationPoolAsync(string poolName, CancellationToken cancellationToken = default)
        {
            try
            {
                using var serverManager = new ServerManager();
                var appPool = serverManager.ApplicationPools[poolName];
                
                if (appPool == null)
                    return false;

                // Try to read the current state
                var state = appPool.State;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
