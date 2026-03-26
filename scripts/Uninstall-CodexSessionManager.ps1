param(
    [Parameter(Mandatory = $false)]
    [string]$InstallRoot = "$env:LOCALAPPDATA\\Programs\\CodexSessionManager"
)

if (Test-Path $InstallRoot) {
    Remove-Item -Recurse -Force $InstallRoot
    Write-Output "Removed Codex Session Manager from $InstallRoot"
} else {
    Write-Output "Nothing to remove at $InstallRoot"
}
