
# IIS Deployment Automation - Quick Start Demo

## 🚀 Quick Start Guide

This guide demonstrates how to set up and use the IIS Deployment Automation System.

### Prerequisites Check

1. **Verify Administrator Privileges**
   ```powershell
   # Check if running as admin
   ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
   ```

2. **Verify IIS Installation**
   ```powershell
   # Check IIS features
   Get-WindowsFeature -Name *IIS* | Where-Object {$_.InstallState -eq "Installed"}
   
   # Check IIS service status
   Get-Service W3SVC, WAS
   ```

3. **Check .NET 8.0 Runtime**
   ```powershell
   dotnet --version
   ```

### Demo Setup

#### Step 1: Create Demo Applications
```powershell
# Create demo source directories
New-Item -Path "C:\Demo\Source\MainApp" -ItemType Directory -Force
New-Item -Path "C:\Demo\Source\ApiApp" -ItemType Directory -Force

# Create demo IIS directories
New-Item -Path "C:\inetpub\wwwroot\MainApp" -ItemType Directory -Force
New-Item -Path "C:\inetpub\wwwroot\ApiApp" -ItemType Directory -Force

# Create sample files
@"
<!DOCTYPE html>
<html>
<head><title>Main Application</title></head>
<body>
    <h1>Main Application - Demo</h1>
    <p>Last updated: $(Get-Date)</p>
</body>
</html>
"@ | Out-File -FilePath "C:\Demo\Source\MainApp\index.html" -Encoding UTF8

@"
<!DOCTYPE html>
<html>
<head><title>API Application</title></head>
<body>
    <h1>API Application - Demo</h1>
    <p>Last updated: $(Get-Date)</p>
    <p>API Status: Running</p>
</body>
</html>
"@ | Out-File -FilePath "C:\Demo\Source\ApiApp\index.html" -Encoding UTF8
```

#### Step 2: Create Demo Application Pools
```powershell
# Import IIS module
Import-Module WebAdministration

# Create application pools
New-WebAppPool -Name "MainAppPool" -Force
New-WebAppPool -Name "ApiAppPool" -Force

# Configure application pools
Set-ItemProperty -Path "IIS:\AppPools\MainAppPool" -Name processModel.identityType -Value ApplicationPoolIdentity
Set-ItemProperty -Path "IIS:\AppPools\ApiAppPool" -Name processModel.identityType -Value ApplicationPoolIdentity

# Create IIS applications
New-WebApplication -Site "Default Web Site" -Name "MainApp" -PhysicalPath "C:\inetpub\wwwroot\MainApp" -ApplicationPool "MainAppPool" -Force
New-WebApplication -Site "Default Web Site" -Name "ApiApp" -PhysicalPath "C:\inetpub\wwwroot\ApiApp" -ApplicationPool "ApiAppPool" -Force

# Verify setup
Get-WebAppPoolState -Name "MainAppPool", "ApiAppPool"
Get-WebApplication -Site "Default Web Site"
```

#### Step 3: Configure the Automation System
Update the `config.json` file:

```json
{
  "applications": [
    {
      "name": "MainApplication",
      "applicationPoolName": "MainAppPool",
      "siteName": "Default Web Site",
      "sourcePath": "C:\\Demo\\Source\\MainApp",
      "destinationPath": "C:\\inetpub\\wwwroot\\MainApp",
      "watchFolders": ["C:\\Demo\\Source\\MainApp"],
      "excludePatterns": ["*.log", "*.tmp", ".git/*"],
      "isEnabled": true,
      "priority": 1,
      "maxRetries": 3,
      "timeoutSeconds": 300
    },
    {
      "name": "ApiApplication",
      "applicationPoolName": "ApiAppPool",
      "siteName": "Default Web Site",
      "sourcePath": "C:\\Demo\\Source\\ApiApp",
      "destinationPath": "C:\\inetpub\\wwwroot\\ApiApp",
      "watchFolders": ["C:\\Demo\\Source\\ApiApp"],
      "excludePatterns": ["*.log", "*.tmp", ".git/*"],
      "isEnabled": true,
      "priority": 2,
      "maxRetries": 3,
      "timeoutSeconds": 300
    }
  ],
  "globalSettings": {
    "enableFileSystemWatcher": true,
    "batchDelayMilliseconds": 5000,
    "maxConcurrentDeployments": 2,
    "backupEnabled": true,
    "backupPath": "C:\\Demo\\Backups",
    "maxBackupDays": 7,
    "requireAdminPrivileges": true,
    "validateIISPermissions": true,
    "enableHealthCheck": true,
    "healthCheckTimeoutSeconds": 60
  }
}
```

### Demo Execution

#### Scenario 1: System Validation
```powershell
# Navigate to the application directory
cd "D:\Keval\POC\DeployAutomation\IISDeploymentAutomation"

# Run system validation
.\bin\Debug\net8.0\IISDeploymentAutomation.exe --validate
```

**Expected Output:**
```
✓ Administrator privileges verified
✓ IIS Management Access
✓ File System Access
✓ Windows Service Access
✓ Registry Access
✓ Process Management Access
✓ Configuration loaded successfully (2 applications configured)
```

#### Scenario 2: List IIS Components
```powershell
# List application pools
.\bin\Debug\net8.0\IISDeploymentAutomation.exe --list-pools

# List sites
.\bin\Debug\net8.0\IISDeploymentAutomation.exe --list-sites
```

#### Scenario 3: Manual Deployment
```powershell
# Trigger manual deployment for MainApplication
.\bin\Debug\net8.0\IISDeploymentAutomation.exe --deploy MainApplication
```

