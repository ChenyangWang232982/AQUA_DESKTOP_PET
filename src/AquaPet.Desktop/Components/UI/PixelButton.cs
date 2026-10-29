using System.Windows;
using System.Windows.Controls;

namespace AquaPet.Desktop.Components.UI;

/// <summary>AquaPet 的基础像素风按钮；调用方只需设置 Content。</summary>
public class PixelButton : Button
{
    static PixelButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PixelButton),
            new FrameworkPropertyMetadata(typeof(PixelButton)));
    }
}
