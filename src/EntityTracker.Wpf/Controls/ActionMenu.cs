using System.Windows.Controls;

namespace EntityTracker.Wpf.Controls;

/// <summary>Opens the native menu of a "…" button: below the button, for the item the button belongs to.</summary>
internal static class ActionMenu
{
    public static void Open(object sender)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }
}
