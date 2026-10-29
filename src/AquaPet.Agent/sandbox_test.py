import asyncio

from components.sandbox.executor import execute_python


async def main() -> None:
    script = """
import time
from urllib.request import urlopen

print("尝试访问 example.com")

try:
    with urlopen("https://example.com", timeout=5) as response:
        print("访问成功，状态码：", response.status)
except Exception as error:
    print("访问失败：", type(error).__name__, str(error))
"""

    result = await execute_python(script)

    print("标准输出：")
    print(result.stdout)

    print("错误输出：")
    print(result.stderr)

    print("退出码：", result.exit_code)
    print("执行错误：", result.error)


if __name__ == "__main__":
    asyncio.run(main())