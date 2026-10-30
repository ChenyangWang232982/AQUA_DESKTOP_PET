"""Local HTTP entry point for the AquaPet LangGraph agent."""

from __future__ import annotations

from contextlib import asynccontextmanager
import base64
import binascii
from pathlib import Path

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from graph import build_agent_graph
from model import create_chat_model, load_model_config
from storage.sqlite import MessageStore

from components.context.context import build_context
from components.permissions.router import decide_permissions
from components.sandbox.session import sandbox_task


store = MessageStore()
agent = None
agent_config = None


@asynccontextmanager
async def lifespan(_: FastAPI):
    """Prepare local SQLite persistence before serving requests."""
    load_dotenv(Path(__file__).resolve().parents[2] / ".env", override=False)
    store.initialize()
    yield


app = FastAPI(title="AquaPet Agent", lifespan=lifespan)


class ImageAttachment(BaseModel):
    name: str = Field(min_length=1, max_length=255)
    content_type: str
    data: str = Field(max_length=11_184_812)


class ChatRequest(BaseModel):
    content: str = Field(default="", max_length=20_000)
    attachments: list[ImageAttachment] = Field(default_factory=list, max_length=5)


class ChatResponse(BaseModel):
    content: str


@app.get("/health")
def health() -> dict[str, bool]:
    """Signal that the agent and its SQLite startup check succeeded."""
    return {"ready": True}


@app.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest) -> ChatResponse:
    """保存用户消息，运行 Agent，再保存模型回复。"""

    global agent, agent_config

    if not request.content.strip() and not request.attachments:
        raise HTTPException(status_code=400, detail="请输入消息或添加图片。")

    image_blocks = []
    for attachment in request.attachments:
        if attachment.content_type != "image/png":
            raise HTTPException(status_code=400, detail="图片格式不受支持。")
        try:
            image_bytes = base64.b64decode(attachment.data, validate=True)
        except (ValueError, binascii.Error) as error:
            raise HTTPException(status_code=400, detail="图片数据无效。") from error
        if not image_bytes.startswith(b"\x89PNG\r\n\x1a\n") or len(image_bytes) > 8 * 1024 * 1024:
            raise HTTPException(status_code=400, detail="图片数据无效或超过 8 MB。")
        image_blocks.append({"type": "image_url", "image_url": {"url": f"data:image/png;base64,{attachment.data}"}})

    saved_content = request.content
    if request.attachments:
        saved_content += ("\n" if saved_content else "") + "\n".join(
            f"[图片附件：{attachment.name}]" for attachment in request.attachments
        )
    store.save("user", saved_content)

    try:
        current_config = load_model_config()
        if agent is None or current_config != agent_config:
            agent = create_chat_model(current_config)
            agent_config = current_config

        # 保留原来的基础提示词和历史消息拼接逻辑。
        history = store.load_messages()
        messages = build_context(history)
        current_model = agent
        decision = await decide_permissions(
            current_model, history, [item.name for item in request.attachments],
        )
        request_agent = build_agent_graph(current_model, decision.web_search, decision.sandbox)
        if image_blocks:
            messages[-1] = {
                "role": "user",
                "content": [{"type": "text", "text": request.content or "请查看这张图片。"}, *image_blocks],
            }

        # 一次用户请求使用一个沙盒会话。
        async with sandbox_task():
            result = await request_agent.ainvoke(
                {"messages": messages},
                config={"recursion_limit": 20},
            )

        reply = result["reply"]
        if not decision.succeeded:
            reply += "\n\n（权限判断未完成，本次未启用联网搜索或沙盒工具。）"

    except Exception as error:
        raise HTTPException(status_code=500, detail=str(error)) from error

    store.save("model", reply)

    return ChatResponse(content=reply)
