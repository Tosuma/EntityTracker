using System.Windows;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// An icon followed by its label, such as a sidebar item. Both take the surrounding text colour and dim
/// when disabled. A button showing one needs an AutomationProperties.Name, since its content is no
/// longer plain text.
/// </summary>
public class IconLabel : StackPanel
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(IconLabel),
        new PropertyMetadata(null, (element, args) => ((IconLabel)element)._icon.IconKey = (string?)args.NewValue));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(IconLabel),
        new PropertyMetadata(string.Empty, (element, args) => ((IconLabel)element)._label.Text = (string?)args.NewValue));

    private readonly Icon _icon = new() { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Children.Add(_icon);
        Children.Add(_label);
        IsEnabledChanged += (_, _) => _label.Opacity = IsEnabled ? 1 : Icon.DisabledOpacity;
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
