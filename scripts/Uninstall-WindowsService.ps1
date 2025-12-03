<#
.SYNOPSIS
    Uninstalls the DbExecPlanMonitor Windows Service.

.DESCRIPTION
    Stops and removes the Windows Service, optionally removing the installation directory.

.PARAMETER ServiceName
    The name of the Windows Service. Default: DbExecPlanMonitor

.PARAMETER RemoveFiles
    If specified, removes the installation directory after uninstalling.

.PARAMETER InstallPath
    Installation directory to remove (only used with -RemoveFiles). Default: C:\Services\DbExecPlanMonitor

.EXAMPLE
    .\Uninstall-WindowsService.ps1

.EXAMPLE
    .\Uninstall-WindowsService.ps1 -RemoveFiles

.NOTES
    Requires Administrator privileges.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "DbExecPlanMonitor",
    [switch]$RemoveFiles,
    [string]$InstallPath = "C:\Services\DbExecPlanMonitor"
)

$ErrorActionPreference = "Stop"

# Check for admin privileges
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "This script requires Administrator privileges. Please run as Administrator."
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "DbExecPlanMonitor Service Uninstall" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if service exists
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Service '$ServiceName' not found." -ForegroundColor Yellow
    
    if ($RemoveFiles -and (Test-Path $InstallPath)) {
        Write-Host "Removing installation directory: $InstallPath" -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
        Write-Host "Directory removed." -ForegroundColor Green
    }
    
    exit 0
}

Write-Host "Found service: $ServiceName" -ForegroundColor White
Write-Host "  Status: $($service.Status)" -ForegroundColor White
Write-Host ""

# Stop the service if running
if ($service.Status -eq 'Running') {
    Write-Host "Stopping service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force
    
    # Wait for service to stop
    $timeout = 30
    $elapsed = 0
    while ((Get-Service -Name $ServiceName).Status -ne 'Stopped' -and $elapsed -lt $timeout) {
        Start-Sleep -Seconds 1
        $elapsed++
    }
    
    if ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
        Write-Error "Failed to stop service within $timeout seconds."
        exit 1
    }
    
    Write-Host "Service stopped." -ForegroundColor Green
}

# Remove the service
Write-Host "Removing service..." -ForegroundColor Yellow
sc.exe delete $ServiceName | Out-Null

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to remove service."
    exit 1
}

Write-Host "Service removed." -ForegroundColor Green

# Remove installation directory if requested
if ($RemoveFiles) {
    Write-Host ""
    if (Test-Path $InstallPath) {
        Write-Host "Removing installation directory: $InstallPath" -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
        Write-Host "Directory removed." -ForegroundColor Green
    } else {
        Write-Host "Installation directory not found: $InstallPath" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Uninstall Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
