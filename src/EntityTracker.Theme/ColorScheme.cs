namespace EntityTracker.Theme;

/// <summary>
/// Every colour in EntityTracker: the app, its charts and the exported Project report. This is the only
/// place a colour is defined. <see cref="Base"/> names each colour once; every other section assigns a
/// base colour to one part of the application. To recolour something, change its assignment below
/// (or a base value, to change everything that uses it) and rebuild.
/// </summary>
/// <remarks>
/// The app's text, page, card, border and focus colours are not here: they follow the Windows light or
/// dark theme (Fluent) and change with it.
/// </remarks>
public static class ColorScheme
{
    /// <summary>The base colours. Every assignment below is one of these.</summary>
    public static class Base
    {
        // Brand
        public const string DarkGreen = "#141E1E";
        public const string Green100 = "#123836";
        public const string Green90 = "#2A4C4A";
        public const string Green80 = "#41605E";
        public const string Green60 = "#718886";
        public const string Green40 = "#A0AFAF";
        public const string Green30 = "#B8C3C3";
        public const string Green20 = "#D0D7D7";
        public const string Green10 = "#E7EBEB";
        public const string White = "#FFFFFF";
        public const string Black = "#000000";
        public const string Coral = "#FF6359";
        public const string CoralHover = "#FF786F";

        // Supporting colours
        public const string Amber = "#D9922E";
        public const string PaleAmber = "#F6D9A8";
        public const string SelectionAmber = "#C8821E";
        public const string DeepCoral = "#C2382F";
        public const string Blush = "#FFE4E1";
        public const string Slate = "#55615F";
        public const string Mist = "#F6F8F8";
        public const string Snow = "#FAFBFB";
        public const string Frost = "#FBFCFC";
        public const string NotificationBlue = "#A6DCEB";
        public const string NotificationGreen = "#96E4AD";
        public const string NotificationRed = "#FF9C94";
        public const string NotificationYellow = "#FFD188";
    }

    /// <summary>
    /// Development statuses: badges, Overview cards, the "% implemented" bar, the dependency graph, the
    /// progress charts and the exported report.
    /// </summary>
    public static class Status
    {
        public static readonly StatusColor NotStarted = new(Base.Green40, Base.DarkGreen);
        public static readonly StatusColor InProgress = new(Base.Green80, Base.White);
        public static readonly StatusColor ReworkNeeded = new(Base.Coral, Base.DarkGreen);
        public static readonly StatusColor Reworking = new(Base.Green80, Base.White);
        public static readonly StatusColor Blocked = new(Base.Coral, Base.DarkGreen);
        public static readonly StatusColor DevelopmentCompleted = new(Base.Green60, Base.DarkGreen);
        public static readonly StatusColor Reconciled = new(Base.Green100, Base.White);
    }

    /// <summary>Work statuses in the app: the Overview's and the dashboard's work status badges.</summary>
    public static class WorkStatus
    {
        public static readonly StatusColor Ready = new(Base.Green80, Base.White);
        public static readonly StatusColor WaitingOnDependencies = new(Base.Coral, Base.DarkGreen);
        public static readonly StatusColor Blocked = new(Base.Coral, Base.DarkGreen);
        public static readonly StatusColor InProgress = new(Base.Green80, Base.White);
        public static readonly StatusColor Completed = new(Base.Green60, Base.DarkGreen);
        public static readonly StatusColor Reconciled = new(Base.Green100, Base.White);
    }

    /// <summary>Work statuses in the exported report's tables, which use a lighter Ready and Waiting.</summary>
    public static class ReportWorkStatus
    {
        public static readonly StatusColor Ready = new(Base.Green20, Base.DarkGreen);
        public static readonly StatusColor WaitingOnDependencies = new(Base.PaleAmber, Base.DarkGreen);
        public static readonly StatusColor Blocked = new(Base.Coral, Base.DarkGreen);
        public static readonly StatusColor InProgress = new(Base.Green80, Base.White);
        // White on Green 60% is below 4.5:1 contrast; kept as the report has always shown it.
        public static readonly StatusColor Completed = new(Base.Green60, Base.White);
        public static readonly StatusColor Reconciled = new(Base.Green100, Base.White);
    }

