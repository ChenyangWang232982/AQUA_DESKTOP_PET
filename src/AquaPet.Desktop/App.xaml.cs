using System.Windows;
using AquaPet.Desktop.Pet;

namespace AquaPet.Desktop;

public partial class App : Application
{
    private readonly PetRunner _petRunner = new();

    private async void App_Startup(object sender, StartupEventArgs e)
    {
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Closed += (_, _) =>
        {
            _petRunner.Stop();
            Shutdown();
        };
        mainWindow.Show();

        try
        {
            await _petRunner.StartIfEnabledAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(mainWindow, exception.Message, "桌宠启动失败",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void ApplyPetSettings(bool petEnabled, string behaviorMode) =>
        _petRunner.ApplySettings(petEnabled, behaviorMode);

    public Task ShowPetDialogAsync(string content) => _petRunner.ShowDialogAsync(content);
}
