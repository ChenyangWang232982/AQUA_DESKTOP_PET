using System.Windows;
using System.Windows.Controls;
using AquaPet.Desktop.Components.UI.Form;
using AquaPet.Desktop.Infrastructure.Configuration;

namespace AquaPet.Desktop.Components.Setting.Model;

public partial class ModelSettingsPanel : UserControl
{
    private ModelConfig _saved = ModelConfig.Default;
    public event EventHandler<ModelSettingsFeedbackEventArgs>? FeedbackRequested;

    public ModelSettingsPanel()
    {
        InitializeComponent();
        ProviderSelect.Options = [new FormOption("glm", "GLM")];
        ReasoningEffortSelect.Options =
        [
            new FormOption("low", "LOW"),
            new FormOption("high", "HIGH"),
            new FormOption("max", "MAX")
        ];
        Apply(_saved);
    }

    private async void ModelSettingsPanel_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var configured = new ModelConfig(
                await JsonConfigStore.CheckAsync<string>("model_setting", "provider"),
                await JsonConfigStore.CheckAsync<string>("model_setting", "token"),
                await JsonConfigStore.CheckAsync<string>("model_setting", "baseUrl"),
                await JsonConfigStore.CheckAsync<string>("model_setting", "modelName"),
                await ReadSettingOrDefaultAsync("reasoningEffort", ModelConfig.Default.ReasoningEffort));
            _saved = configured with
            {
                BaseUrl = configured.BaseUrl == "https://open.bigmodel.cn/api/paas/v4"
                    ? ModelConfig.Default.BaseUrl : configured.BaseUrl,
            };
            Apply(_saved);
        }
        catch (Exception)
        {
            Apply(_saved);
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var current = Read();
        try
        {
            await JsonConfigStore.SaveAsync("model_setting", "provider", current.Provider);
            await JsonConfigStore.SaveAsync("model_setting", "token", current.Token);
            await JsonConfigStore.SaveAsync("model_setting", "baseUrl", current.BaseUrl);
            await JsonConfigStore.SaveAsync("model_setting", "modelName", current.ModelName);
            await JsonConfigStore.SaveAsync("model_setting", "reasoningEffort", current.ReasoningEffort);
            _saved = current;
            UpdateSaveAvailability();
            FeedbackRequested?.Invoke(this, new ModelSettingsFeedbackEventArgs("已保存", null, true));
        }
        catch (Exception exception)
        {
            FeedbackRequested?.Invoke(this, new ModelSettingsFeedbackEventArgs("保存失败", exception.Message, false));
        }
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSaveAvailability();
    private void TextInput_Changed(object sender, TextChangedEventArgs e) => UpdateSaveAvailability();
    private void TokenInput_Changed(object sender, RoutedEventArgs e) => UpdateSaveAvailability();
    private async Task<string> ReadSettingOrDefaultAsync(string field, string defaultValue)
    {
        try
        {
            return await JsonConfigStore.CheckAsync<string>("model_setting", field);
        }
        catch (KeyNotFoundException)
        {
            return defaultValue;
        }
    }

    private ModelConfig Read() => new(
        ProviderSelect.SelectedValue,
        TokenInput.Password,
        BaseUrlInput.Text.Trim(),
        ModelNameInput.Text.Trim(),
        ReasoningEffortSelect.SelectedValue);

    private void Apply(ModelConfig model)
    {
        ProviderSelect.SelectedValue = model.Provider;
        TokenInput.Password = model.Token;
        BaseUrlInput.Text = model.BaseUrl;
        ModelNameInput.Text = model.ModelName;
        ReasoningEffortSelect.SelectedValue = model.ReasoningEffort;
        UpdateSaveAvailability();
    }
    private void UpdateSaveAvailability() { if (SaveButton is not null) SaveButton.IsEnabled = Read() != _saved; }
}

public sealed record ModelConfig(
    string Provider,
    string Token,
    string BaseUrl,
    string ModelName,
    string ReasoningEffort)
{
    public static ModelConfig Default { get; } = new("glm", "", "https://api.z.ai/api/paas/v4", "glm-5.3", "max");
}
public sealed class ModelSettingsFeedbackEventArgs(string title, string? detail, bool state) : EventArgs
{
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public bool State { get; } = state;
}
