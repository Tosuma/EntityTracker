using System.Globalization;
using System.Windows;

namespace EntityTracker.Screenshots;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        CultureInfo english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        CultureInfo.CurrentCulture = english;
        CultureInfo.CurrentUICulture = english;

        ScreenshotCommandLine commandLine;
        try
        {
            commandLine = ScreenshotCommandLine.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }

        if (commandLine.ShowHelp)
        {
            PrintUsage();
            return 0;
        }

        int exitCode = 1;
        System.Windows.Application application = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "/EntityTracker.Wpf;component/Themes/EntityTrackerTheme.xaml",
                UriKind.Relative)
        });
        application.Startup += async (_, _) =>
        {
            try
            {
                exitCode = await RunAsync(commandLine);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"README screenshots could not be generated: {exception}");
                exitCode = 1;
            }
            finally
            {
                application.Shutdown(exitCode);
            }
        };
        application.Run();
        return exitCode;
    }

    private static async Task<int> RunAsync(ScreenshotCommandLine commandLine)
    {
        string repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory);
        string destination = commandLine.UpdateReadme
            ? Path.Combine(repositoryRoot, "images")
            : Path.GetFullPath(commandLine.OutputDirectory ??
                Path.Combine(repositoryRoot, "artifacts", "readme-screenshots"));

        using ScreenshotWorkspace workspace = new();
        Console.WriteLine("Generating deterministic EntityTracker README screenshots...");
        Console.WriteLine($"Temporary data: {workspace.RootDirectory}");
        await new ReadmeScreenshotGenerator().GenerateAsync(repositoryRoot, workspace);
        ScreenshotPublisher.ValidateStagingDirectory(workspace.StagingDirectory);
        ScreenshotPublisher.Publish(workspace.StagingDirectory, destination);

        Console.WriteLine($"Generated {ScreenshotManifest.FileNames.Count} screenshots in:");
        Console.WriteLine(destination);
        if (!commandLine.UpdateReadme)
        {
            Console.WriteLine("Review them, then rerun with --update-readme to replace README images.");
        }

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots -- --output <directory>");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots -- --update-readme");
        Console.WriteLine();
        Console.WriteLine(
            "The tool uses a temporary SQLite database and never reads or modifies normal application data.");
    }
}
