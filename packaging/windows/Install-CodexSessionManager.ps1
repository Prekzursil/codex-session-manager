param(
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -Path (Join-Path $scriptRoot '*') -Destination $InstallRoot -Recurse -Force
Remove-Item -Path (Join-Path $InstallRoot 'Install-CodexSessionManager.ps1') -Force -ErrorAction SilentlyContinue
Write-Host "Installed Codex Session Manager to $InstallRoot"
