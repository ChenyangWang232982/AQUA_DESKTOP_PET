from dataclasses import dataclass
from datetime import timedelta

from opensandbox import Sandbox
from opensandbox.config import ConnectionConfig
from opensandbox.models import WriteEntry
from opensandbox.models.execd import RunCommandOpts

@dataclass
class SandboxResult:
    stdout: str
    stderr: str
    exit_code: int | None
    error: str | None

async def _execute(command: str, script: str | None = None) -> SandboxResult:
    """在新沙盒中执行命令，并在完成后销毁沙盒。"""

    sandbox = await Sandbox.create(
        "python:3.12",
        timeout=timedelta(minutes=10),
        resource={
            "cpu": "1",
            "memory": "512Mi",
        },
        connection_config=ConnectionConfig(
            domain="localhost:8080",
            use_server_proxy=False,
        ),
    )

    try:
        if script is not None:
            await sandbox.files.write_files(
                [WriteEntry(path="/tmp/skill.py", data=script, mode=644)]
            )

        execution = await sandbox.commands.run(
            command,
            opts=RunCommandOpts(
                timeout=timedelta(seconds=20),
            ),
        )

        # 将多条日志合并成字符串。
        stdout = "".join(item.text for item in execution.logs.stdout)
        stderr = "".join(item.text for item in execution.logs.stderr)

        return SandboxResult(
            stdout=stdout,
            stderr=stderr,
            exit_code=execution.exit_code,
            error=str(execution.error) if execution.error else None,
        )

    finally:
        # 即使写文件或执行脚本时报错，也尝试销毁沙盒。
        await sandbox.destroy()


async def execute_python(script: str) -> SandboxResult:
    """在沙盒中运行 Python 代码，并返回结果。"""
    return await _execute("python /tmp/skill.py", script)


async def execute_command(command: str) -> SandboxResult:
    """在沙盒中使用 Linux shell 运行命令。"""
    return await _execute(command)
