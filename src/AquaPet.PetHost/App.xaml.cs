using Microsoft.UI.Xaml;

namespace AquaPet.PetHost;

public partial class App : Application
{
    private PetWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) => LogError(args.Exception);
    }

    internal static void LogError(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AquaPet", "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "pet-host-error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Error logging must not cause a second failure.
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length < 3 || !int.TryParse(commandLine[1], out var parentPid))
        {
            Exit();
            return;
        }

        var behaviorMode = commandLine.Length >= 4 ? commandLine[3] : "default";
        var dialogAssetPath = commandLine.Length >= 5 ? commandLine[4] : string.Empty;
        var dialogPipeName = commandLine.Length >= 6 ? commandLine[5] : string.Empty;
        _window = new PetWindow(parentPid, commandLine[2], behaviorMode, dialogAssetPath, dialogPipeName);
        _window.Activate();
    }
}