#### Scenario 4: Interactive Monitoring
```powershell
# Start interactive mode
.\bin\Debug\net8.0\IISDeploymentAutomation.exe
```

**Interactive Commands:**
- Press `h` for help
- Press `s` for system status
- Press `q` to quit

### Demo Test Scenarios

#### Test 1: File Change Detection
1. Start the automation system in interactive mode
2. In another terminal, modify a file:
   ```powershell
   # Update the main app file
   @"
   <!DOCTYPE html>
   <html>
   <head><title>Main Application - Updated</title></head>
   <body>
       <h1>Main Application - UPDATED VERSION</h1>
       <p>Last updated: $(Get-Date)</p>
       <p>Version: 2.0</p>
   </body>
   </html>
   "@ | Out-File -FilePath "C:\Demo\Source\MainApp\index.html" -Encoding UTF8
   ```
3. Watch the automation system detect the change and deploy automatically

#### Test 2: Multiple File Changes
```powershell
# Create multiple files to test batching
"Content 1" | Out-File -FilePath "C:\Demo\Source\MainApp\file1.txt"
"Content 2" | Out-File -FilePath "C:\Demo\Source\MainApp\file2.txt"
"Content 3" | Out-File -FilePath "C:\Demo\Source\MainApp\file3.txt"

# Add a CSS file
@"
body {
    font-family: Arial, sans-serif;
    background-color: #f0f0f0;
    margin: 0;
    padding: 20px;
}
h1 {
    color: #333;
    border-bottom: 2px solid #007acc;
}
"@ | Out-File -FilePath "C:\Demo\Source\MainApp\styles.css" -Encoding UTF8
```

#### Test 3: Application Pool Management
```powershell
# Manually stop an application pool
Stop-WebAppPool -Name "MainAppPool"

# Check automation system detects and logs the state change
# Then restart it
Start-WebAppPool -Name "MainAppPool"
```

### Monitoring and Logs

#### View Application Logs
```powershell
# View today's deployment logs
Get-Content ".\Logs\deployment-$(Get-Date -Format 'yyyy-MM-dd').log" | Select-Object -Last 50

# View audit logs
Get-Content ".\Logs\Audit\audit-$(Get-Date -Format 'yyyy-MM-dd').json" | ConvertFrom-Json | Format-Table Timestamp, Category, Message
```

#### Monitor Application Pool States
```powershell
# Check application pool states
Get-WebAppPoolState -Name "MainAppPool", "ApiAppPool"

# Check application URLs
Invoke-WebRequest -Uri "http://localhost/MainApp/" -UseBasicParsing
Invoke-WebRequest -Uri "http://localhost/ApiApp/" -UseBasicParsing
```

### Performance Testing

#### Stress Test File Changes
```powershell
# Create a script to generate multiple file changes
for ($i = 1; $i -le 10; $i++) {
    "Test content $i - $(Get-Date)" | Out-File -FilePath "C:\Demo\Source\MainApp\test$i.txt"
    Start-Sleep -Milliseconds 500
}
```

#### Monitor System Resources
```powershell
# Monitor CPU and memory usage
Get-Process -Name "IISDeploymentAutomation" | Select-Object CPU, WorkingSet, VirtualMemorySize

# Monitor IIS worker processes
Get-Process -Name "w3wp" | Select-Object Id, ProcessName, WorkingSet
```

### Troubleshooting Demo

#### Common Issues and Solutions

1. **Permission Denied**
   ```powershell
   # Verify running as admin
   if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
       Write-Host "Please run PowerShell as Administrator" -ForegroundColor Red
   }
   ```

2. **Application Pool Won't Start**
   ```powershell
   # Check IIS logs
   Get-EventLog -LogName System -Source "Microsoft-Windows-IIS-W3SVC" -Newest 10
   
   # Check application pool configuration
   Get-ItemProperty -Path "IIS:\AppPools\MainAppPool" | Format-List
   ```

3. **File Access Issues**
   ```powershell
   # Check directory permissions
   Get-Acl "C:\Demo\Source\MainApp" | Format-List
   Get-Acl "C:\inetpub\wwwroot\MainApp" | Format-List
   ```

### Cleanup Demo Environment
```powershell
# Stop automation system (if running)
# Then cleanup demo resources

# Remove IIS applications
Remove-WebApplication -Site "Default Web Site" -Name "MainApp"
Remove-WebApplication -Site "Default Web Site" -Name "ApiApp"

# Remove application pools
Remove-WebAppPool -Name "MainAppPool"
Remove-WebAppPool -Name "ApiAppPool"

# Remove demo directories
Remove-Item -Path "C:\Demo" -Recurse -Force
Remove-Item -Path "C:\inetpub\wwwroot\MainApp" -Recurse -Force
Remove-Item -Path "C:\inetpub\wwwroot\ApiApp" -Recurse -Force
```

### Demo Success Metrics

After completing the demo, you should have:

1. ✅ **System Validation**: All permission checks passed
2. ✅ **Automatic Deployment**: File changes trigger deployments
3. ✅ **Application Pool Management**: Pools stop/start correctly
4. ✅ **Batch Processing**: Multiple file changes processed efficiently
5. ✅ **Logging**: Comprehensive audit trail created
6. ✅ **Error Handling**: Graceful handling of edge cases
7. ✅ **Performance**: System handles concurrent operations

### Next Steps

1. **Production Setup**: Adapt configuration for production environment
2. **Service Installation**: Install as Windows Service for production
3. **Notification Setup**: Configure email/Teams notifications
4. **Monitoring Integration**: Integrate with existing monitoring systems
5. **Backup Strategy**: Implement backup verification and restore procedures

This demo showcases the enterprise-grade capabilities of the IIS Deployment Automation System!
