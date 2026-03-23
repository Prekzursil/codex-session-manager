param(
    [Parameter(Mandatory = $false)]
    [string]$SourceRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path),

    [Parameter(Mandatory = $false)]
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

$publishRoot = Join-Path $SourceRoot "publish"

if (-not (Test-Path $publishRoot)) {
    throw "Expected publish directory at $publishRoot."
}

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -Recurse -Force -Path (Join-Path $publishRoot '*') -Destination $InstallRoot

Write-Host "Installed Codex Session Manager to $InstallRoot"
