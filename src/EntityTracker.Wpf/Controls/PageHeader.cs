using System.Windows;
using System.Windows.Controls;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// The top of every page: a heading, in a row as tall as a toolbar so it sits at the same height on every
/// page, optional tabs and a description below it, and the page's toolbar. Set <see cref="Title"/>, or
/// <see cref="Heading"/> for a breadcrumb, and add the toolbar as a child (a WrapPanel with
/// PageToolbarStyle, which also moves it under the heading in the compact layout).
/// </summary>
public sealed class PageHeader : DockPanel
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader),
        new PropertyMetadata(string.Empty, static (d, e) => ((PageHeader)d)._title.Text = (string)e.NewValue));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(PageHeader),
        new PropertyMetadata(string.Empty, static (d, e) => ((PageHeader)d).ShowDescription((string)e.NewValue)));

    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(UIElement), typeof(PageHeader),
        new PropertyMetadata(null, static (d, e) => ((PageHeader)d).ShowHeading((UIElement?)e.NewValue)));

    public static readonly DependencyProperty TabsProperty = DependencyProperty.Register(
        nameof(Tabs), typeof(UIElement), typeof(PageHeader));

    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _description = new() { Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly Border _headingRow = new();
    private readonly StackPanel _text = new();

    public PageHeader()
    {
        Margin = new Thickness(0, 0, 0, 16);
        _title.SetResourceReference(StyleProperty, "PageHeadingTextStyle");
        _description.SetResourceReference(StyleProperty, "PageDescriptionTextStyle");
        _headingRow.SetResourceReference(MinHeightProperty, "Height.Control");
        _headingRow.Child = _title;
        _text.Children.Add(_headingRow);
        _text.Children.Add(_description);
    }

    /// <summary>Gets or sets the page's heading as plain text.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the line under the heading; empty shows none.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Gets or sets a heading with more than text, such as a breadcrumb, in place of <see cref="Title"/>.</summary>
    public UIElement? Heading
    {
        get => (UIElement?)GetValue(HeadingProperty);
        set => SetValue(HeadingProperty, value);
    }

    /// <summary>Gets or sets a row of tabs, across the whole header between the heading and the description.</summary>
    public UIElement? Tabs
    {
        get => (UIElement?)GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (Tabs is not null)
        {
            // Docked first, so they run under the toolbar too; the description goes below the tabs.
            _text.Children.Remove(_description);
            _description.Margin = new Thickness(0, 8, 0, 0);
            SetDock(_description, Dock.Bottom);
            SetDock(Tabs, Dock.Bottom);
            Children.Insert(0, _description);
            Children.Insert(1, Tabs);
        }

        // Last, so the toolbar added in XAML docks first and the heading takes the room that is left.
        Children.Add(_text);
    }

    private void ShowDescription(string text)
    {
        _description.Text = text;
        _description.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowHeading(UIElement? heading) => _headingRow.Child = heading ?? _title;
}
