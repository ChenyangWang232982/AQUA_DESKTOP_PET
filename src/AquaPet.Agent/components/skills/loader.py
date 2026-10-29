from dataclasses import dataclass
from pathlib import Path

import yaml


# 从当前文件的位置找到项目根目录，再进入 skills 文件夹。
SKILLS_DIR = Path(__file__).resolve().parents[4] / "skills"


@dataclass
class Skill:
    """一个已读取的 Skill。"""

    name: str
    description: str
    content: str
    directory: Path


def load_skill(skill_directory: Path) -> Skill:
    """读取一个技能目录中的 SKILL.md。"""

    skill_file = skill_directory / "SKILL.md"
    text = skill_file.read_text(encoding="utf-8")

    # 把文件分成：开头的空内容、YAML 信息、Markdown 正文。
    parts = text.split("---", 2)

    if len(parts) != 3 or parts[0].strip():
        raise ValueError(f"Skill 格式不正确：{skill_file}")

    metadata = yaml.safe_load(parts[1])

    if not isinstance(metadata, dict):
        raise ValueError(f"Skill 前置信息必须是字典：{skill_file}")

    name = metadata.get("name")
    description = metadata.get("description")

    if not isinstance(name, str) or not name.strip():
        raise ValueError(f"Skill 缺少有效的 name：{skill_file}")

    if not isinstance(description, str) or not description.strip():
        raise ValueError(f"Skill 缺少有效的 description：{skill_file}")

    if name != skill_directory.name:
        raise ValueError(
            f"Skill 名称 {name!r} 与目录名 {skill_directory.name!r} 不一致"
        )

    return Skill(
        name=name,
        description=description,
        content=parts[2].strip(),
        directory=skill_directory,
    )


def discover_skills() -> list[Skill]:
    """扫描 skills 目录，返回找到的技能。"""

    if not SKILLS_DIR.is_dir():
        return []

    skills = []

    for directory in sorted(SKILLS_DIR.iterdir()):
        if not directory.is_dir():
            continue

        if not (directory / "SKILL.md").is_file():
            continue

        skill = load_skill(directory)
        skills.append(skill)

    return skills