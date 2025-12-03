<#
.SYNOPSIS
    Installs DbExecPlanMonitor as a Windows Service.

.DESCRIPTION
    This script publishes the application and registers it as a Windows Service
    using the built-in sc.exe utility.

.PARAMETER ServiceName
    The name of the Windows Service. Default: DbExecPlanMonitor

.PARAMETER DisplayName
    The display name shown in Services console. Default: Database Execution Plan Monitor

.PARAMETER InstallPath
    Installation directory. Default: C:\Services\DbExecPlanMonitor

.PARAMETER Environment
    The environment name (Development, Staging, Production). Default: Production

.PARAMETER StartupType
    Service startup type (Auto, Manual, Disabled). Default: Auto

.EXAMPLE
    .\Install-WindowsService.ps1

.EXAMPLE
    .\Install-WindowsService.ps1 -Environment Staging -InstallPath D:\Services\DbExecPlanMonitor

.NOTES
    Requires Administrator privileges.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "DbExecPlanMonitor",
    [string]$DisplayName = "Database Execution Plan Monitor",
    [string]$InstallPath = "C:\Services\DbExecPlanMonitor",
    [ValidateSet("Development", "Staging", "Production")]
    [string]$Environment = "Production",
    [ValidateSet("Auto", "Manual", "Disabled")]
    [string]$StartupType = "Auto"
)

$ErrorActionPreference = "Stop"

# Check for admin privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DbExecPlanMonitor Windows Service Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Find the solution root
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SolutionRoot = Split-Path -Parent $ScriptDir
$WorkerProject = Join-Path $SolutionRoot "src\DbExecPlanMonitor.Worker"

if (-not (Test-Path $WorkerProject)) {
    Write-Error "Worker project not found at: $WorkerProject"
    exit 1
}

# Check if service already exists
$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service '$ServiceName' already exists." -ForegroundColor Yellow
    Write-Host "Current Status: $($existingService.Status)" -ForegroundColor Yellow
    
    $response = Read-Host "Do you want to stop and remove the existing service? (y/N)"
    if ($response -eq 'y' -or $response -eq 'Y') {
        Write-Host "Stopping service..." -ForegroundColor Yellow
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        
        Write-Host "Removing service..." -ForegroundColor Yellow
        sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    } else {
        Write-Host "Installation cancelled." -ForegroundColor Yellow
        exit 0
    }
}

# Publish the application
Write-Host ""
Write-Host "Step 1: Publishing application..." -ForegroundColor Green
Write-Host "  Project: $WorkerProject"
Write-Host "  Output:  $InstallPath"
Write-Host ""

if (Test-Path $InstallPath) {
    Write-Host "Cleaning existing installation directory..." -ForegroundColor Yellow
    Remove-Item -Path $InstallPath -Recurse -Force
}

dotnet publish $WorkerProject `
    -c Release `
    -o $InstallPath `
    -r win-x64 `
    --self-contained false

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to publish application."
    exit 1
}

Write-Host "Application published successfully." -ForegroundColor Green

# Copy environment-specific config
$sourceConfig = Join-Path $WorkerProject "appsettings.$Environment.json"
if (Test-Path $sourceConfig) {
    Write-Host "Copying $Environment configuration..." -ForegroundColor Green
    Copy-Item $sourceConfig -Destination $InstallPath -Force
}

# Set environment variable in the service
$exePath = Join-Path $InstallPath "DbExecPlanMonitor.Worker.exe"

# Create the service
Write-Host ""
Write-Host "Step 2: Creating Windows Service..." -ForegroundColor Green
Write-Host "  Name:    $ServiceName"
Write-Host "  Display: $DisplayName"
Write-Host "  Path:    $exePath"
Write-Host ""

$binPath = "`"$exePath`""
sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "$DisplayName"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service."
    exit 1
}

# Configure service description
sc.exe description $ServiceName "Monitors SQL Server execution plans for performance regressions and suggests remediation actions."

# Configure recovery options (restart on failure)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

# Set startup type
switch ($StartupType) {
    "Auto" { sc.exe config $ServiceName start= auto }
    "Manual" { sc.exe config $ServiceName start= demand }
    "Disabled" { sc.exe config $ServiceName start= disabled }
}

Write-Host "Service created successfully." -ForegroundColor Green

# Set environment variable for the service
Write-Host ""
Write-Host "Step 3: Configuring environment..." -ForegroundColor Green

# Create a wrapper batch file to set environment
$wrapperPath = Join-Path $InstallPath "start-service.cmd"
$wrapperContent = @"
@echo off
set DOTNET_ENVIRONMENT=$Environment
"$exePath"
"@
Set-Content -Path $wrapperPath -Value $wrapperContent

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Installation Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Service Details:" -ForegroundColor White
Write-Host "  Name:        $ServiceName"
Write-Host "  Path:        $InstallPath"
Write-Host "  Environment: $Environment"
Write-Host "  Startup:     $StartupType"
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Yellow
Write-Host "  1. Configure connection strings in appsettings.$Environment.json"
Write-Host "  2. Start the service: Start-Service $ServiceName"
Write-Host "  3. Check logs in: $InstallPath\logs\"
Write-Host ""
Write-Host "Commands:" -ForegroundColor White
Write-Host "  Start:   Start-Service $ServiceName"
Write-Host "  Stop:    Stop-Service $ServiceName"
Write-Host "  Status:  Get-Service $ServiceName"
Write-Host "  Remove:  sc.exe delete $ServiceName"
Write-Host ""
