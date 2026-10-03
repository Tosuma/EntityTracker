using System.Diagnostics;
using System.IO;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.Tests.Services;

/// <summary>
/// The updater must run the scripts of the release it installs, so updater fixes apply on the
/// update that ships them. These tests use real git repositories: a bare origin with a release tag
/// and a separate source checkout, as on a user's machine.
/// </summary>
public sealed class AppUpdaterStagingTests : IDisposable
{
    private const string Tag = "app-v1.1.0";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "entitytracker-updater-" + Guid.NewGuid().ToString("N"));
    private readonly string _origin;
    private readonly string _publisher;
    private readonly string _source;
    private readonly string _installedUpdater;
    private readonly List<string> _stagingFolders = [];

    public AppUpdaterStagingTests()
    {
        _origin = Path.Combine(_root, "origin.git");
        _publisher = Path.Combine(_root, "publisher");
        _source = Path.Combine(_root, "source");
        _installedUpdater = Path.Combine(_root, "installed", "Updater");
        Directory.CreateDirectory(_installedUpdater);
        foreach (string name in AppUpdateService.UpdaterScripts)
            File.WriteAllText(Path.Combine(_installedUpdater, name), $"# installed {name}");

        Git(_root, "init", "--bare", "--initial-branch=main", _origin);
        Git(_root, "clone", _origin, _publisher);
        Directory.CreateDirectory(Path.Combine(_publisher, "scripts"));
        foreach (string name in AppUpdateService.UpdaterScripts)
            File.WriteAllText(Path.Combine(_publisher, "scripts", name), $"# release {name}");
        Git(_publisher, "add", ".");
        Git(_publisher, "commit", "-m", "Release");
        Git(_publisher, "push", "origin", "HEAD:main");

        // The user's checkout predates the release tag, so the updater has to fetch it.
        Git(_root, "clone", _origin, _source);
        Git(_publisher, "tag", "-a", Tag, "-m", "Release");
        Git(_publisher, "push", "origin", Tag);
    }

    [Fact]
    public async Task UpdaterScriptsComeFromTheReleaseBeingInstalled()
    {
        UpdaterStaging staging = await PrepareAsync(_source, Tag);

        Assert.True(staging.FromRelease);
        Assert.All(AppUpdateService.UpdaterScripts, name =>
            Assert.Equal($"# release {name}", File.ReadAllText(Path.Combine(staging.Directory, name))));
    }

    [Fact]
    public async Task UpdaterIsToldWhichVersionOfItselfIsRunning()
    {
        AppUpdateService service = CreateService(_source);
        UpdaterStaging fromRelease = await PrepareAsync(_source, Tag);
        UpdaterStaging installed = await PrepareAsync(_source, "app-v9.9.9");

        IReadOnlyList<string> releaseArguments = service.UpdaterArguments(fromRelease, Tag, 42);
        IReadOnlyList<string> fallbackArguments = service.UpdaterArguments(installed, "app-v9.9.9", 42);

        Assert.Equal(Tag, ValueAfter(releaseArguments, "-UpdaterVersion"));
        Assert.Equal("Release", ValueAfter(releaseArguments, "-UpdaterSource"));
        Assert.Equal(Tag, ValueAfter(releaseArguments, "-Tag"));
        Assert.Equal(Path.Combine(fromRelease.Directory, "Update-EntityTracker.ps1"), ValueAfter(releaseArguments, "-File"));
        Assert.Equal("app-v1.0.0", ValueAfter(fallbackArguments, "-UpdaterVersion"));
        Assert.Equal("Installed", ValueAfter(fallbackArguments, "-UpdaterSource"));

        static string ValueAfter(IReadOnlyList<string> arguments, string name) =>
            arguments[arguments.ToList().IndexOf(name) + 1];
    }

    [Fact]
    public async Task MissingReleaseTagFallsBackToTheInstalledUpdater()
    {
        UpdaterStaging staging = await PrepareAsync(_source, "app-v9.9.9");

        AssertInstalledScripts(staging);
    }

    [Fact]
    public async Task LocalTagThatDiffersFromOriginIsNotTrusted()
    {
        File.WriteAllText(Path.Combine(_source, "local.txt"), "local");
        Git(_source, "add", ".");
        Git(_source, "commit", "-m", "Local");
        Git(_source, "tag", Tag);

        UpdaterStaging staging = await PrepareAsync(_source, Tag);

        AssertInstalledScripts(staging);
    }

    [Fact]
    public async Task UnreadableSourceCheckoutFallsBackToTheInstalledUpdater()
    {
        string notARepository = Path.Combine(_root, "not-a-repository");
        Directory.CreateDirectory(notARepository);

        UpdaterStaging staging = await PrepareAsync(notARepository, Tag);

        AssertInstalledScripts(staging);
    }

    public void Dispose()
    {
        foreach (string folder in _stagingFolders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);
        foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, recursive: true);
    }

    private async Task<UpdaterStaging> PrepareAsync(string sourcePath, string tag)
    {
        UpdaterStaging staging = await CreateService(sourcePath).PrepareUpdaterAsync(tag, CancellationToken.None);
        _stagingFolders.Add(staging.Directory);
        return staging;
    }

    private AppUpdateService CreateService(string sourcePath)
    {
        ApplicationDataPaths paths = new(_root, "", "", "", "");
        return new AppUpdateService(paths, new AppInstallManifest(sourcePath, "app-v1.0.0"), query: null,
            installedUpdaterDirectory: _installedUpdater);
    }

    private static void AssertInstalledScripts(UpdaterStaging staging)
    {
        Assert.False(staging.FromRelease);
        Assert.All(AppUpdateService.UpdaterScripts, name =>
            Assert.Equal($"# installed {name}", File.ReadAllText(Path.Combine(staging.Directory, name))));
    }

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
