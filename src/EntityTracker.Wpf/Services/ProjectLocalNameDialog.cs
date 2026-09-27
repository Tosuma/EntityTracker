using System.Windows;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Services;

internal static class ProjectLocalNameDialog
{
    public static string? Prompt(Window? owner, string sharedName)
    {
        TextBox input = new() { Margin = new Thickness(0, 12, 0, 12), MinWidth = 280,
            Text = sharedName + " (local)" };
        Window dialog = new()
        {
            Title = "Choose a local Project name",
            Width = 420,
            Height = 205,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = owner
        };
        Button accept = new() { Content = "Import Project", IsDefault = true, MinWidth = 110 };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                MessageBox.Show(dialog, "Enter a unique local Project name.", "Project name",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            dialog.DialogResult = true;
        };
        StackPanel buttons = new() { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        StackPanel content = new() { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock
        {
            Text = $"'{sharedName}' is already used locally. Choose a private name for this installation.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(input);
        content.Children.Add(buttons);
        dialog.Content = content;
        dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
