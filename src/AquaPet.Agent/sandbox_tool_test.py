import asyncio

from components.sandbox.tool import run_python_in_sandbox


async def main() -> None:
    result = await run_python_in_sandbox.ainvoke(
        {
            "script": 'print("沙盒工具调用成功")',
        }
    )

    print("工具返回：")
    print(result)


if __name__ == "__main__":
    asyncio.run(main())