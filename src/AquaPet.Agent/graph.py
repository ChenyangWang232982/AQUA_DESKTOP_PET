from __future__ import annotations 

from typing import Annotated, Any, TypedDict

from langgraph.graph import END, START, StateGraph
from langgraph.graph.message import add_messages
from langgraph.prebuilt import ToolNode, tools_condition

from components.sandbox.tool import (
    run_command_in_sandbox,
    run_python_in_sandbox,
)
from model import create_chat_model

from langchain_core.messages import SystemMessage
from components.skills.loader import discover_skills
from components.skills.tool import load_skill_instructions

class AgentState(TypedDict):
    """一次会话中各个节点共享的数据。"""

    messages: Annotated[list, add_messages]
    reply: str

def _content_as_text(content: Any) -> str:
    """将模型的回复内容转换为可显示的文字。"""

    if isinstance(content, str):
        return content

    if isinstance(content, list):
        return "".join(
            item.get("text", "") if isinstance(item, dict) else str(item)
            for item in content
        )

    return str(content)

def build_agent_graph(chat_model: Any | None = None):
    """创建支持沙盒工具调用的会话图。"""

    model = chat_model or create_chat_model()

    tools = [
        run_command_in_sandbox,
        run_python_in_sandbox,
        load_skill_instructions,
    ]

    # 告诉模型有哪些工具可以调用。
    model_with_tools = model.bind_tools(tools)

    async def call_model(state: AgentState) -> dict:
        """调用模型，把回复加入消息列表。"""

        skills = discover_skills()

        if skills:
            skill_lines = [
                f"- {skill.name}: {skill.description}" for skill in skills
            ]
            skill_catalog = "\n".join(skill_lines)
        else:
            skill_catalog = "没有可用的技能。"

        skill_message = SystemMessage(
            content=(
                "以下是当前可用的 Skill：\n"
                f"{skill_catalog}\n\n"
                "当用户请求与某个 Skill 相符时，"
                "先调用 load_skill_instructions 加载完整说明，"
                "遵守技能指定的检查和执行顺序；根据任务规划需要的步骤，"
                "按需读取技能的依赖清单及引用的文件，检查沙盒环境，"
                "使用沙盒命令工具安装缺少的依赖并验证结果。"
                "安装前先检查已有依赖，避免不必要地重复安装。"
                "依据工具输出和退出码判断结果，不要声称未验证的安装成功。"
                "需要较长时间的安装时，可为命令指定更长的 timeout_seconds。"
            )
        )

        response = await model_with_tools.ainvoke(
            [skill_message, *state["messages"]]
        )

        return {
            "messages": [response],
        }

    def finish_reply(state: AgentState) -> dict:
        """从最后一条模型消息中提取最终回复。"""

        last_message = state["messages"][-1]

        return {
            "reply": _content_as_text(last_message.content),
        }

    graph = StateGraph(AgentState)

    graph.add_node("model", call_model)
    graph.add_node("tools", ToolNode(tools))
    graph.add_node("finish", finish_reply)

    graph.add_edge(START, "model")

    # 如果模型请求工具，就去 tools；否则直接生成最终回复。
    graph.add_conditional_edges(
        "model",
        tools_condition,
        {
            "tools": "tools",
            END: "finish",
        },
    )

    # 工具执行完后，把结果交回模型。
    graph.add_edge("tools", "model")
    graph.add_edge("finish", END)

    return graph.compile()
