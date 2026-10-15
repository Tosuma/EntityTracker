using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// Shows a grey hint in an empty text field, such as "Search active entities", and hides it as soon as
/// something is typed. Set <c>controls:Placeholder.Text</c> on a <see cref="TextBox"/> or an editable
/// <see cref="ComboBox"/>. The hint is drawn by an adorner over the field's text area, so the theme's
/// templates stay as they are; it doesn't replace the field's label or automation name.
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
            if (Adorner is not null) return;
            // An editable ComboBox types into the text box of its template.
            _input = field as TextBox;
            if (field is ComboBox comboBox)
            {
                comboBox.ApplyTemplate();
                _input = comboBox.Template?.FindName("PART_EditableTextBox", comboBox) as TextBox;
            }
            if (_input is null || AdornerLayer.GetAdornerLayer(_input) is not { } layer) return;

            Adorner = new PlaceholderAdorner(_input, GetText(field));
            layer.Add(Adorner);
            _input.TextChanged += OnInputChanged;
            field.IsVisibleChanged += OnFieldChanged;
            field.IsEnabledChanged += OnFieldChanged;
            Refresh();
        }

        public void Detach()
        {
            if (Adorner is null || _input is null) return;
            AdornerLayer.GetAdornerLayer(_input)?.Remove(Adorner);
            _input.TextChanged -= OnInputChanged;
            field.IsVisibleChanged -= OnFieldChanged;
            field.IsEnabledChanged -= OnFieldChanged;
            Adorner = null;
            _input = null;
        }

        public void Refresh()
        {
            if (Adorner is null || _input is null) return;
            Adorner.Text = GetText(field);
            // An adorner stays on screen when its field is collapsed, so it follows the field's visibility.
            Adorner.Visibility = field.IsVisible && _input.Text.Length == 0 && Adorner.Text.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            Adorner.Opacity = field.IsEnabled ? 1 : 0.6;
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void OnFieldChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh();
    }
}

/// <summary>Draws a placeholder hint where the adorned text box's text starts.</summary>
internal sealed class PlaceholderAdorner : Adorner
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(PlaceholderAdorner),
        new FrameworkPropertyMetadata(SystemColors.GrayTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));

    // A text box draws its text this far inside its padding.
    private const double TextInset = 2;

    private string _text;

    public PlaceholderAdorner(TextBox input, string text) : base(input)
    {
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
        TextBox input = (TextBox)AdornedElement;
        Thickness border = input.BorderThickness;
        Thickness padding = input.Padding;
        double left = border.Left + padding.Left + TextInset;
        double width = input.ActualWidth - left - border.Right - padding.Right - TextInset;
        if (_text.Length == 0 || width <= 0) return;

        FormattedText text = new(_text, CultureInfo.CurrentUICulture, input.FlowDirection,
            new Typeface(input.FontFamily, input.FontStyle, input.FontWeight, input.FontStretch),
            input.FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = width,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        double contentTop = border.Top + padding.Top;
        double contentHeight = input.ActualHeight - contentTop - border.Bottom - padding.Bottom;
        double top = input.VerticalContentAlignment switch
        {
            VerticalAlignment.Center => contentTop + Math.Max(0, (contentHeight - text.Height) / 2),
            VerticalAlignment.Bottom => contentTop + Math.Max(0, contentHeight - text.Height),
            _ => contentTop
        };
        drawingContext.DrawText(text, new Point(left, top));
    }
}
