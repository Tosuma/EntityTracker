using System.Windows;

namespace EntityTracker.Wpf.Views;

internal readonly record struct CardActionMenuPosition(double Left, double Top, bool OpensAbove);

internal static class CardActionMenuPlacement
{
    public static CardActionMenuPosition Calculate(Rect anchor, Size menu, Size viewport)
    {
        double left = Math.Clamp(anchor.Left, 0, Math.Max(0, viewport.Width - menu.Width));
        double below = anchor.Bottom - 1;
        bool opensAbove = below + menu.Height > viewport.Height &&
                          anchor.Top >= menu.Height - 1;
        double top = opensAbove ? anchor.Top - menu.Height + 1 : below;
        top = Math.Clamp(top, 0, Math.Max(0, viewport.Height - menu.Height));
        return new CardActionMenuPosition(left, top, opensAbove);
    }
}
