using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AquaPet.Desktop.Infrastructure.Configuration;

namespace AquaPet.Desktop.Pet;

/// <summary>读取启用状态，并管理独立的 WinUI Composition 桌宠进程。</summary>
public sealed class PetRunner
{
    private const string DefaultAnimationRelativePath = "Assets\\Pet\\vup\\Default\\Nomal\\1";
    private readonly string _dialogPipeName = $"AquaPet.Dialog.{Environment.ProcessId}";
    private Process? _petProcess;

    public async Task StartIfEnabledAsync()
    {
        var petEnabled = await JsonConfigStore.CheckAsync<bool>("pet_setting", "petEnabled");
        if (petEnabled)
        {
            string behaviorMode;
            try
            {
                behaviorMode = await JsonConfigStore.CheckAsync<string>("pet_setting", "behaviorMode");
            }
            catch (KeyNotFoundException)
            {
                behaviorMode = "default";
            }
            Start(behaviorMode);
        }
    }

    public void Start(string behaviorMode = "default")
    {
        if (_petProcess is { HasExited: false })
        {
            return;
        }

        _petProcess?.Dispose();
        _petProcess = null;
        var animationDirectory = Path.Combine(AppContext.BaseDirectory, DefaultAnimationRelativePath);
        if (!Directory.Exists(animationDirectory))
        {
            throw new DirectoryNotFoundException($"桌宠动画目录不存在：{animationDirectory}");
        }

        if (!Directory.EnumerateFiles(animationDirectory, "*.png").Any())
        {
            throw new FileNotFoundException($"桌宠动画目录内没有 PNG：{animationDirectory}");
        }

        var hostPath = Path.Combine(AppContext.BaseDirectory, "PetHost", "AquaPet.PetHost.exe");
        if (!File.Exists(hostPath))
        {
            throw new FileNotFoundException("桌宠 Composition 宿主未构建。", hostPath);
        }

        var startInfo = new ProcessStartInfo(hostPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(hostPath)!
        };
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(animationDirectory);
        startInfo.ArgumentList.Add(behaviorMode);
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Assets", "Dialog", "dialog.png"));
        startInfo.ArgumentList.Add(_dialogPipeName);
        _petProcess = Process.Start(startInfo)
            ?? throw new InvalidOperationException("桌宠 Composition 宿主未能启动。");
        _petProcess.WaitForInputIdle(5000);
        if (_petProcess.HasExited)
        {
            throw new InvalidOperationException(
                $"桌宠 Composition 宿主启动后立即退出（代码 {_petProcess.ExitCode}）。");
        }
    }

    public void Stop()
    {
        if (_petProcess is null) return;
        try
        {
            if (!_petProcess.HasExited)
            {
                _petProcess.CloseMainWindow();
                // The shutdown animation must finish before a forced stop.
                if (!_petProcess.WaitForExit(6000))
                {
                    _petProcess.Kill();
                    _petProcess.WaitForExit();
                }
            }
        }
        finally
        {
            _petProcess.Dispose();
            _petProcess = null;
        }
    }

    /// <summary>Sends an agent reply to the currently running desktop pet.</summary>
    public async Task ShowDialogAsync(string content)
    {
        if (_petProcess is not { HasExited: false } || string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(
                ".", _dialogPipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
            await client.ConnectAsync(timeout.Token);
            await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false);
            await writer.WriteAsync(content);
        }
        catch (Exception)
        {
            // The overlay is optional; a stopped/restarting pet must not break chat.
        }
    }

    /// <summary>将刚保存的桌宠启用状态立即应用到运行中的桌宠。</summary>
    public void ApplySettings(bool petEnabled, string behaviorMode)
    {
        if (!petEnabled)
        {
            Stop();
            return;
        }

        Stop();
        Start(behaviorMode);
    }
}
