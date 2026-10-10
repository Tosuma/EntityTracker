using SkiaSharp;

namespace EntityTracker.Reporting;

internal static class ProgressChartPalette
{
    internal static readonly SKColor DarkGreen = new(0x14, 0x1E, 0x1E);
    internal static readonly SKColor Green100 = new(0x12, 0x38, 0x36);
    internal static readonly SKColor Green80 = new(0x41, 0x60, 0x5E);
    internal static readonly SKColor Green60 = new(0x71, 0x88, 0x86);
    internal static readonly SKColor Green40 = new(0xA0, 0xAF, 0xAF);
    internal static readonly SKColor Green20 = new(0xD0, 0xD7, 0xD7);
    internal static readonly SKColor White = new(0xFF, 0xFF, 0xFF);
    internal static readonly SKColor Coral = new(0xFF, 0x63, 0x59);

    // The status extension, as in the app's palette.
    internal static readonly SKColor InProgressBlue = new(0x3D, 0x6A, 0x8A);
    internal static readonly SKColor ReworkingLavender = new(0xB5, 0x8B, 0xD0);
    internal static readonly SKColor BlockedBrick = new(0x9E, 0x2B, 0x25);
    internal static readonly SKColor CompletedGreen = new(0xA8, 0xD5, 0xA2);
    internal static readonly SKColor WaitingAmber = new(0xD9, 0x92, 0x2E);
}
