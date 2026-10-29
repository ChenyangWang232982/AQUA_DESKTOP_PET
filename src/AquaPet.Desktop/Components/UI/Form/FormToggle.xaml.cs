using System.Windows;
using System.Windows.Controls;

namespace AquaPet.Desktop.Components.UI.Form;

public partial class FormToggle : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(FormToggle), new PropertyMetadata(""));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(FormToggle), new PropertyMetadata(""));
    public static readonly DependencyProperty IsCheckedProperty = DependencyProperty.Register(nameof(IsChecked), typeof(bool), typeof(FormToggle), new PropertyMetadata(false));
    public event RoutedEventHandler? ValueChanged;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public bool IsChecked { get => (bool)GetValue(IsCheckedProperty); set => SetValue(IsCheckedProperty, value); }
    public FormToggle() => InitializeComponent();
    private void CheckBox_Changed(object sender, RoutedEventArgs e) => ValueChanged?.Invoke(this, e);
}
