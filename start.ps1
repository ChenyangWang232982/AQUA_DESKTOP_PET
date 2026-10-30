# Start AquaPet's local LangGraph service, then its WPF desktop application.
# Run from PowerShell: .\start.ps1

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$agentDirectory = Join-Path $projectRoot 'src\AquaPet.Agent'
$pythonExe = 'D:\work\conda\envs\pet\python.exe'
$agentPort = 8765
$agentProcess = $null
$logDirectory = Join-Path $projectRoot 'logs'
$agentOutputLog = Join-Path $logDirectory 'agent-output.log'
$agentErrorLog = Join-Path $logDirectory 'agent-error.log'

if (-not (Test-Path -LiteralPath $pythonExe)) {
    throw "Python environment not found: $pythonExe"
}

try {
    & $pythonExe -s -c 'import fastapi, uvicorn, langgraph, langchain_openai'
    if ($LASTEXITCODE -ne 0) {
        throw 'AquaPet Agent dependencies are missing.'
    }

    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    Write-Host "Starting AquaPet Agent on http://127.0.0.1:$agentPort ..."
    $agentProcess = Start-Process `
        -FilePath $pythonExe `
        -ArgumentList @('-s', '-m', 'uvicorn', 'main:app', '--host', '127.0.0.1', '--port', "$agentPort") `
        -WorkingDirectory $agentDirectory `
        -RedirectStandardOutput $agentOutputLog `
        -RedirectStandardError $agentErrorLog `
        -WindowStyle Hidden `
        -PassThru

    $ready = $false
    for ($attempt = 1; $attempt -le 20; $attempt++) {
        if ($agentProcess.HasExited) {
            throw "Agent stopped unexpectedly. Check $agentErrorLog"
        }

        try {
            $health = Invoke-RestMethod -Uri "http://127.0.0.1:$agentPort/health" -TimeoutSec 1
            if ($health.ready -eq $true) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $ready) {
        throw "Agent did not become ready. Check $agentErrorLog"
    }

    Write-Host 'Agent is ready. Starting AquaPet Desktop...'
    dotnet run --project (Join-Path $projectRoot 'src\AquaPet.Desktop\AquaPet.Desktop.csproj')
}
finally {
    if ($null -ne $agentProcess -and -not $agentProcess.HasExited) {
        Stop-Process -Id $agentProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
