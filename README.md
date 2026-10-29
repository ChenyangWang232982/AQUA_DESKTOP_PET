# AquaPet

当前阶段：WPF 主面板原型。

## 运行

安装 .NET 8 SDK 后，在项目根目录执行：

```powershell
dotnet run --project .\src\AquaPet.Desktop
```

主面板包含左侧导航、右侧会话记录和消息发送交互。当前消息仅保存在运行内存中，后续再接入 LangGraph Agent。

