using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AquaPet.Desktop.Components.Style;

/// <summary>逐字显示文字；文字完成时自动停止并隐藏末尾光标。</summary>
public partial class TypingText : UserControl
{
    private readonly DispatcherTimer _typingTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private int _visibleLength;
    private bool _cursorVisible;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(TypingText),
        new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty AutoStartProperty = DependencyProperty.Register(
        nameof(AutoStart),
        typeof(bool),
        typeof(TypingText),
        new PropertyMetadata(true));

    public TypingText()
    {
        InitializeComponent();
        _typingTimer.Tick += TypingTimer_Tick;
        _cursorTimer.Tick += CursorTimer_Tick;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>为 false 时由调用方以 Start() 控制文字何时开始显示。</summary>
    public bool AutoStart
    {
        get => (bool)GetValue(AutoStartProperty);
        set => SetValue(AutoStartProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
    {
        var typingText = (TypingText)sender;
        if (typingText.AutoStart)
        {
            typingText.Restart();
        }
    }

    private void TypingText_Loaded(object sender, RoutedEventArgs e)
    {
        if (AutoStart)
        {
            Restart();
        }
    }

    private void TypingText_Unloaded(object sender, RoutedEventArgs e)
    {
        _typingTimer.Stop();
        _cursorTimer.Stop();
    }

    private void Restart()
    {
        if (!IsLoaded)
        {
            return;
        }

        _typingTimer.Stop();
        _cursorTimer.Stop();
        _visibleLength = 0;
        _cursorVisible = true;
        VisibleRun.Text = string.Empty;
        CursorRun.Text = "▌";

        if (string.IsNullOrEmpty(Text))
        {
            CursorRun.Text = string.Empty;
            return;
        }

        _typingTimer.Start();
        _cursorTimer.Start();
    }

    public void Start() => Restart();

    public void Stop()
    {
        _typingTimer.Stop();
        _cursorTimer.Stop();
        CursorRun.Text = string.Empty;
    }

    private void TypingTimer_Tick(object? sender, EventArgs e)
    {
        _visibleLength++;
        VisibleRun.Text = Text[.._visibleLength];

        if (_visibleLength >= Text.Length)
        {
            _typingTimer.Stop();
            _cursorTimer.Stop();
            CursorRun.Text = string.Empty;
        }
    }

    private void CursorTimer_Tick(object? sender, EventArgs e)
    {
        _cursorVisible = !_cursorVisible;
        CursorRun.Text = _cursorVisible ? "▌" : string.Empty;
    }
}
