using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AquaPet.Desktop.Components.UI;

/// <summary>带淡入淡出动画的居中反馈弹窗。</summary>
public partial class Feedback : UserControl
{
    private readonly DispatcherTimer _dismissTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Feedback), new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(Feedback), new PropertyMetadata(null, OnContentChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(bool), typeof(Feedback), new PropertyMetadata(true, OnContentChanged));

    public Feedback()
    {
        InitializeComponent();
        _dismissTimer.Tick += (_, _) => Hide();
        UpdateVisuals();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Detail
    {
        get => (string?)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>true 为成功（天蓝），false 为失败（灰色）。</summary>
    public bool State
    {
        get => (bool)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public void Show(string title, string? detail, bool state)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Feedback title is required.", nameof(title));
        }

        Title = title;
        Detail = detail;
        State = state;
        _dismissTimer.Stop();
        TitleTyping.Stop();
        DetailTyping.Stop();
        UpdateVisuals();

        Visibility = Visibility.Visible;
        BeginAnimation(OpacityProperty, null);
        PopupScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Opacity = 0;
        PopupScale.ScaleX = 0.92;
        PopupScale.ScaleY = 0.92;

        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(160));
        fadeIn.Completed += (_, _) =>
        {
            StartTextTyping();
            _dismissTimer.Start();
        };
        BeginAnimation(OpacityProperty, fadeIn);
        PopupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        PopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
    }

    public void Hide()
    {
        _dismissTimer.Stop();
        TitleTyping.Stop();
        DetailTyping.Stop();
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
        fadeOut.Completed += (_, _) => Visibility = Visibility.Collapsed;
        BeginAnimation(OpacityProperty, fadeOut);
        PopupScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(140)));
        PopupScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(140)));
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
        => ((Feedback)sender).UpdateVisuals();

    private void UpdateVisuals()
    {
        if (TitleTyping is null)
        {
            return;
        }

        TitleTyping.Text = Title;
        DetailTyping.Text = Detail ?? string.Empty;
        DetailTyping.Visibility = string.IsNullOrWhiteSpace(Detail) ? Visibility.Collapsed : Visibility.Visible;
        PopupBorder.Background = State ? new SolidColorBrush(Color.FromRgb(142, 215, 248)) : new SolidColorBrush(Color.FromRgb(190, 199, 207));
        PopupBorder.BorderBrush = State ? Brushes.White : new SolidColorBrush(Color.FromRgb(104, 118, 130));
        TitleTyping.Foreground = new SolidColorBrush(Color.FromRgb(18, 59, 106));
        DetailTyping.Foreground = new SolidColorBrush(Color.FromRgb(41, 71, 91));
    }

    private void StartTextTyping()
    {
        TitleTyping.Start();
        if (!string.IsNullOrWhiteSpace(Detail))
        {
            DetailTyping.Start();
        }
    }
}
