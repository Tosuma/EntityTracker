using System.Windows;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Carries a view's DataContext into elements outside its visual tree, such as DataGrid column headers.
/// As a Freezable in the view's resources it inherits the DataContext; bind with
/// <c>{Binding Data.Property, Source={StaticResource Proxy}}</c>.
/// </summary>
public sealed class BindingProxy : Freezable
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(object), typeof(BindingProxy));

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new BindingProxy();
}
