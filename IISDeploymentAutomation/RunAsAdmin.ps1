# Run IIS Deployment Automation as Administrator
# This script ensures the application runs with proper privileges

param(
    [Parameter(Mandatory=$false)]
    [string]$Command = "--validate",
    
    [Parameter(Mandatory=$false)]
    [switch]$ShowHelp
)

# Function to check if running as administrator
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Function to run as administrator
function Start-AsAdmin {
    param([string]$Arguments)
    
    $exePath = Join-Path $PSScriptRoot "bin\Debug\net8.0\IISDeploymentAutomation.exe"
    
    if (Test-Path $exePath) {
        $processArgs = @{
            FilePath = $exePath
            Verb = "RunAs"
            ArgumentList = $Arguments
            Wait = $true
        }
        
        try {
            Start-Process @processArgs
        }
        catch {
            Write-Error "Failed to start application as administrator: $_"
        }
    }
    else {
        Write-Error "Application not found at: $exePath"
        Write-Host "Please ensure the application has been built successfully." -ForegroundColor Yellow
        Write-Host "Run: dotnet build" -ForegroundColor Cyan
    }
}

# Show help if requested
if ($ShowHelp) {
    Write-Host @"
IIS Deployment Automation - Admin Launcher

Usage:
    .\RunAsAdmin.ps1 [Command]

Commands:
    --validate          Validate system requirements and permissions
    --list-pools        List all IIS application pools
    --list-sites        List all IIS sites
    --deploy <name>     Deploy specific application
    --interactive       Start in interactive mode (default if no command)
    --help              Show application help

Examples:
    .\RunAsAdmin.ps1 --validate
    .\RunAsAdmin.ps1 --list-pools
    .\RunAsAdmin.ps1 --deploy "MainApplication"
    .\RunAsAdmin.ps1 --interactive

Note: This script will automatically request administrator privileges.
"@ -ForegroundColor Green
    return
}

# Main execution
Write-Host "IIS Deployment Automation - Admin Launcher" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

if (Test-Administrator) {
    Write-Host "Already running as Administrator." -ForegroundColor Green
    
    # Run directly
    $exePath = Join-Path $PSScriptRoot "bin\Debug\net8.0\IISDeploymentAutomation.exe"
    if (Test-Path $exePath) {
        & $exePath $Command
    }
    else {
        Write-Error "Application not found. Please build the project first."
    }
}
else {
    Write-Host "Requesting Administrator privileges..." -ForegroundColor Yellow
    Start-AsAdmin -Arguments $Command
}