    /// <summary>The rest of the app. Each entry becomes the brush "Brush.&lt;path&gt;", e.g. Brush.Surface.Primary.</summary>
    public static class App
    {
        public const string Accent = Base.Coral;

        public static class Text
        {
            public const string OnDark = Base.White;
            public const string OnDarkMuted = Base.Green10;
        }

        public static class Surface
        {
            public const string Primary = Base.Green100;
            public static readonly Tint Selected = new(Base.Green60, 0.30);
        }

        /// <summary>The scrim behind dialogs; Strong for focused catalog dialogs.</summary>
        public static class Overlay
        {
            public static readonly Tint Default = new(Base.DarkGreen, 0.60);
            public static readonly Tint Strong = new(Base.DarkGreen, 0.84);
        }

        public static class Notification
        {
            public const string Information = Base.NotificationBlue;
            public const string Success = Base.NotificationGreen;
            public const string Failure = Base.NotificationRed;
            public const string Action = Base.NotificationYellow;
        }

        /// <summary>Schema synchronization review: possibly removed entities, and retained warnings.</summary>
        public static class Review
        {
            public static class Missing
            {
                public const string Header = Base.Coral;
                public static readonly Tint Surface = new(Base.Coral, 0.10);
                public const string Border = Base.Coral;
            }

            public static class Warning
            {
                public const string Header = Base.Amber;
                public static readonly string Surface = WithAlpha(Base.Amber, 0x40);
                public const string Border = Base.Amber;
            }
        }

        public static class Graph
        {
            /// <summary>Selected entities in the dependency graph.</summary>
            public const string Selection = Base.SelectionAmber;

            /// <summary>Text on a status colour in the tree view: dark on light colours, light on dark.</summary>
            public const string LabelOnLight = Base.Black;
            public const string LabelOnDark = Base.White;
        }

        public static class Report
        {
            /// <summary>The light canvas behind the app's progress charts.</summary>
            public const string Canvas = Base.White;
        }
    }

    /// <summary>Progress charts: in the app, saved as images, and in the exported report.</summary>
    public static class Chart
    {
        public const string Canvas = Base.White;
        public const string Text = Base.DarkGreen;
        public const string Grid = Base.Green20;
        public const string SliceBorder = Base.White;
        public const string Entities = Base.Green80;
        public const string Implemented = Base.Green100;
        public const string ReadyTrend = Base.Green80;
        public const string WaitingTrend = Base.Coral;
        public const string Increase = Base.Green80;
        public const string Decrease = Base.Coral;
        public const string NetChange = Base.Green60;
        public const string UnknownStatus = Base.Green40;
    }

    /// <summary>
    /// The exported report's page. Each entry is the CSS variable "--&lt;kebab-name&gt;" in report.css, as are
    /// the base colours (for example --green-100).
    /// </summary>
    public static class Report
    {
        public const string Ink = Base.DarkGreen;
        public const string Muted = Base.Slate;
        public const string Surface = Base.White;
        public const string Page = Base.Mist;
        public const string Border = Base.Green20;
        public const string Focus = Base.SelectionAmber;
        public const string OnDark = Base.White;
        public const string HeaderMeta = Base.Green20;
        public const string Attention = Base.DeepCoral;
        public const string RowStripe = Base.Snow;
        public const string Highlight = Base.PaleAmber;
        public const string GraphCanvas = Base.Frost;
        public const string MissingBand = Base.Blush;
        public const string MissingSwatch = Base.White;
    }

    /// <summary>Gets "#AARRGGBB": the colour with the given opacity built in.</summary>
    public static string WithAlpha(string color, byte alpha) =>
        $"#{alpha:X2}{color.TrimStart('#')}";
}

/// <summary>A status's background colour and the colour of the text on it.</summary>
public readonly record struct StatusColor(string Fill, string Text);

/// <summary>A colour drawn see-through, at the given opacity (0–1).</summary>
public readonly record struct Tint(string Color, double Opacity);
