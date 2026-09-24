using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;

using EntityTracker.Infrastructure.Configuration;
using EntityTracker.Wpf.Services;

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

        if (commandLine.Appearance is null)
        {
            return GenerateAllAppearances(commandLine);
        }

        return GenerateAppearance(commandLine);
    }

    private static int GenerateAllAppearances(ScreenshotCommandLine commandLine)
    {
        Console.WriteLine(
            "Generating deterministic EntityTracker screenshots in Dark and Light modes...");
        foreach (ApplicationAppearance appearance in ScreenshotManifest.Appearances)
        {
            using Process process = StartAppearanceProcess(commandLine, appearance);
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return process.ExitCode;
            }
        }

        if (!commandLine.UpdateReadme)
        {
            Console.WriteLine(
                "Review both appearance directories, then rerun with --update-readme to replace README images.");
        }

        return 0;
    }

    private static Process StartAppearanceProcess(
        ScreenshotCommandLine commandLine,
        ApplicationAppearance appearance)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("The screenshot process path is unavailable.");
        ProcessStartInfo startInfo = new(processPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory
        };
        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        }

        if (commandLine.UpdateReadme)
        {
            startInfo.ArgumentList.Add("--update-readme");
        }
        else if (commandLine.OutputDirectory is not null)
        {
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(commandLine.OutputDirectory);
        }

        startInfo.ArgumentList.Add("--appearance");
        startInfo.ArgumentList.Add(
            ScreenshotManifest.GetAppearanceDirectoryName(appearance));
        return Process.Start(startInfo) ??
            throw new InvalidOperationException(
                $"The {appearance} screenshot process could not be started.");
    }

    private static int GenerateAppearance(ScreenshotCommandLine commandLine)
    {
        ApplicationAppearance appearance = commandLine.Appearance ??
            throw new InvalidOperationException("An appearance is required.");

        int exitCode = 1;
        System.Windows.Application application = new()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown
        };
#pragma warning disable WPF0001
        application.ThemeMode = appearance == ApplicationAppearance.Dark
            ? ThemeMode.Light
            : ThemeMode.Dark;
#pragma warning restore WPF0001
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
                exitCode = await RunAsync(commandLine, appearance);
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

    private static async Task<int> RunAsync(
        ScreenshotCommandLine commandLine,
        ApplicationAppearance appearance)
    {
        string repositoryRoot = RepositoryLocator.FindRoot(Environment.CurrentDirectory);
        string destinationRoot = commandLine.UpdateReadme
            ? Path.Combine(repositoryRoot, "images")
            : Path.GetFullPath(commandLine.OutputDirectory ??
                Path.Combine(repositoryRoot, "artifacts", "readme-screenshots"));

        using ScreenshotWorkspace workspace = new();
        string appearanceDirectory = ScreenshotManifest.GetAppearanceDirectoryName(appearance);
        string destination = Path.Combine(destinationRoot, appearanceDirectory);

        Console.WriteLine($"Generating {appearance} screenshots...");
        Console.WriteLine($"Temporary data: {workspace.RootDirectory}");
        new ApplicationThemeService().Apply(appearance);
        await new ReadmeScreenshotGenerator().GenerateAsync(
            repositoryRoot,
            workspace,
            appearance);
        ScreenshotPublisher.ValidateStagingDirectory(workspace.StagingDirectory);
        ScreenshotPublisher.Publish(workspace.StagingDirectory, destination);
        Console.WriteLine(
            $"Generated {ScreenshotManifest.FileNames.Count} {appearance} screenshots in:");
        Console.WriteLine(destination);

        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots -- --output <directory>");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots -- --update-readme");
        Console.WriteLine("  dotnet run --project tools/EntityTracker.Screenshots -- --appearance <light|dark>");
        Console.WriteLine();
        Console.WriteLine(
            "The tool generates complete images/dark and images/light sets using temporary SQLite databases and never reads or modifies normal application data.");
    }
}
