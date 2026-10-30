import json
import hashlib

from langchain_core.tools import tool

from components.sandbox.session import get_current_session
from components.skills.loader import discover_skills


@tool
async def load_skill_instructions(name: str) -> str:
    """加载指定 Skill 的说明，并将其文件复制到当前沙盒。"""

    skills = discover_skills()

    for skill in skills:
        if skill.name == name:
            session = get_current_session()
            project_path = await session.stage_skill(skill.project_directory)
            skill_key = hashlib.sha256(skill.name.encode("utf-8")).hexdigest()[:24]
            skill_data_path = f"/persist/skill-data/{skill_key}"
            await session.prepare_skill_data(skill_data_path)
            relative_path = skill.directory.relative_to(skill.project_directory).as_posix()
            sandbox_path = project_path if relative_path == "." else f"{project_path}/{relative_path}"

            return json.dumps(
                {
                    "name": skill.name,
                    "description": skill.description,
                    "instructions": skill.content,
                    "sandbox_path": sandbox_path,
                    "project_path": project_path,
                    "persistent_paths": {
                        "python_environment": "/persist/python",
                        "python_executable": "/persist/python/bin/python",
                        "skill_home": skill_data_path,
                    },
                    "note": (
                        "完整项目已复制到 project_path，Skill 说明及其相对资源位于 sandbox_path。"
                        "skill_home 为这个 Skill 的持久目录。每次运行依赖 HOME 的命令时，"
                        "在同一条 shell 命令中设置 HOME 为 skill_home；shell 命令间环境不共享。"
                        "先检查持久 Python 环境及依赖，再使用它安装并验证；不要将临时文件当作持久安装。"
                    ),
                },
                ensure_ascii=False,
            )

    return json.dumps(
        {
            "error": f"没有找到 Skill：{name}",
        },
        ensure_ascii=False,
    )
