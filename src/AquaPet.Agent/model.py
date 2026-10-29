"""AquaPet's provider-agnostic LangChain model factory.

The desktop app owns model configuration. This module reads its saved JSON
configuration (or explicit environment-variable overrides) and creates a model
object only; it never makes a network request during initialization.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass
from pathlib import Path


DEFAULT_BASE_URL = "https://api.z.ai/api/paas/v4"
DEFAULT_MODEL_NAME = "glm-5.3"
DEFAULT_REASONING_EFFORT = "max"


@dataclass(frozen=True)
class ModelConfig:
    provider: str
    api_key: str
    base_url: str
    model_name: str
    reasoning_effort: str


def _config_paths() -> list[Path]:
    """Return user config first, then the repository default config."""
    paths: list[Path] = []
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        paths.append(Path(local_app_data) / "AquaPet" / "config" / "model_setting.json")
    paths.append(Path(__file__).resolve().parents[2] / "config" / "model_setting.json")
    return paths


def _read_saved_config() -> dict[str, str]:
    for path in _config_paths():
        if path.is_file():
            try:
                data = json.loads(path.read_text(encoding="utf-8"))
            except json.JSONDecodeError as error:
                raise RuntimeError(f"Invalid AquaPet model configuration: {path}") from error
            return {key: str(value) for key, value in data.items() if value is not None}
    return {}


def load_model_config() -> ModelConfig:
    """Load GLM defaults, desktop settings, then environment overrides."""
    saved = _read_saved_config()
    return ModelConfig(
        provider=os.getenv("AQUAPET_LLM_PROVIDER", saved.get("provider", "glm")),
        api_key=os.getenv("AQUAPET_LLM_TOKEN", saved.get("token", "")),
        base_url=os.getenv("AQUAPET_LLM_BASE_URL", saved.get("baseUrl", DEFAULT_BASE_URL)),
        model_name=os.getenv("AQUAPET_LLM_MODEL", saved.get("modelName", DEFAULT_MODEL_NAME)),
        reasoning_effort=os.getenv(
            "AQUAPET_LLM_REASONING_EFFORT",
            saved.get("reasoningEffort", DEFAULT_REASONING_EFFORT),
        ),
    )


def create_chat_model():
    """Create the LangChain chat model used by later LangGraph nodes."""
    config = load_model_config()
    if not config.api_key:
        raise RuntimeError(
            "No model token is configured. Set it in AquaPet's 模型 page or "
            "provide AQUAPET_LLM_TOKEN."
        )

    if config.provider.lower() != "glm":
        raise ValueError(f"Unsupported AquaPet model provider: {config.provider}")

    from langchain_openai import ChatOpenAI

    return ChatOpenAI(
        model=config.model_name,
        api_key=config.api_key,
        base_url=config.base_url.rstrip("/"),
        reasoning_effort=config.reasoning_effort,
        # AquaPet's current /chat endpoint returns one complete response.
        # Streaming will be enabled only when the desktop receives SSE chunks.
        streaming=False,
    )
