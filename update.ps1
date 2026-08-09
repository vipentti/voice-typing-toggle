# Rebuilds Voice Typing Toggle and restarts it from the publish output.
# Usage: ./update.ps1
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\VoiceTypingToggle\VoiceTypingToggle.csproj'
$exe     = Join-Path $PSScriptRoot 'src\VoiceTypingToggle\bin\Release\net10.0-windows\win-x64\publish\VoiceTypingToggle.exe'

$running = Get-Process VoiceTypingToggle -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping VoiceTypingToggle (PID $($running.Id))..."
    $running | ForEach-Object {
        Stop-Process -Id $_.Id -Force
        while (Get-Process -Id $_.Id -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 100 }
    }
} else {
    Write-Host 'VoiceTypingToggle is not running.'
}

Write-Host 'Publishing (Native AOT)...'
dotnet publish $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Publish failed; not starting the app.' }

Write-Host "Starting $exe"
Start-Process $exe
Write-Host 'Done.'
