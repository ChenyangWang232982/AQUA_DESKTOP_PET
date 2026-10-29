import json

from langchain_core.tools import tool

from components.sandbox.session import get_current_session


@tool
async def run_python_in_sandbox(script: str) -> str:
    """在当前任务的沙盒中运行 Python 代码。"""

    session = get_current_session()
    result = await session.run_python(script)

    return json.dumps(
        {
            "stdout": result.stdout,
            "stderr": result.stderr,
            "exit_code": result.exit_code,
            "error": result.error,
        },
        ensure_ascii=False,
    )

@tool
async def run_command_in_sandbox(command: str, timeout_seconds: int = 20) -> str:
    """在当前任务的 Linux 沙盒中运行 shell 命令；timeout_seconds 为 1 到 300 秒。"""

    if not 1 <= timeout_seconds <= 300:
        raise ValueError("命令执行时限必须在 1 到 300 秒之间。")

    session = get_current_session()
    result = await session.run_command(command, timeout_seconds=timeout_seconds)

    return json.dumps(
        {
            "stdout": result.stdout,
            "stderr": result.stderr,
            "exit_code": result.exit_code,
            "error": result.error,
        },
        ensure_ascii=False,
    )
