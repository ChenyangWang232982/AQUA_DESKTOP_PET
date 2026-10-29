# 桌宠 Composition 宿主

AquaPet.Desktop 负责设置窗口、配置及进程生命周期；AquaPet.PetHost 是独立的 WinUI 3 透明窗口，接收主程序 PID 和动画目录作为启动参数。

宿主通过 ElementCompositionPreview 把 SpriteVisual 接入 WinUI 视觉树。PNG 帧启动时全部加载成 Composition surface，随后按文件名中的毫秒数切换画笔；当前默认目录为 Assets/Pet/vup/Default/Nomal/1。纵向拖动和松手后的重力下落都改变 Visual 的位置；窗口保持固定高度，下落曲线一次性提交给 Windows Composition。

透明背景由 WinUIEx 的 TransparentTintBackdrop 提供。窗口区域仅覆盖桌宠所在位置；下落时临时扩大，落地后恢复，使其余透明区域可以点击桌面。关闭主程序或关闭桌宠设置时，主程序会结束宿主进程；宿主也会检查主进程是否仍存在。

从项目根目录运行：

    dotnet build src/AquaPet.Desktop/AquaPet.Desktop.csproj

构建会自动构建宿主并复制到主程序输出目录的 PetHost 子目录。运行依赖 .NET 8 Desktop Runtime 和 Windows App SDK 2.5.1 Runtime。
