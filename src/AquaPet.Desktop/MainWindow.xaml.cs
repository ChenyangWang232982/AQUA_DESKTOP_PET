using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AquaPet.Desktop.Components.Setting.Pet;
using AquaPet.Desktop.Components.Setting.Model;

namespace AquaPet.Desktop;

public partial class MainWindow : Window
{
    private static readonly HttpClient AgentClient = new()
    {
        BaseAddress = new Uri("http://127.0.0.1:8765/"),
        Timeout = TimeSpan.FromSeconds(90)
    };

    private static readonly IReadOnlyDictionary<string, string> PanelDescriptions = new Dictionary<string, string>
    {
        ["会话"] = "与 AquaPet 交流 · OFFLINE",
        ["记忆"] = "查看与管理长期记忆 · OFFLINE",
        ["桌宠"] = "Configuration",
        ["模型"] = "配置 AquaPet 的基础模型与连接信息",
        ["设置"] = "配置 AquaPet 的本地偏好 · OFFLINE"
    };

    private readonly ObservableCollection<ChatMessage> _messages = new();

    private readonly PetSettingsPanel _petSettingsPanel = new();
    private readonly ModelSettingsPanel _modelSettingsPanel = new();
    private bool _isSending;

    public MainWindow()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        _petSettingsPanel.FeedbackRequested += PetSettingsPanel_FeedbackRequested;
        _petSettingsPanel.SettingsSaved += PetSettingsPanel_SettingsSaved;
        _modelSettingsPanel.FeedbackRequested += ModelSettingsPanel_FeedbackRequested;
    }

    private void NavButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Content: string title })
        {
            PanelTitle.Text = title;
            PanelDescription.Text = PanelDescriptions[title];
            MainContent.Content = title switch
            {
                "桌宠" => _petSettingsPanel,
                "模型" => _modelSettingsPanel,
                _ => ConversationContent
            };
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private void PetSettingsPanel_FeedbackRequested(object? sender, PetSettingsFeedbackEventArgs e)
    {
        FeedbackPopup.Show(e.Title, e.Detail, e.State);
    }

    private void PetSettingsPanel_SettingsSaved(object? sender, PetSettingsSavedEventArgs e)
    {
        ((App)Application.Current).ApplyPetSettings(e.PetEnabled, e.BehaviorMode);
    }

    private void ModelSettingsPanel_FeedbackRequested(object? sender, ModelSettingsFeedbackEventArgs e) =>
        FeedbackPopup.Show(e.Title, e.Detail, e.State);

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (FeedbackPopup.Visibility == Visibility.Visible && !FeedbackPopup.IsMouseOver)
        {
            FeedbackPopup.Hide();
        }
    }

    private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SendMessageAsync();
            e.Handled = true;
        }
    }

    private async Task SendMessageAsync()
    {
        if (_isSending)
        {
            return;
        }

        var message = MessageInput.Text.Trim();
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var userMessage = new ChatMessage(message, true);
        _messages.Add(userMessage);
        MessageInput.Clear();
        MessagesList.ScrollIntoView(userMessage);

        _isSending = true;
        MessageInput.IsEnabled = false;
        try
        {
            using var response = await AgentClient.PostAsJsonAsync("chat", new { content = message });
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var agentResponse = JsonSerializer.Deserialize<AgentResponse>(
                    responseBody,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrWhiteSpace(agentResponse?.Content))
                {
                    AddAgentMessage(agentResponse.Content, showOnPet: true);
                }
                else
                {
                    AddAgentMessage("Agent error: The response did not contain content.");
                }
            }
            else
            {
                AddAgentMessage($"Agent error: {ReadErrorMessage(responseBody, response.StatusCode)}");
            }
        }
        catch (Exception error)
        {
            AddAgentMessage($"Agent error: {error.Message}");
        }
        finally
        {
            _isSending = false;
            MessageInput.IsEnabled = true;
            MessageInput.Focus();
        }
    }

    private void AddAgentMessage(string content, bool showOnPet = false)
    {
        var agentMessage = new ChatMessage(content, false);
        _messages.Add(agentMessage);
        MessagesList.ScrollIntoView(agentMessage);
        if (showOnPet)
        {
            _ = ((App)Application.Current).ShowPetDialogAsync(content);
        }
    }

    private static string ReadErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.GetString() ?? statusCode.ToString();
            }
        }
        catch (JsonException)
        {
            // The server can return non-JSON error content.
        }

        return string.IsNullOrWhiteSpace(responseBody) ? statusCode.ToString() : responseBody;
    }

    private sealed record AgentResponse(string Content);
    private sealed record ChatMessage(string Content, bool IsUser);
}
