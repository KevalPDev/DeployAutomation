# IIS Deployment Automation System

Enterprise-grade .NET Core application for automating IIS deployments with application pool management, file system monitoring, and comprehensive audit logging.

## 🚀 Features

### Core Functionality
- **Automated File System Monitoring**: Real-time detection of file changes with intelligent batching
- **IIS Application Pool Management**: Start, stop, and restart application pools with comprehensive error handling
- **Enterprise Logging**: Structured logging with Serilog and comprehensive audit trails
- **Permission Validation**: Thorough security and access validation before operations
- **Batch Processing**: Intelligent grouping of file changes to minimize application downtime
- **Configuration Management**: JSON-based configuration with validation and defaults

### Advanced Features
- **Concurrent Deployment Limiting**: Prevent system overload with configurable limits
- **Retry Logic**: Robust error handling with configurable retry mechanisms
- **Health Monitoring**: Application pool state monitoring and health checks
- **Backup Management**: Optional backup creation before deployments
- **Notification Support**: Email, Teams, and SMS notifications (configurable)
- **Command Line Interface**: Full CLI support for automation and scripting

## 📋 Prerequisites

### System Requirements
- **Windows Server 2016+** or **Windows 10/11**
- **.NET 8.0 Runtime**
- **IIS with Management Tools** installed
- **Administrator Privileges** (required for IIS management)

### Required IIS Features
```powershell
# Enable required Windows features
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServer
Enable-WindowsOptionalFeature -Online -FeatureName IIS-CommonHttpFeatures
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpErrors
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpLogging
Enable-WindowsOptionalFeature -Online -FeatureName IIS-RequestFiltering
Enable-WindowsOptionalFeature -Online -FeatureName IIS-StaticContent
Enable-WindowsOptionalFeature -Online -FeatureName IIS-ManagementConsole
Enable-WindowsOptionalFeature -Online -FeatureName IIS-IIS6ManagementCompatibility
```

## 🛠️ Installation

### 1. Download and Build
```bash
git clone <repository-url>
cd IISDeploymentAutomation
dotnet build --configuration Release
```

### 2. Configure Application
Edit `config.json` to match your environment:

```json
{
  "applications": [
    {
      "name": "YourApplication",
      "applicationPoolName": "YourAppPool",
      "siteName": "Default Web Site",
      "sourcePath": "C:\\Source\\YourApp",
      "destinationPath": "C:\\inetpub\\wwwroot\\YourApp",
      "watchFolders": ["C:\\Source\\YourApp"],
      "excludePatterns": ["*.log", "*.tmp", "bin/Debug/*", "obj/*", ".git/*"],
      "isEnabled": true,
      "priority": 1,
      "maxRetries": 3,
      "timeoutSeconds": 300
    }
  ]
}
```

### 3. Permission Setup
Ensure the application has the required permissions:

#### Required Permissions Checklist
- [ ] **Administrator Privileges**: Run as Administrator
- [ ] **IIS Management Access**: Manage application pools and sites
- [ ] **File System Access**: Read/write to source and destination paths
- [ ] **Windows Service Access**: Query and control Windows services
- [ ] **Registry Access**: Read IIS configuration from registry
- [ ] **Process Management**: Enumerate and manage processes

#### Setting Up Permissions
1. **Run as Administrator**: Always launch with administrator privileges
2. **Service Account**: If running as a service, use an account with IIS administration rights
3. **File Permissions**: Ensure read access to source paths and write access to destination paths
4. **IIS Access**: User must be in "IIS_IUSRS" group for IIS management

## 🚀 Usage

### Interactive Mode
```bash
# Start in interactive mode
IISDeploymentAutomation.exe
```

Interactive commands:
- `h` - Show help
- `s` - Show system status  
- `q` - Quit application
- `Ctrl+C` - Stop service

### Command Line Operations
```bash
# Validate system requirements
IISDeploymentAutomation.exe --validate

# List application pools
IISDeploymentAutomation.exe --list-pools

# List IIS sites
IISDeploymentAutomation.exe --list-sites

# Trigger manual deployment
IISDeploymentAutomation.exe --deploy MainApplication

# Show help
IISDeploymentAutomation.exe --help
```

### Windows Service Mode
```bash
# Install as Windows Service (requires additional setup)
sc create "IISDeploymentAutomation" binpath="C:\Path\To\IISDeploymentAutomation.exe"
sc start "IISDeploymentAutomation"
```

