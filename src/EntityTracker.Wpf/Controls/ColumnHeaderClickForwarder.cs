using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Makes the whole table column header act as the button in it, not only the button's own area. The
/// Fluent header lays its content out at the content's size, so clicks beside the title land on the
/// header; they run the button's command. The button stays for keyboard focus and screen readers.
/// </summary>
internal sealed class ColumnHeaderClickForwarder
{
    private readonly FrameworkElement _content;
    private readonly ButtonBase _button;
    private DataGridColumnHeader? _header;

    private ColumnHeaderClickForwarder(FrameworkElement content, ButtonBase button)
    {
        _content = content;
        _button = button;
        content.Loaded += (_, _) => Attach();
        content.Unloaded += (_, _) => Detach();
    }

    public static void Attach(FrameworkElement content, ButtonBase button) => _ = new ColumnHeaderClickForwarder(content, button);

    private void Attach()
    {
        Detach();
        _header = FindAncestor<DataGridColumnHeader>(_content);
        if (_header is null)
        {
            return;
        }

        _header.Click += OnHeaderClick;
        _header.Cursor = Cursors.Hand;
        _header.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(FrameworkElement.ToolTip)) { Source = _button });
    }

    private void Detach()
    {
        if (_header is null)
        {
            return;
        }

        _header.Click -= OnHeaderClick;
        _header.ClearValue(FrameworkElement.CursorProperty);
        _header.ClearValue(FrameworkElement.ToolTipProperty);
        _header = null;
    }

    private void OnHeaderClick(object sender, RoutedEventArgs e)
    {
        // The button's own Click bubbles through the header too; that one has already run the command.
        if (!ReferenceEquals(e.OriginalSource, _header) ||
            _button.Command is not { } command ||
            !command.CanExecute(_button.CommandParameter))
        {
            return;
        }

        command.Execute(_button.CommandParameter);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(element);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
