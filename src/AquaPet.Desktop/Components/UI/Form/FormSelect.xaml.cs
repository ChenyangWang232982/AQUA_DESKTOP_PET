using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace AquaPet.Desktop.Components.UI.Form;

public partial class FormSelect : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(FormSelect), new PropertyMetadata(""));
    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(nameof(Detail), typeof(string), typeof(FormSelect), new PropertyMetadata(""));
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(nameof(Options), typeof(IEnumerable<FormOption>), typeof(FormSelect));
    public static readonly DependencyProperty SelectedValueProperty = DependencyProperty.Register(nameof(SelectedValue), typeof(string), typeof(FormSelect), new PropertyMetadata(""));
    public event SelectionChangedEventHandler? ValueChanged;
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Detail { get => (string)GetValue(DetailProperty); set => SetValue(DetailProperty, value); }
    public IEnumerable<FormOption>? Options { get => (IEnumerable<FormOption>?)GetValue(OptionsProperty); set => SetValue(OptionsProperty, value); }
    public string SelectedValue { get => (string)GetValue(SelectedValueProperty); set => SetValue(SelectedValueProperty, value); }
    public FormSelect() => InitializeComponent();
    private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ValueChanged?.Invoke(this, e);
}
