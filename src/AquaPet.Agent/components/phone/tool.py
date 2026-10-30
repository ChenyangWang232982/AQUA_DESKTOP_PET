"""An independent model request with the provider's web search enabled."""

import logging
from typing import Any

from langchain_core.messages import HumanMessage, SystemMessage
from langchain_core.tools import tool

logger = logging.getLogger(__name__)


def create_phone_tool(model: Any):
    """Bind the search request to the model configured for this conversation."""

    @tool
    async def phone(question: str) -> str:
        """用手机联网查询。question 是从用户角度提出的完整查询问题，包含必要背景。适合查实时信息和网页内容。"""
        if not question.strip() or len(question) > 4000:
            return "手机查询失败：问题为空或超过 4000 个字符。"
        try:
            search_model = model.bind_tools([{
                "type": "web_search",
                "web_search": {"enable": True, "search_engine": "search-prime", "search_result": True},
            }])
            response = await search_model.ainvoke([
                SystemMessage(content="你是手机联网查询助手。根据搜索结果回答问题；如无法获取有效结果，明确说明，不要编造。"),
                HumanMessage(content=question),
            ])
            count = response.response_metadata.get("zai_search_result_count")
            if not count:
                return "手机查询未取得可验证的搜索结果，请告知用户查询未成功。"
            if not isinstance(response.content, str) or not response.content.strip():
                return "手机查询已返回搜索结果，但没有生成可用的回答。"
            return response.content
        except Exception:
            logger.exception("Phone search request failed")
            return "手机查询失败，搜索服务暂时不可用。请如实告知用户。"

    return phone
