using System.Diagnostics;

using EntityTracker.Domain;

namespace EntityTracker.DemoData;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            ProgressDemoCommandLine commandLine = ProgressDemoCommandLine.Parse(args);
            if (commandLine.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            if (IsEntityTrackerRunning())
            {
                Console.Error.WriteLine(
                    "EntityTracker is running. Close the application before replacing its progress data.");
                return 2;
            }

            Console.WriteLine("Replacing current progress statuses and history with synthetic data.");
            Console.WriteLine("No backup will be created.");
            ProgressDemoOptions options = new(
                commandLine.Days,
                commandLine.Seed,
                DateOnly.FromDateTime(DateTime.Today),
                TimeZoneInfo.Local);
            ProgressDemoResult result = await new ProgressDemoSeeder().SeedAsync(
                commandLine.DatabasePath!,
                options);

            Console.WriteLine();
            Console.WriteLine($"Database: {result.DatabasePath}");
            Console.WriteLine($"History:  {result.StartDate:yyyy-MM-dd} through {result.EndDate:yyyy-MM-dd}");
            Console.WriteLine($"Seed:     {result.Seed}");
            Console.WriteLine($"Entities: {result.ActiveEntityCount} active");
            Console.WriteLine($"Events:   {result.TransitionCount} transitions, {result.SnapshotCount} snapshots");
            Console.WriteLine("Final statuses:");
            foreach (DevelopmentStatus status in Enum.GetValues<DevelopmentStatus>())
            {
                Console.WriteLine($"  {FormatStatus(status),-22} {result.FinalStatusCounts[status]}");
            }

            return 0;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            Console.Error.WriteLine();
            PrintUsage();
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Synthetic progress data could not be created: {exception.Message}");
            return 1;
        }
    }

    private static bool IsEntityTrackerRunning()
    {
        Process[] processes = Process.GetProcessesByName("EntityTracker.Wpf");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static string FormatStatus(DevelopmentStatus status) => status switch
    {
        DevelopmentStatus.NotStarted => "Not started",
        DevelopmentStatus.InProgress => "In progress",
        DevelopmentStatus.ReworkNeeded => "Rework needed",
        DevelopmentStatus.DevelopmentCompleted => "Dev. completed",
        DevelopmentStatus.Reconciled => "Reconciled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine(
            "  dotnet run --project tools/EntityTracker.DemoData -- " +
            "--database <path> --confirm-reset [--days 90] [--seed 12345]");
        Console.WriteLine();
        Console.WriteLine(
            "This replaces progress statuses and progress history in the selected database. " +
            "Entity identities, dependencies, notes, provenance, and archive state are preserved.");
    }
}
