"""Local HTTP entry point for the AquaPet LangGraph agent."""

from __future__ import annotations

from contextlib import asynccontextmanager
from pathlib import Path

from dotenv import load_dotenv
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel, Field

from graph import build_agent_graph
from storage.sqlite import MessageStore

from components.context.context import build_context
from components.sandbox.session import sandbox_task


store = MessageStore()
agent = None


@asynccontextmanager
async def lifespan(_: FastAPI):
    """Prepare local SQLite persistence before serving requests."""
    load_dotenv(Path(__file__).resolve().parents[2] / ".env", override=False)
    store.initialize()
    yield


app = FastAPI(title="AquaPet Agent", lifespan=lifespan)


class ChatRequest(BaseModel):
    content: str = Field(min_length=1, max_length=20_000)


class ChatResponse(BaseModel):
    content: str


@app.get("/health")
def health() -> dict[str, bool]:
    """Signal that the agent and its SQLite startup check succeeded."""
    return {"ready": True}


@app.post("/chat", response_model=ChatResponse)
async def chat(request: ChatRequest) -> ChatResponse:
    """保存用户消息，运行 Agent，再保存模型回复。"""

    global agent

    store.save("user", request.content)

    try:
        if agent is None:
            agent = build_agent_graph()

        # 保留原来的基础提示词和历史消息拼接逻辑。
        messages = build_context(store.load_messages())

        # 一次用户请求使用一个沙盒会话。
        async with sandbox_task():
            result = await agent.ainvoke(
                {"messages": messages},
                config={"recursion_limit": 20},
            )

        reply = result["reply"]

    except Exception as error:
        raise HTTPException(status_code=500, detail=str(error)) from error

    store.save("model", reply)

    return ChatResponse(content=reply)
