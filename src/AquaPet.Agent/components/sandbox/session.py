import asyncio
import io
import gzip
import hashlib
import os
import shlex
import tarfile
import uuid
from datetime import timedelta

from opensandbox import Sandbox
from opensandbox.config import ConnectionConfig
from opensandbox.models.execd import RunCommandOpts

from components.sandbox.executor import SandboxResult

from contextlib import asynccontextmanager
from contextvars import ContextVar

from opensandbox.models import WriteEntry
from opensandbox.models.sandboxes import PVC, Volume

from pathlib import Path

class SandboxSession:
    """一个任务使用的沙盒会话。"""

    def __init__(self) -> None:
        self._sandbox: Sandbox | None = None
        self._lock = asyncio.Lock()
        self._closed = False

    async def __aenter__(self) -> "SandboxSession":
        """进入任务时只创建会话，沙盒在首次调用工具时创建。"""
        return self

    async def _ensure_sandbox(self) -> Sandbox:
        """调用方持有锁，保证并发工具调用只创建一个沙盒。"""
        if self._closed:
            raise RuntimeError("沙盒会话已结束")
        if self._sandbox is not None:
            return self._sandbox
        self._sandbox = await Sandbox.create(
            "python:3.12",
            timeout=timedelta(minutes=10),
            resource={
                "cpu": "1",
                "memory": "512Mi",
            },
            volumes=[
                Volume(
                    name="dependencies",
                    pvc=PVC(
                        claimName="aquapet-dependencies",
                        createIfNotExists=False,
                        deleteOnSandboxTermination=False,
                    ),
                    mountPath="/persist",
                    readOnly=False,
                ),
                Volume(
                    name="skills",
                    pvc=PVC(
                        claimName="aquapet-skills",
                        createIfNotExists=False,
                        deleteOnSandboxTermination=False,
                    ),
                    mountPath="/skills",
                    readOnly=False,
                ),
            ],
            env={
                "PATH": (
                    "/persist/python/bin:"
                    "/usr/local/sbin:/usr/local/bin:"
                    "/usr/sbin:/usr/bin:/sbin:/bin"
                ),
            },
            connection_config=ConnectionConfig(
                domain="localhost:8080",
                use_server_proxy=False,
                request_timeout=timedelta(seconds=330),
            ),
        )

        return self._sandbox

    async def run_command(self, command: str, timeout_seconds: int = 20) -> SandboxResult:
        """在当前沙盒中执行一条 shell 命令。"""

        async with self._lock:
            sandbox = await self._ensure_sandbox()
            return await self._run_command(sandbox, command, timeout_seconds)

    async def _run_command(self, sandbox: Sandbox, command: str, timeout_seconds: int = 20) -> SandboxResult:
        execution = await sandbox.commands.run(
            command,
            opts=RunCommandOpts(
                timeout=timedelta(seconds=timeout_seconds),
            ),
        )

        return SandboxResult(
            stdout="".join(item.text for item in execution.logs.stdout),
            stderr="".join(item.text for item in execution.logs.stderr),
            exit_code=execution.exit_code,
            error=str(execution.error) if execution.error else None,
        )

    async def run_python(self, script: str) -> SandboxResult:
        async with self._lock:
            sandbox = await self._ensure_sandbox()
            await sandbox.files.write_files(
                [WriteEntry(path="/tmp/skill.py", data=script, mode=644)]
            )
            return await self._run_command(sandbox, "python /tmp/skill.py")

    async def prepare_skill_data(self, path: str) -> None:
        """创建 Skill 专用持久目录。"""
        async with self._lock:
            sandbox = await self._ensure_sandbox()
            await sandbox.files.create_directories([WriteEntry(path=path, mode=700)])

    async def stage_skill(self, skill_directory: Path) -> str:
        """在持久卷创建完整项目的内容快照，避免并发覆盖已有快照。"""

        skill_directory = skill_directory.resolve()
        archive_data = io.BytesIO()
        with tarfile.open(fileobj=archive_data, mode="w") as archive:
            for root, directories, files in os.walk(skill_directory, followlinks=False):
                directories[:] = sorted(d for d in directories if d not in {".git", "__pycache__", ".venv", "node_modules"})
                for name in [*directories, *sorted(files)]:
                    entry = Path(root) / name
                    if entry.is_symlink() or not entry.resolve().is_relative_to(skill_directory):
                        raise ValueError(f"Skill 包含不安全的路径：{entry}")
                    if entry.is_file() or entry.is_dir():
                        archive.add(entry, arcname=entry.relative_to(skill_directory).as_posix(), recursive=False)

        archive_bytes = archive_data.getvalue()
        digest = hashlib.sha256(archive_bytes).hexdigest()
        sandbox_path = f"/skills/projects/{digest}"

        async with self._lock:
            sandbox = await self._ensure_sandbox()
            archive_path = f"/tmp/skill-{uuid.uuid4().hex}.tar.gz"
            await sandbox.files.create_directories([WriteEntry(path="/skills/projects", mode=755)])
            await sandbox.files.write_files([WriteEntry(path=archive_path, data=gzip.compress(archive_bytes, mtime=0), mode=644)])
            extract = '''import os, shutil, sys, tarfile, tempfile
archive, dest = sys.argv[1:]
if not os.path.isdir(dest):
    temp = tempfile.mkdtemp(prefix='.staging-', dir=os.path.dirname(dest))
    try:
        with tarfile.open(archive, 'r:gz') as tar:
            tar.extractall(temp, filter='data')
        try:
            os.rename(temp, dest)
        except OSError:
            if not os.path.isdir(dest):
                raise
    finally:
        if os.path.isdir(temp):
            shutil.rmtree(temp)
'''
            result = await self._run_command(
                sandbox,
                f"python -c {shlex.quote(extract)} {shlex.quote(archive_path)} {shlex.quote(sandbox_path)}",
            )
            if result.exit_code != 0 or result.error:
                raise RuntimeError(f"Skill 解压失败：{result.stderr or result.error}")
            return sandbox_path

    async def __aexit__(self, exc_type, exc_value, traceback) -> None:
        """离开 async with 时销毁沙盒。"""

        async with self._lock:
            self._closed = True
            sandbox = self._sandbox
            self._sandbox = None
            if sandbox is not None:
                await sandbox.destroy()

_current_session: ContextVar[SandboxSession | None] = ContextVar(
    "current_sandbox_session",
    default=None,
)


def get_current_session() -> SandboxSession:
    """获取当前任务绑定的沙盒会话。"""

    session = _current_session.get()

    if session is None:
        raise RuntimeError("当前任务没有绑定沙盒会话")

    return session


@asynccontextmanager
async def sandbox_task():
    """为一次任务创建并绑定沙盒会话。"""

    async with SandboxSession() as session:
        token = _current_session.set(session)

        try:
            yield session
        finally:
            _current_session.reset(token)
