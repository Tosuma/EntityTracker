using System.Diagnostics;
using System.IO;

using EntityTracker.Wpf.Services;
using EntityTracker.Wpf.ViewModels;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>
/// What's new comes from the release tags' messages: before an update it lists what the update brings, after
/// one what changed since the version used before.
/// </summary>
public sealed class ReleaseNotesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "entitytracker-notes-" + Guid.NewGuid().ToString("N"));

    public ReleaseNotesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ParsingReadsEachReleaseNewestFirstAndSkipsOtherTags()
    {
        string output =
            "app-v4.1.0\0" + "2026-10-24\0" + "The Overview cards changed.\r\nSecond paragraph.\n\x1e\n" +
            "app-v4.2.0\0" + "2026-10-25\0" + "Implemented now shows what it counts: four parts.\n" +
            "-----BEGIN PGP SIGNATURE-----\nabc\n-----END PGP SIGNATURE-----\n\x1e\n" +
            "v1.0\0" + "2026-01-01\0" + "Not an app release.\x1e\n" +
            "app-v4.0.0\0" + "2026-10-23\0" + "   \x1e\n";

        IReadOnlyList<ReleaseNote> notes = ReleaseNotes.Parse(output);

        Assert.Equal(["app-v4.2.0", "app-v4.1.0"], notes.Select(static note => note.Version.ToString()));
        Assert.Equal("Implemented now shows what it counts: four parts.", notes[0].Text);
        Assert.Equal("The Overview cards changed.\nSecond paragraph.", notes[1].Text);
        Assert.Equal("4.2.0 · 25 Oct 2026", notes[0].Heading);
        Assert.Equal("Implemented now shows what it counts", notes[0].Headline);
        Assert.Equal("The Overview cards changed", notes[1].Headline);
    }

    [Fact]
    public void TheRangeIncludesSkippedVersionsButNotTheInstalledOne()
    {
        string[] tags = ["app-v3.9.0", "app-v4.0.0", "app-v4.1.0", "app-v4.2.0", "app-v4.3.0"];
        ReleaseNote[] notes = tags.Select(Note).ToArray();

        Assert.Equal(["app-v4.2.0", "app-v4.1.0"],
            ReleaseNotes.InRange(notes, Version("app-v4.0.0"), Version("app-v4.2.0")).Select(static note => note.Text));
        Assert.Equal(5, ReleaseNotes.InRange(notes, null, Version("app-v4.3.0")).Count);
    }

    [Fact]
    public void WhatsNewHeadsTheListByWhatItShows()
    {
        ReleaseNote[] notes = [Note("app-v4.2.0"), Note("app-v4.1.0")];
        WhatsNewViewModel whatsNew = new();
        Assert.False(whatsNew.HasEntries);

        whatsNew.ShowComing(Version("app-v4.2.0"), notes);
        Assert.Equal("What's new in 4.2.0", whatsNew.Heading);
        Assert.Equal(2, whatsNew.Entries.Count);

        whatsNew.ShowSince(Version("app-v4.0.0"), notes);
        Assert.Equal("What's new since 4.0.0", whatsNew.Heading);

        whatsNew.ShowInstalled(Version("app-v4.1.0"), notes);
        Assert.Equal("What's new in 4.1.0", whatsNew.Heading);
        Assert.Equal("app-v4.1.0", Assert.Single(whatsNew.Entries).Text);
    }

    [Fact]
    public void OnlyARealUpdateHasAPreviousVersion()
    {
        // A fresh install has none, the same version again has itself, and after an update the older one.
        Assert.Null(Service("app-v4.1.0").TakePreviousRunVersion());
        Assert.Equal("app-v4.1.0", Service("app-v4.1.0").TakePreviousRunVersion()?.ToString());
        Assert.Equal("app-v4.1.0", Service("app-v4.2.0").TakePreviousRunVersion()?.ToString());
        Assert.Equal("app-v4.2.0", Service("app-v4.2.0").TakePreviousRunVersion()?.ToString());
    }

    [Fact]
    public async Task NotesThatCannotBeReadAreAnEmptyListNotAnError()
    {
        AppUpdateService service = Service("app-v4.1.0", notes: _ => throw new IOException("offline"));

        Assert.Empty(await service.ReadReleaseNotesAsync(null, Version("app-v4.2.0"), fetch: true));
    }

    [Fact]
    public async Task NewerReleaseTagsAreFetchedForTheirNotes()
    {
        string origin = Path.Combine(_root, "origin.git");
        string publisher = Path.Combine(_root, "publisher");
        string source = Path.Combine(_root, "source");
        Git(_root, "init", "--bare", "--initial-branch=main", origin);
        Git(_root, "clone", origin, publisher);
        File.WriteAllText(Path.Combine(publisher, "README.md"), "EntityTracker");
        Git(publisher, "add", ".");
        Git(publisher, "commit", "-m", "Release");
        Git(publisher, "tag", "-a", "app-v1.0.0", "-m", "The first release.");
        Git(publisher, "push", "origin", "HEAD:main", "app-v1.0.0");
        Git(_root, "clone", origin, source);
        // Released after this install was made: its clone does not have them yet.
        Git(publisher, "tag", "-a", "app-v1.1.0", "-m", "Second: a new card.");
        Git(publisher, "tag", "-a", "app-v1.2.0", "-m", "Third: a faster sync.\n\nWith a second paragraph.");
        Git(publisher, "push", "origin", "app-v1.1.0", "app-v1.2.0");

        AppUpdateService service = new(new ApplicationDataPaths(_root, "", "", "", ""),
            new AppInstallManifest(source, "app-v1.0.0"), query: null);

        IReadOnlyList<ReleaseNote> notes = await service.ReadReleaseNotesAsync(
            Version("app-v1.0.0"), Version("app-v1.2.0"), fetch: true);

        Assert.Equal(["app-v1.2.0", "app-v1.1.0"], notes.Select(static note => note.Version.ToString()));
        Assert.Equal("Third: a faster sync.\n\nWith a second paragraph.", notes[0].Text);
        Assert.Equal("Second: a new card.", notes[1].Text);
    }

    private AppUpdateService Service(string installed, Func<CancellationToken, Task<string>>? notes = null) =>
        new(new ApplicationDataPaths(_root, "", "", "", ""), new AppInstallManifest(_root, installed),
            query: null, notes: notes);

    private static AppReleaseVersion Version(string tag) =>
        AppReleaseVersion.TryParse(tag, out AppReleaseVersion version) ? version : throw new ArgumentException(tag);

    private static ReleaseNote Note(string tag) => new(Version(tag), new DateOnly(2026, 10, 25), tag);

    private static void Git(string workingDirectory, params string[] arguments)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string setting in new[] { "user.name=Test", "user.email=test@example.com", "commit.gpgsign=false", "tag.gpgsign=false" })
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }

        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string errors = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {errors}");
    }
}
