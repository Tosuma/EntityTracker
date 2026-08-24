namespace EntityTracker.Screenshots;

internal sealed record ScreenshotCommandLine(
    string? OutputDirectory,
    bool UpdateReadme,
    bool ShowHelp)
{
    internal static ScreenshotCommandLine Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? outputDirectory = null;
        bool updateReadme = false;
        bool showHelp = false;
        for (int index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--output":
                    if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
                    {
                        throw new ArgumentException("The --output argument requires a directory.");
                    }

                    outputDirectory = arguments[index];
                    break;
                case "--update-readme":
                    updateReadme = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arguments[index]}'.");
            }
        }

        if (updateReadme && outputDirectory is not null)
        {
            throw new ArgumentException("Use either --output or --update-readme, not both.");
        }

        return new ScreenshotCommandLine(outputDirectory, updateReadme, showHelp);
    }
}
