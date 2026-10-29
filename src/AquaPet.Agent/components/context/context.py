"""Build the immutable base prompt context for AquaPet LangGraph flows."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Iterable


_BASIC_CONTEXT_FILE = Path(__file__).with_name("basic.json")
_REQUIRED_FIELDS = ("basic_prompt", "role_prompt", "style")


def load_basic_context(path: Path = _BASIC_CONTEXT_FILE) -> dict[str, str]:
    """Load and validate the three required base-context fragments."""
    try:
        raw_context = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError as error:
        raise RuntimeError(f"Basic context file was not found: {path}") from error
    except json.JSONDecodeError as error:
        raise RuntimeError(f"Basic context is not valid JSON: {path}") from error

    missing_fields = [field for field in _REQUIRED_FIELDS if field not in raw_context]
    if missing_fields:
        raise RuntimeError(f"Basic context is missing fields: {', '.join(missing_fields)}")

    return {field: str(raw_context[field]) for field in _REQUIRED_FIELDS}


def build_base_messages(path: Path = _BASIC_CONTEXT_FILE) -> list[dict[str, str]]:
    """Create the base conversation in its required prompt-engineering order."""
    context = load_basic_context(path)
    return [
        {"role": "user", "content": context["basic_prompt"]},
        {"role": "assistant", "content": "ok"},
        {"role": "user", "content": context["style"]},
        {"role": "user", "content": context["role_prompt"]},
    ]


def build_context(
    prompts: Iterable[dict[str, Any]],
    path: Path = _BASIC_CONTEXT_FILE,
) -> list[dict[str, Any]]:
    """Append later LangGraph prompts after the stable base context."""
    return [*build_base_messages(path), *prompts]
