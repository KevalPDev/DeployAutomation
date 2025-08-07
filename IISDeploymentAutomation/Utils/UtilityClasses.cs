using System.Security.Principal;
using System.Security.AccessControl;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace IISDeploymentAutomation.Utils
{
    /// <summary>
    /// Security and permission utilities
    /// </summary>
    public static class SecurityUtils
    {
        /// <summary>
        /// Checks if the current process is running with administrator privileges
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the current user has specific permissions on a directory
        /// </summary>
        public static bool HasDirectoryPermission(string directoryPath, FileSystemRights permission)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return false;

                var identity = WindowsIdentity.GetCurrent();
                var directoryInfo = new DirectoryInfo(directoryPath);
                var directorySecurity = directoryInfo.GetAccessControl();
                var accessRules = directorySecurity.GetAccessRules(true, true, typeof(SecurityIdentifier));

                foreach (FileSystemAccessRule rule in accessRules)
                {
                    if (identity.Groups?.Contains(rule.IdentityReference) == true ||
                        identity.User?.Equals(rule.IdentityReference) == true)
                    {
                        if ((rule.FileSystemRights & permission) == permission &&
                            rule.AccessControlType == AccessControlType.Allow)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the current user context information
        /// </summary>
        public static (string UserName, string DomainName, bool IsElevated) GetCurrentUserContext()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var parts = identity.Name?.Split('\\') ?? new[] { "", "" };
                return (
                    UserName: parts.Length > 1 ? parts[1] : parts[0],
                    DomainName: parts.Length > 1 ? parts[0] : Environment.MachineName,
                    IsElevated: IsRunningAsAdministrator()
                );
            }
            catch
            {
                return (Environment.UserName, Environment.MachineName, false);
            }
        }
    }

    /// <summary>
    /// File system utilities
    /// </summary>
    public static class FileSystemUtils
    {
        /// <summary>
        /// Safely copies files with retry logic and locked file handling
        /// </summary>
        public static async Task<bool> SafeCopyFileAsync(string sourcePath, string destinationPath, 
            int maxRetries = 3, int delayMs = 1000, ILogger? logger = null)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Ensure destination directory exists
                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Use FileStream with sharing options to handle locked files
                    using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    
                    await sourceStream.CopyToAsync(destStream);
                    return true;
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    logger?.LogWarning("File copy attempt {Attempt} failed for {Source} -> {Destination}: {Error}", 
                        attempt, sourcePath, destinationPath, ex.Message);
                    
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "File copy failed for {Source} -> {Destination}", sourcePath, destinationPath);
                    return false;
                }
            }
            
            logger?.LogError("File copy failed after {MaxRetries} attempts for {Source} -> {Destination}", 
                maxRetries, sourcePath, destinationPath);
            return false;
        }

        /// <summary>
        /// Safely deletes a file with retry logic
        /// </summary>
        public static async Task<bool> SafeDeleteFileAsync(string filePath, int maxRetries = 3, int delayMs = 1000, ILogger? logger = null)
        {
            if (!File.Exists(filePath))
                return true;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                catch (IOException ex) when (attempt < maxRetries)
                {
                    logger?.LogWarning("File delete attempt {Attempt} failed for {FilePath}: {Error}", 
                        attempt, filePath, ex.Message);
                    
                    await Task.Delay(delayMs);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "File delete failed for {FilePath}", filePath);
                    return false;
                }
            }
            
            logger?.LogError("File delete failed after {MaxRetries} attempts for {FilePath}", maxRetries, filePath);
            return false;
        }

        /// <summary>
        /// Gets file hash for change detection
        /// </summary>
        public static async Task<string> GetFileHashAsync(string filePath)
        {
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                
                var hashBytes = await Task.Run(() => sha256.ComputeHash(fileStream));
                return Convert.ToBase64String(hashBytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Checks if a file matches exclude patterns
        /// </summary>
        public static bool IsFileExcluded(string filePath, List<string> excludePatterns)
        {
            if (excludePatterns?.Any() != true)
                return false;

            var fileName = Path.GetFileName(filePath);
            var relativePath = filePath;

            return excludePatterns.Any(pattern => 
                MatchesPattern(fileName, pattern) || 
                MatchesPattern(relativePath, pattern));
        }

        /// <summary>
        /// Simple pattern matching with wildcards
        /// </summary>
        private static bool MatchesPattern(string input, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            // Convert simple wildcards to regex
            var regexPattern = pattern
                .Replace(".", "\\.")
                .Replace("*", ".*")
                .Replace("?", ".");

            try
            {
                return System.Text.RegularExpressions.Regex.IsMatch(input, regexPattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Creates a compressed backup of a directory
        /// </summary>
        public static async Task<string> CreateBackupAsync(string sourcePath, string backupRootPath, string applicationName)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFileName = $"{applicationName}_{timestamp}.zip";
            var backupPath = Path.Combine(backupRootPath, backupFileName);

            if (!Directory.Exists(backupRootPath))
            {
                Directory.CreateDirectory(backupRootPath);
            }

            await Task.Run(() => 
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(sourcePath, backupPath);
            });

            return backupPath;
        }
    }

    /// <summary>
    /// Process utilities for running external commands
    /// </summary>
    public static class ProcessUtils
    {
        /// <summary>
        /// Runs a command with timeout and captures output
        /// </summary>
        public static async Task<(bool Success, string Output, string Error)> RunCommandAsync(
            string fileName, string arguments, TimeSpan timeout, ILogger? logger = null)
        {
            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (sender, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (sender, e) => { if (e.Data != null) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds));

                if (!completed)
                {
                    try
                    {
                        process.Kill();
                        logger?.LogWarning("Process {FileName} {Arguments} killed due to timeout", fileName, arguments);
                    }
                    catch { }
                    
                    return (false, string.Empty, "Process timed out");
                }

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                return (process.ExitCode == 0, output, error);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to run command {FileName} {Arguments}", fileName, arguments);
                return (false, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Checks if a process is running
        /// </summary>
        public static bool IsProcessRunning(string processName)
        {
            try
            {
                return Process.GetProcessesByName(processName).Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Validation utilities
    /// </summary>
    public static class ValidationUtils
    {
        /// <summary>
        /// Validates that a path exists and is accessible
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidatePath(string path, bool mustExist = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                return (false, "Path cannot be empty");

            try
            {
                var fullPath = Path.GetFullPath(path);
                
                if (mustExist)
                {
                    if (File.Exists(fullPath))
                        return (true, null);
                    
                    if (Directory.Exists(fullPath))
                        return (true, null);
                    
                    return (false, $"Path does not exist: {fullPath}");
                }
                
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Invalid path: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates an application pool name
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateApplicationPoolName(string poolName)
        {
            if (string.IsNullOrWhiteSpace(poolName))
                return (false, "Application pool name cannot be empty");

            if (poolName.Length > 64)
                return (false, "Application pool name cannot exceed 64 characters");

            // Check for invalid characters
            var invalidChars = new[] { '<', '>', ':', '"', '|', '?', '*', '/', '\\' };
            if (poolName.Any(c => invalidChars.Contains(c)))
                return (false, "Application pool name contains invalid characters");

            return (true, null);
        }

        /// <summary>
        /// Validates a site name
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateSiteName(string siteName)
        {
            if (string.IsNullOrWhiteSpace(siteName))
                return (false, "Site name cannot be empty");

            if (siteName.Length > 64)
                return (false, "Site name cannot exceed 64 characters");

            return (true, null);
        }

        /// <summary>
        /// Validates email address format
        /// </summary>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates URL format
        /// </summary>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
                   (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
