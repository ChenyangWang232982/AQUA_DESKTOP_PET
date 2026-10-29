using System.Windows;
using System.Windows.Controls;
using AquaPet.Desktop.Components.UI.Form;
using AquaPet.Desktop.Infrastructure.Configuration;

namespace AquaPet.Desktop.Components.Setting.Pet;

/// <summary>桌宠设置主体；当前只承载启用状态。</summary>
public partial class PetSettingsPanel : UserControl
{
    private bool _savedIsEnabled = true;
    private string _savedBehaviorMode = "default";

    public event EventHandler<PetSettingsFeedbackEventArgs>? FeedbackRequested;
    public event EventHandler<PetSettingsSavedEventArgs>? SettingsSaved;

    public PetSettingsPanel()
    {
        InitializeComponent();
        BehaviorModeSelect.Options =
        [
            new FormOption("default", "默认（站立）"),
            new FormOption("mouseFollow", "鼠标跟随")
        ];
        BehaviorModeSelect.SelectedValue = _savedBehaviorMode;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var enabled = EnablePetToggle.IsChecked;
        var behaviorMode = BehaviorModeSelect.SelectedValue;
        if (enabled == _savedIsEnabled && behaviorMode == _savedBehaviorMode)
        {
            return;
        }

        try
        {
            await JsonConfigStore.SaveAsync("pet_setting", "petEnabled", enabled);
            await JsonConfigStore.SaveAsync("pet_setting", "behaviorMode", behaviorMode);
            _savedIsEnabled = enabled;
            _savedBehaviorMode = behaviorMode;
            UpdateSaveAvailability();
            SettingsSaved?.Invoke(this, new PetSettingsSavedEventArgs(enabled, behaviorMode));
            FeedbackRequested?.Invoke(this, new PetSettingsFeedbackEventArgs("已保存", null, true));
        }
        catch (Exception exception)
        {
            FeedbackRequested?.Invoke(this, new PetSettingsFeedbackEventArgs("保存失败", exception.Message, false));
        }
    }

    private void PetSettingsPanel_Loaded(object sender, RoutedEventArgs e) => UpdateSaveAvailability();

    private void EnablePetToggle_ValueChanged(object sender, RoutedEventArgs e) => UpdateSaveAvailability();

    private void BehaviorModeSelect_ValueChanged(object sender, SelectionChangedEventArgs e) => UpdateSaveAvailability();

    private void UpdateSaveAvailability()
    {
        if (SaveButton is null)
        {
            return;
        }

        var behaviorMode = BehaviorModeSelect.SelectedValue;
        SaveButton.IsEnabled = EnablePetToggle.IsChecked != _savedIsEnabled ||
            behaviorMode != _savedBehaviorMode;
    }
}

public sealed class PetSettingsFeedbackEventArgs(string title, string? detail, bool state) : EventArgs
{
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public bool State { get; } = state;
}

public sealed class PetSettingsSavedEventArgs(bool petEnabled, string behaviorMode) : EventArgs
{
    public bool PetEnabled { get; } = petEnabled;
    public string BehaviorMode { get; } = behaviorMode;
}