## ⚙️ Configuration

### Application Configuration (`config.json`)

#### Applications Section
```json
{
  "name": "ApplicationName",              // Unique name for the application
  "applicationPoolName": "AppPoolName",   // IIS Application Pool name
  "siteName": "Default Web Site",         // IIS Site name
  "sourcePath": "C:\\Source\\App",        // Source code directory
  "destinationPath": "C:\\inetpub\\wwwroot\\App", // Deployment target
  "watchFolders": ["C:\\Source\\App"],    // Directories to monitor
  "excludePatterns": [                    // Files/patterns to ignore
    "*.log", "*.tmp", "bin/Debug/*", "obj/*", ".git/*"
  ],
  "preDeploymentSteps": [],               // Custom commands before deployment
  "postDeploymentSteps": [],              // Custom commands after deployment
  "isEnabled": true,                      // Enable/disable monitoring
  "enableFileCopy": true,                 // Enable/disable file copying (true=copy files, false=only restart app pool)
  "priority": 1,                          // Deployment priority (1-10)
  "maxRetries": 3,                        // Retry attempts on failure
  "timeoutSeconds": 300                   // Operation timeout
}
```

#### Global Settings
```json
{
  "enableFileSystemWatcher": true,        // Enable real-time monitoring
  "batchDelayMilliseconds": 5000,         // Delay before processing batches
  "maxConcurrentDeployments": 3,          // Maximum simultaneous deployments
  "backupEnabled": true,                  // Create backups before deployment
  "backupPath": "C:\\DeploymentBackups", // Backup storage location
  "maxBackupDays": 7,                     // Backup retention period
  "requireAdminPrivileges": true,         // Enforce admin privileges
  "validateIISPermissions": true,         // Validate IIS access
  "enableHealthCheck": true,              // Monitor application health
  "healthCheckTimeoutSeconds": 60         // Health check timeout
}
```

#### Notification Settings
```json
{
  "enabled": false,                       // Enable notifications
  "notifyOnSuccess": false,               // Notify on successful deployments
  "notifyOnFailure": true,                // Notify on failed deployments
  "notifyOnStart": false,                 // Notify when deployments start
  "emailSettings": {
    "smtpServer": "smtp.company.com",
    "port": 587,
    "username": "user@company.com",
    "password": "password",
    "fromEmail": "deployment@company.com",
    "toEmails": ["admin@company.com"],
    "enableSsl": true
  }
}
```

## 🔧 Application Deployment Modes

The system supports two deployment modes for each application:

### 1. Full Deployment Mode (`enableFileCopy: true`)
- **File Monitoring**: Watches configured directories for changes
- **File Copying**: Copies changed files from source to destination
- **Application Pool Management**: Stops pool before copying, starts after copying
- **Use Case**: Complete application deployment with file synchronization

### 2. Pool-Only Mode (`enableFileCopy: false`) 
- **File Monitoring**: Still watches configured directories for changes
- **File Copying**: **DISABLED** - No files are copied
- **Application Pool Management**: Only restarts the application pool
- **Use Case**: Applications that handle their own file deployment (CI/CD pipelines, shared storage, etc.)

### Configuration Example
```json
{
  "applications": [
    {
      "name": "MainApp",
      "enableFileCopy": true,   // Full deployment with file copying
      "applicationPoolName": "MainAppPool",
      // ... other settings
    },
    {
      "name": "ApiApp", 
      "enableFileCopy": false,  // Pool restart only, no file copying
      "applicationPoolName": "ApiAppPool",
      // ... other settings
    }
  ]
}
```

### Pool-Only Mode Benefits
- **External Deployment Tools**: Works with CI/CD systems that deploy files separately
- **Shared Storage**: Applications using shared network storage or content delivery networks
- **Performance**: Faster deployments when file copying isn't needed
- **Flexibility**: Allows custom deployment workflows while maintaining pool management

### Logging Configuration
The application uses Serilog for structured logging with multiple sinks:

```json
{
  "logLevel": "Information",              // Minimum log level
  "logToFile": true,                      // Enable file logging
  "logToConsole": true,                   // Enable console logging
  "logFilePath": ".\\Logs\\deployment-{Date}.log", // Log file pattern
  "retainDays": 30,                       // Log retention period
  "maxFileSizeMB": 100,                   // Maximum log file size
  "enableStructuredLogging": true         // Enable structured logging
}
```

