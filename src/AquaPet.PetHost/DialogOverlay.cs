using System.Numerics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.UI;

namespace AquaPet.PetHost;

/// <summary>Draws a speech bubble and its text on a Composition surface.</summary>
internal sealed class DialogOverlay : IDisposable
{
    public const int Width = 360;
    // Relative to the sprite canvas: negative values overlap its transparent margin.
    public const int HorizontalGap = -90;
    public const int LeftSpace = Width + HorizontalGap;
    // Larger values move the bubble down towards the visible character.
    private const int VerticalOverlap = 85;
    private const float Padding = 12;
    private const float LineHeight = 19;
    private const float LineBaseline = 15;
    private readonly CompositionGraphicsDevice _graphicsDevice;
    private readonly CompositionDrawingSurface _surface;
    private readonly SpriteVisual _sprite;
    private readonly DispatcherQueueTimer _typingTimer;
    private readonly DispatcherQueueTimer _fadeTimer;
    private string _content = string.Empty;
    private int[] _textElements = [];
    private int _visibleElements;
    private int _typingDelayTicks;
    private bool _hovered;
    private float _height;

    public DialogOverlay(Compositor compositor)
    {
        Visual = compositor.CreateContainerVisual();
        Visual.IsVisible = false;
        Visual.CenterPoint = new Vector3(Width, 0, 0);
        _sprite = compositor.CreateSpriteVisual();
        Visual.Children.InsertAtTop(_sprite);
        var queue = DispatcherQueue.GetForCurrentThread();
        _typingTimer = queue.CreateTimer();
        _typingTimer.Interval = TimeSpan.FromMilliseconds(30);
        _typingTimer.Tick += (_, _) => AdvanceTyping();
        _fadeTimer = queue.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(350);
        _fadeTimer.IsRepeating = false;
        _fadeTimer.Tick += (_, _) => FinishFade();
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, CanvasDevice.GetSharedDevice());
        _surface = _graphicsDevice.CreateDrawingSurface(
            new Size(Width, 50), DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);
        var brush = compositor.CreateSurfaceBrush();
        brush.Surface = _surface;
        _sprite.Brush = brush;
        _sprite.Size = new Vector2(Width, 50);
    }

    public ContainerVisual Visual { get; }
    public bool IsVisible => Visual.IsVisible;
    public float Height => _height;
    public event EventHandler? Hidden;

    public void Show(string content, float petY)
    {
        var shouldPop = !Visual.IsVisible || _fadeTimer.IsRunning;
        _typingTimer.Stop();
        _fadeTimer.Stop();
        _sprite.IsVisible = true;
        if (shouldPop) _hovered = false;
        _content = Regex.Replace(content.Replace("\r\n", "\n").Replace('\r', '\n'), "\\n[ \\t]*\\n+", "\n");
        _textElements = StringInfo.ParseCombiningCharacters(_content);
        _visibleElements = 0;
        _typingDelayTicks = shouldPop ? 7 : 0;
        using var format = new CanvasTextFormat
        {
            FontFamily = "Microsoft YaHei",
            FontSize = 16,
            LineSpacing = LineHeight,
            LineSpacingBaseline = LineBaseline,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        // Even one line per text element fits, so measurement has no fixed height cap.
        var measurementHeight = Math.Max(50f, (_textElements.Length + 1f) * LineHeight + Padding * 2);
        using var layout = new CanvasTextLayout(
            CanvasDevice.GetSharedDevice(), _content, format, Width - Padding * 2 - 8, measurementHeight);
        _height = Math.Max((float)Math.Ceiling(layout.LayoutBounds.Height) + Padding * 2, 50);
        _surface.Resize(new Windows.Graphics.SizeInt32 { Width = Width, Height = (int)_height });
        _sprite.Size = new Vector2(Width, _height);
        Visual.Size = new Vector2(Width, _height);
        SetPosition(petY);
        DrawText(string.Empty);
        Visual.IsVisible = true;
        Visual.StopAnimation("Scale");
        Visual.StopAnimation("Opacity");
        if (shouldPop)
        {
            Visual.Scale = new Vector3(0.75f, 0.75f, 1);
            Visual.Opacity = 0;
            var scale = Visual.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, Vector3.One);
            scale.Duration = TimeSpan.FromMilliseconds(220);
            var opacity = Visual.Compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 1);
            opacity.Duration = TimeSpan.FromMilliseconds(180);
            Visual.StartAnimation("Scale", scale);
            Visual.StartAnimation("Opacity", opacity);
        }
        else
        {
            Visual.Scale = Vector3.One;
            Visual.Opacity = 1;
        }
        if (_textElements.Length > 0) _typingTimer.Start();
    }

    private void DrawText(string content)
    {
        using var drawing = CanvasComposition.CreateDrawingSession(_surface);
        drawing.Clear(Color.FromArgb(0, 0, 0, 0));
        var ink = Color.FromArgb(255, 20, 32, 51);
        var white = Color.FromArgb(255, 255, 255, 255);
        drawing.FillRoundedRectangle(2, 2, Width - 4, _height - 4, 8, 8, white);
        drawing.DrawRoundedRectangle(2, 2, Width - 4, _height - 4, 8, 8, ink, 2);
        using var format = new CanvasTextFormat
        {
            FontFamily = "Microsoft YaHei", FontSize = 16,
            LineSpacing = LineHeight, LineSpacingBaseline = LineBaseline,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), content, format,
            Width - Padding * 2 - 8, _height - Padding * 2);
        drawing.DrawTextLayout(layout, Padding, Padding, ink);
    }

    private void AdvanceTyping()
    {
        if (_typingDelayTicks-- > 0) return;
        _visibleElements++;
        var end = _visibleElements < _textElements.Length ? _textElements[_visibleElements] : _content.Length;
        DrawText(_content[..end]);
        if (_visibleElements >= _textElements.Length) _typingTimer.Stop();
    }

    public bool Contains(double x, double y, float petY)
    {
        var top = petY + Visual.Offset.Y;
        return IsVisible && x >= 0 && x <= Width && y >= top && y <= top + _height;
    }

    public void SetHovered(bool hovered, float petY)
    {
        if (_hovered == hovered || !IsVisible) return;
        _hovered = hovered;
        var target = Math.Max(-_height + VerticalOverlap, -petY) - (hovered ? 8 : 0);
        var animation = Visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, (float)target);
        animation.Duration = TimeSpan.FromMilliseconds(150);
        Visual.StartAnimation("Offset.Y", animation);
    }

    public void FadeOut()
    {
        if (!IsVisible || _fadeTimer.IsRunning) return;
        _typingTimer.Stop();
        var fade = Visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0);
        fade.Duration = _fadeTimer.Interval;
        Visual.StartAnimation("Opacity", fade);
        _fadeTimer.Start();
    }

    private void FinishFade()
    {
        Visual.IsVisible = false;
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    public void SetPosition(float petY)
    {
        Visual.StopAnimation("Offset.Y");
        Visual.Offset = new Vector3(-LeftSpace, Math.Max(-_height + VerticalOverlap, -petY) - (_hovered ? 8 : 0), 1);
    }

    public void Dispose()
    {
        _typingTimer.Stop();
        _fadeTimer.Stop();
        _sprite.Brush?.Dispose();
        _sprite.Brush = null;
        _surface.Dispose();
        _graphicsDevice.Dispose();
    }
}
