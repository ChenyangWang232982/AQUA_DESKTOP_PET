import json

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
            sandbox_path = await session.stage_skill(skill.directory)

            return json.dumps(
                {
                    "name": skill.name,
                    "description": skill.description,
                    "instructions": skill.content,
                    "sandbox_path": sandbox_path,
                    "note": (
                        "Skill 的文件已复制到 sandbox_path。"
                        "使用脚本或资源时，请从这个目录寻找。"
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