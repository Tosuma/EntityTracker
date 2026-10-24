using Microsoft.Extensions.Logging;

namespace EntityTracker.Wpf.Services;

/// <summary>Works out the level the app logs at.</summary>
public static class LoggingLevels
{
    /// <summary>The environment variable that overrides the setting, for support sessions.</summary>
    public const string OverrideVariable = "ENTITYTRACKER_LOG_LEVEL";

    /// <summary>
    /// Gets Debug when detailed logging is on and Information otherwise, unless
    /// <paramref name="overrideValue"/> names a level (Trace, Debug, Information, Warning, Error, Critical).
    /// </summary>
    public static LogLevel Resolve(bool detailedLogging, string? overrideValue = null) =>
        TryParseOverride(overrideValue, out LogLevel level)
            ? level
            : detailedLogging ? LogLevel.Debug : LogLevel.Information;

    /// <summary>Gets whether the value names a log level by name (numbers and None don't count).</summary>
    public static bool TryParseOverride(string? value, out LogLevel level) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out level) &&
        Enum.IsDefined(level) && level != LogLevel.None && !int.TryParse(value, out _);

    public static string? ReadOverride() => Environment.GetEnvironmentVariable(OverrideVariable);

    /// <summary>
    /// Says how the app logs, for the log itself: "detailed logging on (Debug)", "detailed logging off
    /// (Information)", or the level an override sets.
    /// </summary>
    public static string Describe(bool detailedLogging, string? overrideValue = null) =>
        TryParseOverride(overrideValue, out LogLevel level)
            ? $"log level {level}, set by {OverrideVariable}"
            : detailedLogging ? "detailed logging on (Debug)" : "detailed logging off (Information)";
}
