using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Services;

internal static class ProjectLocalNameDialog
{
    public static string? Prompt(Window? owner, string sharedName)
    {
        TextBox input = new() { Margin = new Thickness(0, 4, 0, 0), MinWidth = 280,
            Text = sharedName + " (local)" };
        AutomationProperties.SetName(input, "Local project name");
        Label label = new() { Content = "_Local project name", Target = input, Padding = new Thickness(0),
            Margin = new Thickness(0, 14, 0, 0) };
        // Shown under the field instead of a second message box, so the dialog stays the only window.
        TextBlock error = new()
        {
            Margin = new Thickness(0, 6, 0, 0),
            Text = "Enter a local name for this Project.",
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        error.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Critical");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        Window dialog = new()
        {
            Title = "Choose a local Project name",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };
        Button accept = new() { Content = "_Import project", IsDefault = true, MinWidth = 110 };
        accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButtonStyle");
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                error.Visibility = Visibility.Visible;
                input.Focus();
                return;
            }
            dialog.DialogResult = true;
        };
        input.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;
        StackPanel buttons = new() { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        StackPanel content = new() { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock
        {
            Text = $"'{sharedName}' is already used locally. Choose a private name for this installation.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(label);
        content.Children.Add(input);
        content.Children.Add(error);
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
