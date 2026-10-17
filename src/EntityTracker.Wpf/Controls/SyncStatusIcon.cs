using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Controls;

/// <summary>
/// A Project's sync state as an icon, the same wherever it is shown. The icon and colour for each state
/// are chosen here; its tooltip and automation name are the indicator's sentence. Nothing is shown when
/// the Project has no Git repository. Set <see cref="ProjectId"/> and it follows that Project in the app's
/// <see cref="ProjectSyncIndicators"/>; or set <see cref="Indicator"/> directly.
/// </summary>
public sealed class SyncStatusIcon : Icon
{
    public static readonly DependencyProperty IndicatorProperty = DependencyProperty.Register(
        nameof(Indicator), typeof(ProjectSyncIndicator), typeof(SyncStatusIcon),
        new PropertyMetadata(ProjectSyncIndicator.None, (element, _) => ((SyncStatusIcon)element).Apply()));

    public static readonly DependencyProperty ProjectIdProperty = DependencyProperty.Register(
        nameof(ProjectId), typeof(Guid?), typeof(SyncStatusIcon),
        new PropertyMetadata(null, (element, _) => ((SyncStatusIcon)element).Follow()));

    private readonly RotateTransform _rotation = new();
    private ProjectSyncIndicators? _source;

    public SyncStatusIcon()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotation;
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        ToolTipService.SetInitialShowDelay(this, 250);
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        Apply();
    }

    public Guid? ProjectId
    {
        get => (Guid?)GetValue(ProjectIdProperty);
        set => SetValue(ProjectIdProperty, value);
    }

    public ProjectSyncIndicator Indicator
    {
        get => (ProjectSyncIndicator)GetValue(IndicatorProperty);
        set => SetValue(IndicatorProperty, value);
    }

    private void Attach()
    {
        if (_source is not null) return;
        _source = TryFindResource(ProjectSyncIndicators.ResourceKey) as ProjectSyncIndicators;
        if (_source is not null) _source.Changed += OnIndicatorsChanged;
        Follow();
    }

    private void Detach()
    {
        if (_source is not null) _source.Changed -= OnIndicatorsChanged;
        _source = null;
    }

    private void OnIndicatorsChanged(object? sender, EventArgs e) => Follow();

    // Without a ProjectId the Indicator is set or bound directly, as in the header, and is left alone.
    private void Follow()
    {
        if (ProjectId is { } id && _source is not null) Indicator = _source.Get(id);
    }

    /// <summary>The icon resource and colour resource for each state.</summary>
    internal static (string Icon, string Brush) Look(ProjectSyncIndicatorKind kind) => kind switch
    {
        ProjectSyncIndicatorKind.Synced => ("Icon.CheckmarkCircle", "Brush.SyncState.Synced"),
        ProjectSyncIndicatorKind.Unsynced => ("Icon.CloudArrowUp", "Brush.SyncState.Unsynced"),
        ProjectSyncIndicatorKind.Syncing => ("Icon.ArrowSync", "AccentFillColorDefaultBrush"),
        ProjectSyncIndicatorKind.NeedsAttention => ("Icon.Warning", "Brush.SyncState.NeedsAttention"),
        ProjectSyncIndicatorKind.Paused => ("Icon.PauseCircle", "Brush.Text.Secondary"),
        _ => (string.Empty, string.Empty)
    };

    private void Apply()
    {
        ProjectSyncIndicator indicator = Indicator ?? ProjectSyncIndicator.None;
        Visibility = indicator.IsShown ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = indicator.IsShown ? indicator.Label : null;
        AutomationProperties.SetName(this, indicator.Label);
        (string icon, string brush) = Look(indicator.Kind);
        if (icon.Length == 0)
        {
            ClearValue(GeometryProperty);
        }
        else
        {
            SetResourceReference(GeometryProperty, icon);
            SetResourceReference(ForegroundProperty, brush);
        }

        // The sync arrows turn while syncing, whatever Windows' animation effects setting (a choice made
        // for this app: the turning is what shows that a sync is running).
        if (indicator.Kind == ProjectSyncIndicatorKind.Syncing)
            _rotation.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
    }
}
