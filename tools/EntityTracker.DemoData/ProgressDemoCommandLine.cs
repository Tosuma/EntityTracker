namespace EntityTracker.DemoData;

internal sealed record ProgressDemoCommandLine(
    string? DatabasePath,
    int Days,
    int Seed,
    bool ConfirmReset,
    bool ShowHelp)
{
    public static ProgressDemoCommandLine Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string? databasePath = null;
        int days = 90;
        int seed = 12345;
        bool confirmReset = false;
        bool showHelp = false;

        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            switch (argument)
            {
                case "--database":
                    databasePath = ReadValue(arguments, ref index, argument);
                    break;
                case "--days":
                    days = ParseInt(ReadValue(arguments, ref index, argument), argument);
                    break;
                case "--seed":
                    seed = ParseInt(ReadValue(arguments, ref index, argument), argument);
                    break;
                case "--confirm-reset":
                    confirmReset = true;
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{argument}'.");
            }
        }

        if (!showHelp)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("The --database argument is required.");
            }

            if (!confirmReset)
            {
                throw new ArgumentException(
                    "The --confirm-reset flag is required because existing progress history will be replaced.");
            }

            if (days < 7)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(arguments),
                    "The --days value must be at least 7.");
            }
        }

        return new ProgressDemoCommandLine(
            databasePath,
            days,
            seed,
            confirmReset,
            showHelp);
    }

    private static string ReadValue(
        IReadOnlyList<string> arguments,
        ref int index,
        string argument)
    {
        if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
        {
            throw new ArgumentException($"The {argument} argument requires a value.");
        }

        return arguments[index];
    }

    private static int ParseInt(string value, string argument)
    {
        if (!int.TryParse(value, out int result))
        {
            throw new ArgumentException($"The {argument} value must be a whole number.");
        }

        return result;
    }
}
