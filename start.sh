#!/usr/bin/env bash
# Start AquaPet's local LangGraph service, then its WPF desktop application.
# Intended for Git Bash or WSL on Windows.

set -Eeuo pipefail

PROJECT_ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
AGENT_DIRECTORY="$PROJECT_ROOT/src/AquaPet.Agent"
PYTHON_EXE='D:\work\conda\envs\pet\python.exe'
AGENT_PORT=8765
AGENT_PID=''

if command -v wslpath >/dev/null 2>&1; then
    PROJECT_ROOT_WINDOWS="$(wslpath -w "$PROJECT_ROOT")"
else
    PROJECT_ROOT_WINDOWS="$(cygpath -w "$PROJECT_ROOT")"
fi

stop_agent() {
    if [[ -n "$AGENT_PID" ]]; then
        powershell.exe -NoProfile -Command "Stop-Process -Id $AGENT_PID -Force -ErrorAction SilentlyContinue"
    fi
}

trap stop_agent EXIT INT TERM

if ! powershell.exe -NoProfile -Command "& '$PYTHON_EXE' -c \"import fastapi, uvicorn, langgraph, langchain_openai\""; then
    echo "AquaPet Agent dependencies are missing. Run:"
    echo "  D:\\work\\conda\\envs\\pet\\python.exe -m pip install fastapi \"uvicorn[standard]\" langgraph langchain-openai"
    exit 1
fi

echo "Starting AquaPet Agent on http://127.0.0.1:$AGENT_PORT ..."
AGENT_PID="$(powershell.exe -NoProfile -Command "\$process = Start-Process -FilePath '$PYTHON_EXE' -ArgumentList '-m', 'uvicorn', 'main:app', '--host', '127.0.0.1', '--port', '$AGENT_PORT' -WorkingDirectory '$PROJECT_ROOT_WINDOWS\\src\\AquaPet.Agent' -WindowStyle Hidden -PassThru; \$process.Id")"

for _ in {1..20}; do
    if powershell.exe -NoProfile -Command "try { \$response = Invoke-WebRequest -UseBasicParsing http://127.0.0.1:$AGENT_PORT/health; exit [int](\$response.StatusCode -ne 200) } catch { exit 1 }"; then
        echo "Agent is ready. Starting AquaPet Desktop..."
        break
    fi
    sleep 1
done

if ! powershell.exe -NoProfile -Command "try { \$response = Invoke-WebRequest -UseBasicParsing http://127.0.0.1:$AGENT_PORT/health; exit [int](\$response.StatusCode -ne 200) } catch { exit 1 }"; then
    echo "The Agent did not become ready. Check the Agent logs."
    exit 1
fi

powershell.exe -NoProfile -Command "Set-Location -LiteralPath '$PROJECT_ROOT_WINDOWS'; dotnet run --project .\\src\\AquaPet.Desktop\\AquaPet.Desktop.csproj"
