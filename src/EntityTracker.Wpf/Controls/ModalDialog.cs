using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace EntityTracker.Wpf.Controls;

/// <summary>How wide a <see cref="ModalDialog"/> is: a question, a form or list, or a large editor.</summary>
public enum ModalDialogSize
{
    /// <summary>Up to 560 wide: confirmations and short forms.</summary>
    Small,

    /// <summary>Up to 680 wide: lists and forms with more to read.</summary>
    Medium,

    /// <summary>Up to 960 wide and as tall as the page allows: editors and reviews.</summary>
    Large
}

/// <summary>
/// A dialog over the page: it dims what is behind it, keeps Tab inside, moves the focus in when it opens
/// and back to what opened it when it closes, and closes on Esc. A list or calendar that is open inside it
/// takes the Esc first, and a dialog over another closes alone, because Esc goes to the innermost one.
/// The dialog has a title, an optional description, the content, and a footer: <see cref="Footer"/> on
/// the right, commit first and Cancel last, and <see cref="DestructiveAction"/> alone on the left.
/// </summary>
[ContentProperty(nameof(Body))]
public sealed class ModalDialog : Decorator
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(ModalDialog),
        new PropertyMetadata(false, static (d, e) => ((ModalDialog)d).OnIsOpenChanged((bool)e.NewValue)));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ModalDialog),
        new PropertyMetadata(string.Empty, static (d, e) => ((ModalDialog)d).ShowTitle((string)e.NewValue)));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ModalDialog),
        new PropertyMetadata(string.Empty, static (d, e) => ((ModalDialog)d).ShowDescription((string)e.NewValue)));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(ModalDialogSize), typeof(ModalDialog),
        new PropertyMetadata(ModalDialogSize.Small, static (d, _) => ((ModalDialog)d).ApplySize()));

    public static readonly DependencyProperty BodyProperty = DependencyProperty.Register(
        nameof(Body), typeof(UIElement), typeof(ModalDialog),
        new PropertyMetadata(null, static (d, _) => ((ModalDialog)d).PlaceBody()));

    public static readonly DependencyProperty ScrollsBodyProperty = DependencyProperty.Register(
        nameof(ScrollsBody), typeof(bool), typeof(ModalDialog),
        new PropertyMetadata(true, static (d, _) => ((ModalDialog)d).PlaceBody()));

    public static readonly DependencyProperty FooterProperty = DependencyProperty.Register(
        nameof(Footer), typeof(UIElement), typeof(ModalDialog),
        new PropertyMetadata(null, static (d, e) => ((ModalDialog)d)._actions.Child = (UIElement?)e.NewValue));

    public static readonly DependencyProperty DestructiveActionProperty = DependencyProperty.Register(
        nameof(DestructiveAction), typeof(UIElement), typeof(ModalDialog),
        new PropertyMetadata(null, static (d, e) => ((ModalDialog)d)._destructive.Child = (UIElement?)e.NewValue));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(ModalDialog));

    public static readonly DependencyProperty InitialFocusProperty = DependencyProperty.Register(
        nameof(InitialFocus), typeof(IInputElement), typeof(ModalDialog));

    public static readonly DependencyProperty UsesStrongScrimProperty = DependencyProperty.Register(
        nameof(UsesStrongScrim), typeof(bool), typeof(ModalDialog),
        new PropertyMetadata(false, static (d, e) => ((ModalDialog)d).ApplyScrim((bool)e.NewValue)));

    private readonly Grid _scrim = new();
    private readonly Border _card = new();
    private readonly Grid _layout = new();
    private readonly TextBlock _title = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _description = new()
    {
        Margin = new Thickness(0, 3, 0, 0),
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed
    };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Focusable = false
    };
    private readonly Border _body = new();
    private readonly Border _destructive = new();
    private readonly Border _actions = new() { HorizontalAlignment = HorizontalAlignment.Right };

    public ModalDialog()
    {
        Visibility = Visibility.Collapsed;
        ApplyScrim(false);
        FocusManager.SetIsFocusScope(_scrim, true);
        KeyboardNavigation.SetTabNavigation(_scrim, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetControlTabNavigation(_scrim, KeyboardNavigationMode.Cycle);
        KeyboardNavigation.SetDirectionalNavigation(_scrim, KeyboardNavigationMode.Cycle);

        _card.Margin = new Thickness(24);
        _card.Padding = new Thickness(22);
        _card.BorderThickness = new Thickness(1);
        _card.CornerRadius = new CornerRadius(7);
        _card.Focusable = true;
        _card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface.Page");
        _card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border.Medium");
        _title.SetResourceReference(StyleProperty, "DialogTitleTextStyle");
        _description.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Secondary");

        StackPanel header = new() { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(_title);
        header.Children.Add(_description);

        DockPanel footer = new() { Margin = new Thickness(0, 16, 0, 0), LastChildFill = true };
        DockPanel.SetDock(_destructive, Dock.Left);
        footer.Children.Add(_destructive);
        footer.Children.Add(_actions);

        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(footer, 2);
        _layout.Children.Add(header);
        _layout.Children.Add(footer);

        _card.Child = _layout;
        _scrim.Children.Add(_card);
        Child = _scrim;
        ApplySize();
        PlaceBody();
    }

    /// <summary>Raised when the dialog has opened, before the focus moves in; a handler may focus something itself.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised after the dialog has closed and the focus has gone back.</summary>
    public event EventHandler? Closed;

    /// <summary>Raised on Esc, after <see cref="CancelCommand"/>, for a view that decides itself how to close.</summary>
    public event EventHandler? CancelRequested;

    /// <summary>Gets or sets whether the dialog is shown.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets a line under the title; empty shows none.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public ModalDialogSize Size
    {
        get => (ModalDialogSize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets or sets the dialog's content, between the title and the footer.</summary>
    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>Gets or sets whether the dialog scrolls its content; false for content that scrolls itself.</summary>
    public bool ScrollsBody
    {
        get => (bool)GetValue(ScrollsBodyProperty);
        set => SetValue(ScrollsBodyProperty, value);
    }

    /// <summary>Gets or sets the buttons on the right of the footer: the commit first, Cancel last.</summary>
    public UIElement? Footer
    {
        get => (UIElement?)GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    /// <summary>Gets or sets an action that removes something, alone on the left of the footer.</summary>
    public UIElement? DestructiveAction
    {
        get => (UIElement?)GetValue(DestructiveActionProperty);
        set => SetValue(DestructiveActionProperty, value);
    }

    /// <summary>Gets or sets the command Esc runs, usually the one behind Cancel.</summary>
    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    /// <summary>Gets or sets what takes the focus when the dialog opens; otherwise its first control does.</summary>
    public IInputElement? InitialFocus
    {
        get => (IInputElement?)GetValue(InitialFocusProperty);
        set => SetValue(InitialFocusProperty, value);
    }

    /// <summary>Gets or sets whether the dimming is darker, for a dialog over the whole window.</summary>
    public bool UsesStrongScrim
    {
        get => (bool)GetValue(UsesStrongScrimProperty);
        set => SetValue(UsesStrongScrimProperty, value);
    }

    /// <summary>
    /// Gets or sets where the focus goes when the dialog closes. It is what had the focus when the dialog
    /// opened; an <see cref="Opened"/> handler can point it elsewhere, such as the button whose menu opened it.
    /// </summary>
    public IInputElement? FocusOnClose { get; set; }

    /// <summary>Does what Esc does: runs <see cref="CancelCommand"/> and raises <see cref="CancelRequested"/>.</summary>
    public void RequestCancel()
    {
        if (CancelCommand is { } command && command.CanExecute(null))
            command.Execute(null);
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // Esc bubbles up from the focus, so an open list inside, or a dialog over this one, has it first.
        if (e.Handled || e.Key != Key.Escape || !IsOpen)
            return;
        e.Handled = true;
        RequestCancel();
    }

    private void OnIsOpenChanged(bool open)
    {
        Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            FocusOnClose = Keyboard.FocusedElement;
            _scroll.ScrollToTop();
            Opened?.Invoke(this, EventArgs.Empty);
            Dispatcher.BeginInvoke(FocusInside, DispatcherPriority.Input);
            return;
        }

        IInputElement? target = FocusOnClose;
        FocusOnClose = null;
        Dispatcher.BeginInvoke(() =>
        {
            if (target is UIElement { IsVisible: true } element)
                element.Focus();
            else if (target is ContentElement content)
                Keyboard.Focus(content);
            Closed?.Invoke(this, EventArgs.Empty);
        }, DispatcherPriority.Input);
    }

    private void FocusInside()
    {
        if (!IsOpen || IsKeyboardFocusWithin)
            return;
        if (InitialFocus is UIElement { IsVisible: true, IsEnabled: true } initial && initial.Focus())
            return;
        if (!_layout.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
            _card.Focus();
    }

    private void ShowTitle(string title)
    {
        _title.Text = title;
        AutomationProperties.SetName(_card, title);
    }

    private void ShowDescription(string text)
    {
        _description.Text = text;
        _description.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyScrim(bool strong) =>
        _scrim.SetResourceReference(Panel.BackgroundProperty, strong ? "Brush.Overlay.Strong" : "Brush.Overlay");

    private void ApplySize()
    {
        (double width, double height, VerticalAlignment alignment) = Size switch
        {
            ModalDialogSize.Large => (960d, 760d, VerticalAlignment.Stretch),
            ModalDialogSize.Medium => (680d, 680d, VerticalAlignment.Center),
            _ => (560d, 680d, VerticalAlignment.Center)
        };
        _card.MaxWidth = width;
        _card.MaxHeight = height;
        _card.HorizontalAlignment = HorizontalAlignment.Stretch;
        _card.VerticalAlignment = alignment;
    }

    private void PlaceBody()
    {
        _layout.Children.Remove(_scroll);
        _layout.Children.Remove(_body);
        _scroll.Content = null;
        _body.Child = Body;
        UIElement host = ScrollsBody ? _scroll : _body;
        if (ScrollsBody)
            _scroll.Content = _body;
        Grid.SetRow(host, 1);
        _layout.Children.Add(host);
    }
}
