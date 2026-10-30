using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    private readonly ObservableCollection<ImageAttachment> _attachments = new();

    private readonly PetSettingsPanel _petSettingsPanel = new();
    private readonly ModelSettingsPanel _modelSettingsPanel = new();
    private bool _isSending;

    public MainWindow()
    {
        InitializeComponent();
        MessagesList.ItemsSource = _messages;
        PendingAttachments.ItemsSource = _attachments;
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
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
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
        if (string.IsNullOrEmpty(message) && _attachments.Count == 0)
        {
            return;
        }

        var attachments = _attachments.ToArray();
        var userMessage = new ChatMessage(message, true, attachments);
        _messages.Add(userMessage);
        MessageInput.Clear();
        _attachments.Clear();
        ScrollMessagesToEnd();

        _isSending = true;
        MessageInput.IsEnabled = false;
        try
        {
            using var response = await AgentClient.PostAsJsonAsync("chat", new
            {
                content = message,
                attachments = attachments.Select(item => new { name = item.Name, content_type = "image/png", data = Convert.ToBase64String(item.Data) })
            });
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
        var agentMessage = new ChatMessage(content, false, []);
        _messages.Add(agentMessage);
        ScrollMessagesToEnd();
        if (showOnPet)
        {
            _ = ((App)Application.Current).ShowPetDialogAsync(content);
        }
    }

    private void ScrollMessagesToEnd()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            FindScrollViewer(MessagesList)?.ScrollToEnd();
        }));
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer viewer) return viewer;
            var descendant = FindScrollViewer(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private void MessageInput_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void MessageInput_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths) AddImageFile(path);
        e.Handled = true;
    }

    private void MessageInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var paths = Clipboard.GetFileDropList();
                e.Handled = true;
                foreach (string? path in paths)
                    if (!string.IsNullOrEmpty(path)) AddImageFile(path);
            }
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image)
            {
                e.Handled = true;
                AddImage(image, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            }
            else if (Clipboard.GetData("PNG") is Stream pngStream)
            {
                e.Handled = true;
                var frame = BitmapFrame.Create(pngStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                AddImage(frame, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            }
        }
        catch (Exception error)
        {
            e.Handled = true;
            FeedbackPopup.Show("图片添加失败", error.Message, false);
        }
    }

    private void MessageInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        try
        {
            if (e.SourceDataObject.GetDataPresent(DataFormats.FileDrop) &&
                e.SourceDataObject.GetData(DataFormats.FileDrop) is string[] paths)
            {
                e.CancelCommand();
                foreach (var path in paths) AddImageFile(path);
            }
            else if (e.SourceDataObject.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is BitmapSource image)
            {
                e.CancelCommand();
                AddImage(image, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
            }
        }
        catch (Exception error)
        {
            FeedbackPopup.Show("图片添加失败", error.Message, false);
        }
    }

    private void AddImageFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            AddImage(image, Path.GetFileName(path));
        }
        catch (Exception error)
        {
            FeedbackPopup.Show("图片添加失败", error.Message, false);
        }
    }

    private void AddImage(BitmapSource image, string name)
    {
        if (_attachments.Count >= 5) throw new InvalidOperationException("一次最多添加 5 张图片。");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > 8 * 1024 * 1024) throw new InvalidOperationException("图片转换后不能超过 8 MB。");
        var bytes = stream.ToArray();
        using var previewStream = new MemoryStream(bytes);
        var preview = new BitmapImage();
        preview.BeginInit();
        preview.CacheOption = BitmapCacheOption.OnLoad;
        preview.DecodePixelWidth = 300;
        preview.StreamSource = previewStream;
        preview.EndInit();
        preview.Freeze();
        _attachments.Add(new ImageAttachment(name, bytes, preview));
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ImageAttachment attachment }) _attachments.Remove(attachment);
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
    private sealed record ImageAttachment(string Name, byte[] Data, ImageSource Preview)
    {
        public string DisplayName => $"📎 {Name}";
    }
    private sealed record ChatMessage(string Content, bool IsUser, IReadOnlyList<ImageAttachment> Attachments)
    {
        public bool HasText => !string.IsNullOrWhiteSpace(Content);
        public bool HasAttachments => Attachments.Count > 0;
    }
}
