from dataclasses import dataclass
from pathlib import Path
import logging
import os

import yaml


# 从当前文件的位置找到项目根目录，再进入 skills 文件夹。
SKILLS_DIR = Path(__file__).resolve().parents[4] / "skills"
logger = logging.getLogger(__name__)


@dataclass
class Skill:
    """一个已读取的 Skill。"""

    name: str
    description: str
    content: str
    directory: Path
    project_directory: Path


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

    skill_directory = skill_directory.resolve()
    relative = skill_directory.relative_to(SKILLS_DIR.resolve())
    project_directory = SKILLS_DIR.resolve() / relative.parts[0] if relative.parts else skill_directory

    return Skill(
        name=name,
        description=description,
        content=parts[2].strip(),
        directory=skill_directory,
        project_directory=project_directory,
    )


def discover_skills() -> list[Skill]:
    """递归扫描所有项目中的 SKILL.md，不跟随符号链接。"""

    if not SKILLS_DIR.is_dir():
        return []

    skills = []

    seen: dict[str, Path] = {}
    for root, directories, files in os.walk(SKILLS_DIR, followlinks=False):
        directories[:] = sorted(d for d in directories if d not in {".git", "__pycache__", ".venv", "node_modules"} and not (Path(root) / d).is_symlink())
        path = Path(root) / "SKILL.md"
        if "SKILL.md" not in files or path.is_symlink():
            continue
        try:
            skill = load_skill(path.parent)
        except (ValueError, OSError, yaml.YAMLError) as error:
            logger.warning("Skipping invalid skill %s: %s", path, error)
            continue
        if skill.name in seen:
            raise ValueError(f"重复的 Skill 名称 {skill.name!r}: {seen[skill.name]} 和 {path}")
        seen[skill.name] = path
        skills.append(skill)

    return skills