## 🔧 Architecture

### Core Components

#### Services
- **IISManagerService**: Manages IIS application pools and sites
- **FileSystemMonitorService**: Monitors file changes with intelligent batching
- **ConfigurationService**: Handles configuration loading and validation
- **PermissionValidationService**: Validates system permissions and access
- **AuditService**: Provides comprehensive audit logging
- **DeploymentOrchestrationService**: Orchestrates deployment workflows

#### Models
- **DeploymentConfiguration**: Main configuration model
- **DeploymentOperation**: Represents a deployment operation
- **FileChangeInfo**: File change information
- **PermissionValidationResult**: Permission check results
- **AppPoolOperation**: Application pool operation details

#### Utilities
- **SecurityUtils**: Security and permission utilities
- **FileSystemUtils**: File system operations with retry logic
- **ProcessUtils**: Process management utilities
- **ValidationUtils**: Configuration validation utilities

## 📊 Monitoring and Logging

### Log Locations
- **Application Logs**: `.\Logs\deployment-{Date}.log`
- **Audit Logs**: `.\Logs\Audit\audit-{Date}.json`
- **Console Output**: Real-time status and events

### Log Levels
- **Trace**: Detailed diagnostic information
- **Debug**: Development and troubleshooting information
- **Information**: General operational information
- **Warning**: Potential issues that don't prevent operation
- **Error**: Error conditions that need attention
- **Critical**: Critical errors that may terminate the application

### Audit Information
The system maintains comprehensive audit logs including:
- Deployment operations with timing and status
- Application pool state changes
- File system changes and batching
- Permission validation results
- System health checks
- User actions and triggers

## 🛡️ Security Considerations

### Permissions
- **Run as Administrator**: Required for IIS management
- **File System Access**: Minimize permissions to only required directories
- **Network Access**: Configure firewall rules if using notifications
- **Service Account**: Use dedicated service account with minimal required permissions

### Best Practices
1. **Principle of Least Privilege**: Grant only necessary permissions
2. **Regular Monitoring**: Review audit logs regularly
3. **Backup Strategy**: Implement regular backup verification
4. **Network Security**: Secure notification endpoints
5. **Configuration Security**: Protect configuration files from unauthorized access

## 🔧 Troubleshooting

### Common Issues

#### Permission Denied Errors
```
ISSUE: Access denied to IIS configuration
SOLUTION: 
- Ensure running as Administrator
- Verify user is in IIS_IUSRS group
- Check UAC settings
```

#### File System Monitoring Issues
```
ISSUE: File changes not detected
SOLUTION:
- Verify watch folder paths exist
- Check exclude patterns
- Ensure sufficient disk space for logs
- Verify file system permissions
```

#### Application Pool Management Issues
```
ISSUE: Cannot stop/start application pools
SOLUTION:
- Verify application pool exists
- Check IIS service status
- Ensure no other processes are managing the pool
- Review IIS logs for conflicts
```

### Diagnostic Commands
```bash
# Validate system requirements
IISDeploymentAutomation.exe --validate

# Check application pool status
IISDeploymentAutomation.exe --list-pools

# Review logs
Get-Content ".\Logs\deployment-*.log" | Select-String "ERROR"
```

### Performance Tuning

#### File System Monitoring
- Adjust `batchDelayMilliseconds` based on deployment frequency
- Optimize `excludePatterns` to reduce unnecessary processing
- Monitor disk I/O during peak hours

#### Deployment Optimization
- Set appropriate `maxConcurrentDeployments` based on system resources
- Tune `timeoutSeconds` for application pool operations
- Configure `maxRetries` based on environment stability

## 📝 Change Log

### Version 1.0.0
- Initial release
- Core deployment automation functionality
- IIS application pool management
- File system monitoring with batching
- Comprehensive audit logging
- Permission validation system
- Configuration management
- Command line interface

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass
6. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🆘 Support

For support and questions:
- Create an issue in the repository
- Review the troubleshooting section
- Check the audit logs for detailed error information
- Validate system requirements and permissions

## 🔗 Related Resources

- [IIS Administration Documentation](https://docs.microsoft.com/en-us/iis/)
- [.NET Core Hosting Documentation](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/)
- [Serilog Documentation](https://serilog.net/)
- [Windows Services Documentation](https://docs.microsoft.com/en-us/dotnet/core/extensions/windows-service)
