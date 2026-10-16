using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using EntityTracker.Wpf;
using EntityTracker.Wpf.Controls;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Screenshots;

/// <param name="textReplacements">
/// Text shown differently in every screenshot, such as a fixture's real folder shown as an example
/// path. A screenshot never shows the user's profile or temp folder: a capture that would fails.
/// </param>
internal sealed class WpfScreenshotRenderer(Window window, string outputDirectory,
    IReadOnlyDictionary<string, string>? textReplacements = null)
{
    private const double Dpi = 96;

    private static readonly string[] PrivatePaths =
    [
        Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
    ];

    private readonly Window _window = window ?? throw new ArgumentNullException(nameof(window));
    private readonly string _outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
        ? throw new ArgumentException("An output directory is required.", nameof(outputDirectory))
        : outputDirectory;

    private FrameworkElement Root => (FrameworkElement)_window.Content;

    internal async Task CaptureAsync(string fileName, int settleMilliseconds = 150)
    {
        await SettleAsync(settleMilliseconds);
        // The main window is always 1920 × 1080; dialogs keep their own size.
        if (_window is MainWindow) ScreenshotWindowSize.EnsureFullSize(Root);
        using (ReplaceText(fileName))
            Save(RenderVisual(Root, includePageBackground: true), Path.Combine(_outputDirectory, fileName));
    }

    /// <summary>
    /// Shows the replacement text in every rendered label until disposed, then checks that no label
    /// shows a private folder.
    /// </summary>
    private IDisposable ReplaceText(string fileName)
    {
        List<(TextBlock Label, string Text)> replaced = [];
        foreach (TextBlock label in FindVisualDescendants<TextBlock>(Root))
        {
            if (textReplacements is not null && textReplacements.TryGetValue(label.Text, out string? shown))
            {
                replaced.Add((label, label.Text));
                label.SetCurrentValue(TextBlock.TextProperty, shown);
            }
        }
        Root.UpdateLayout();
        TextBlock? leak = FindVisualDescendants<TextBlock>(Root).FirstOrDefault(label =>
            label.IsVisible && PrivatePaths.Any(path =>
                label.Text.Contains(path, StringComparison.OrdinalIgnoreCase)));
        if (leak is not null)
        {
            Restore();
            throw new InvalidOperationException(
                $"{fileName} would show a local folder: \"{leak.Text}\". Add it to the renderer's text replacements.");
        }
        return new Restorer(Restore);

        void Restore()
        {
            foreach ((TextBlock label, string text) in replaced)
                label.SetCurrentValue(TextBlock.TextProperty, text);
            replaced.Clear();
        }
    }

    private sealed class Restorer(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    internal async Task CaptureWithTextOverrideAsync(
        string fileName, string actualText, string screenshotText, int settleMilliseconds = 150)
    {
        TextBlock pathLabel = FindVisualDescendants<TextBlock>(Root)
            .SingleOrDefault(label => label.Text == actualText)
            ?? throw new InvalidOperationException("The repository path label was not rendered.");
        pathLabel.SetCurrentValue(TextBlock.TextProperty, screenshotText);
        try
        {
            await CaptureAsync(fileName, settleMilliseconds);
        }
        finally
        {
            pathLabel.SetCurrentValue(TextBlock.TextProperty, actualText);
        }
    }

    internal async Task CaptureGraphIssueAsync(string fileName)
    {
        await SettleAsync();
        Button button = FindVisualDescendants<Button>(Root)
            .FirstOrDefault(static candidate =>
                candidate.DataContext is EntityOverviewRow { HasGraphIssue: true } &&
                candidate.ContextMenu is not null)
            ?? throw new InvalidOperationException(
                "A rendered dependency issue button could not be found.");

        ContextMenu menu = button.ContextMenu!;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        try
        {
            await SettleAsync();
            RenderTargetBitmap main = RenderVisual(Root, includePageBackground: true);
            RenderTargetBitmap popup = RenderVisual(menu);
            Point anchor = button.TransformToAncestor(Root)
                .Transform(new Point(button.ActualWidth + 8, 0));
            double left = Math.Clamp(anchor.X, 0, Root.ActualWidth - popup.PixelWidth);
            double top = Math.Clamp(anchor.Y, 0, Root.ActualHeight - popup.PixelHeight);

            DrawingVisual drawing = new();
            using (DrawingContext context = drawing.RenderOpen())
            {
                context.DrawImage(main, new Rect(0, 0, main.PixelWidth, main.PixelHeight));
                context.DrawImage(
                    popup,
                    new Rect(left, top, popup.PixelWidth, popup.PixelHeight));
            }

            RenderTargetBitmap composite = new(
                main.PixelWidth,
                main.PixelHeight,
                Dpi,
                Dpi,
                PixelFormats.Pbgra32);
            composite.Render(drawing);
            Save(composite, Path.Combine(_outputDirectory, fileName));
        }
        finally
        {
            menu.IsOpen = false;
            await SettleAsync();
        }
    }

    internal async Task CaptureOpenPopupAsync(string fileName)
    {
        Popup popup = FindVisualDescendants<FilterableColumnHeader>(Root)
            .Select(static header => header.FindName("FilterPopup"))
            .OfType<Popup>()
            .FirstOrDefault(static candidate => candidate.IsOpen && candidate.Child is FrameworkElement)
            ?? throw new InvalidOperationException("An open filter popup could not be found.");
        await CapturePopupAsync(popup, fileName);
    }

    internal async Task CapturePopupAsync(Popup popup, string fileName)
    {
        await SettleAsync();
        if (!popup.IsOpen || popup.Child is not FrameworkElement)
            throw new InvalidOperationException("The popup is not open.");
        FrameworkElement popupContent = (FrameworkElement)popup.Child;
        FrameworkElement placementTarget = popup.PlacementTarget as FrameworkElement
            ?? throw new InvalidOperationException("The open popup has no placement target.");

        RenderTargetBitmap main = RenderVisual(Root, includePageBackground: true);
        RenderTargetBitmap popupBitmap = RenderVisual(popupContent);
        Point anchor = placementTarget.TransformToAncestor(Root)
            .Transform(new Point(0, placementTarget.ActualHeight + 4));
        double left = Math.Clamp(anchor.X + popup.HorizontalOffset, 0,
            Root.ActualWidth - popupBitmap.PixelWidth);
        double top = Math.Clamp(anchor.Y + popup.VerticalOffset, 0,
            Root.ActualHeight - popupBitmap.PixelHeight);

        DrawingVisual drawing = new();
        using (DrawingContext context = drawing.RenderOpen())
        {
            context.DrawImage(main, new Rect(0, 0, main.PixelWidth, main.PixelHeight));
            context.DrawImage(
                popupBitmap,
                new Rect(left, top, popupBitmap.PixelWidth, popupBitmap.PixelHeight));
        }

        RenderTargetBitmap composite = new(
            main.PixelWidth,
            main.PixelHeight,
            Dpi,
            Dpi,
            PixelFormats.Pbgra32);
        composite.Render(drawing);
        Save(composite, Path.Combine(_outputDirectory, fileName));
    }

    internal async Task ScrollSectionIntoViewAndCaptureAsync(
        FrameworkElement section,
        ScrollViewer scrollViewer,
        string fileName)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(scrollViewer);
        await SettleAsync();
        Point currentPosition = section.TransformToAncestor(scrollViewer).Transform(new Point());
        scrollViewer.ScrollToVerticalOffset(
            Math.Max(0, scrollViewer.VerticalOffset + currentPosition.Y - 4));
        await CaptureAsync(fileName);
    }

    internal async Task BringNamedElementIntoViewAndCaptureAsync(
        string elementName,
        string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementName);
        FrameworkElement element = FindVisualDescendants<FrameworkElement>(Root)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                elementName,
                StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"The rendered element '{elementName}' could not be found.");
        element.BringIntoView();
        await CaptureAsync(fileName, settleMilliseconds: 500);
    }

    internal async Task SettleAsync(int milliseconds = 150)
    {
        await _window.Dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.ContextIdle);
        _window.UpdateLayout();
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds);
        }

        await _window.Dispatcher.InvokeAsync(
            static () => { },
            DispatcherPriority.ApplicationIdle);
        _window.UpdateLayout();
    }

    private static RenderTargetBitmap RenderVisual(
        FrameworkElement visual,
        bool includePageBackground = false)
    {
        visual.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
        RenderTargetBitmap raw = new(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        raw.Render(visual);
        if (!includePageBackground)
        {
            return raw;
        }

        Brush pageBackground = (Brush)System.Windows.Application.Current.FindResource(
            "Brush.Surface.Page");
        DrawingVisual drawing = new();
        using (DrawingContext context = drawing.RenderOpen())
        {
            context.DrawRectangle(pageBackground, null, new Rect(0, 0, width, height));
            context.DrawImage(raw, new Rect(0, 0, width, height));
        }

        RenderTargetBitmap opaque = new(width, height, Dpi, Dpi, PixelFormats.Pbgra32);
        opaque.Render(drawing);
        return opaque;
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static Int32Rect ClampCrop(
        int x,
        int y,
        int width,
        int height,
        int maximumWidth,
        int maximumHeight)
    {
        int safeX = Math.Clamp(x, 0, maximumWidth - 1);
        int safeY = Math.Clamp(y, 0, maximumHeight - 1);
        int safeWidth = Math.Clamp(width, 1, maximumWidth - safeX);
        int safeHeight = Math.Clamp(height, 1, maximumHeight - safeY);
        return new Int32Rect(safeX, safeY, safeWidth, safeHeight);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

}
