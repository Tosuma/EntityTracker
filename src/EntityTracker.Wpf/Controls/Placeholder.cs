using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Shows a grey hint in an empty text field, such as "Search active entities", and hides it as soon as
/// something is typed. Set <c>controls:Placeholder.Text</c> on a <see cref="TextBox"/> or an editable
/// <see cref="ComboBox"/>. The hint is an adorner in the field's own text area, the layer the field's caret
/// is drawn in, so the theme's templates stay as they are and whatever covers the field covers its hint too.
/// It doesn't replace the field's label or automation name.
/// </summary>
public static class Placeholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(Placeholder), new PropertyMetadata(string.Empty, OnTextChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(PlaceholderState), typeof(Placeholder));

    public static string GetText(DependencyObject element) => (string)element.GetValue(TextProperty);

    public static void SetText(DependencyObject element, string value) => element.SetValue(TextProperty, value);

    /// <summary>The adorner showing the hint, while the field is loaded; for tests.</summary>
    internal static PlaceholderAdorner? GetAdorner(Control field) =>
        (field.GetValue(StateProperty) as PlaceholderState)?.Adorner;

    private static void OnTextChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not (TextBox or ComboBox)) return;
        Control field = (Control)element;
        if (field.GetValue(StateProperty) is PlaceholderState existing)
        {
            existing.Refresh();
            return;
        }

        PlaceholderState state = new(field);
        field.SetValue(StateProperty, state);
        field.Loaded += (_, _) => state.Attach();
        field.Unloaded += (_, _) => state.Detach();
        if (field.IsLoaded) state.Attach();
    }

    /// <summary>Connects one field to its adorner while the field is loaded.</summary>
    private sealed class PlaceholderState(Control field)
    {
        private TextBox? _input;

        public PlaceholderAdorner? Adorner { get; private set; }

        public void Attach()
        {
            if (_input is not null) return;
            // An editable ComboBox types into the text box of its template.
            _input = field as TextBox;
            if (field is ComboBox comboBox)
            {
                comboBox.ApplyTemplate();
                _input = comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
            }
            if (_input is null) return;

            _input.TextChanged += OnInputChanged;
            field.IsVisibleChanged += OnFieldVisibleChanged;
            field.IsEnabledChanged += OnFieldEnabledChanged;
            AddAdorner();
        }

        public void Detach()
        {
            if (_input is null) return;
            if (Adorner is not null)
            {
                AdornerLayer.GetAdornerLayer(Adorner.AdornedElement)?.Remove(Adorner);
                Adorner = null;
            }
            _input.TextChanged -= OnInputChanged;
            field.IsVisibleChanged -= OnFieldVisibleChanged;
            field.IsEnabledChanged -= OnFieldEnabledChanged;
            _input = null;
        }

        public void Refresh()
        {
            if (Adorner is null || _input is null) return;
            Adorner.Text = GetText(field);
            Adorner.Visibility = _input.Text.Length == 0 && Adorner.Text.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            Adorner.Opacity = field.IsEnabled ? 1 : 0.6;
        }

        /// <summary>
        /// Adds the hint to the layer inside the field. That layer only exists once the field has been laid
        /// out, which a field on a page or dialog that isn't showing hasn't been yet.
        /// </summary>
        private void AddAdorner()
        {
            if (Adorner is not null || _input is null) return;
            _input.ApplyTemplate();
            if (_input.Template?.FindName("PART_ContentHost", _input) is not ScrollViewer { Content: FrameworkElement textArea } ||
                AdornerLayer.GetAdornerLayer(textArea) is not { } layer)
            {
                return;
            }

            Adorner = new PlaceholderAdorner(textArea, _input, GetText(field));
            layer.Add(Adorner);
            Refresh();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void OnFieldEnabledChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();

        private void OnFieldVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Loaded priority: after the layout that gives a field shown for the first time its layer.
            if (Adorner is null && field.IsVisible)
                field.Dispatcher.BeginInvoke(AddAdorner, DispatcherPriority.Loaded);
        }
    }
}

/// <summary>Draws a placeholder hint where a text box's text starts: at its text area, which it adorns.</summary>
internal sealed class PlaceholderAdorner : Adorner
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(PlaceholderAdorner),
        new FrameworkPropertyMetadata(SystemColors.GrayTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly TextBox _input;
    private string _text;

    public PlaceholderAdorner(FrameworkElement textArea, TextBox input, string text) : base(textArea)
    {
        _input = input;
        _text = text;
        IsHitTestVisible = false;
        // The theme's secondary text colour, so the hint follows light and dark mode.
        SetResourceReference(ForegroundProperty, "Brush.Text.Secondary");
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        double width = AdornedElement.RenderSize.Width;
        if (_text.Length == 0 || width <= 0) return;

        FormattedText text = new(_text, CultureInfo.CurrentUICulture, _input.FlowDirection,
            new Typeface(_input.FontFamily, _input.FontStyle, _input.FontWeight, _input.FontStretch),
            _input.FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        double spare = Math.Max(0, AdornedElement.RenderSize.Height - text.Height);
        double top = _input.VerticalContentAlignment switch
        {
            VerticalAlignment.Center => spare / 2,
            VerticalAlignment.Bottom => spare,
            _ => 0
        };
        drawingContext.DrawText(text, new Point(0, top));
    }
}
