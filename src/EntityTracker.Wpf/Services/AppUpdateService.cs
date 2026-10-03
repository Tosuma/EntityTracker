using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Wpf.Services;

public enum AppUpdateState { Development, Checking, Current, Offline, Required }

public sealed record AppInstallManifest(string SourcePath, string Version);

/// <summary>Where the updater scripts for an update were staged, and whether they came from the release.</summary>
public sealed record UpdaterStaging(string Directory, bool FromRelease);

public readonly record struct AppReleaseVersion(int Major, int Minor, int Patch)
    : IComparable<AppReleaseVersion>
{
    public static bool TryParse(string tag, out AppReleaseVersion version)
    {
        Match match = Regex.Match(tag, @"^app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int major) &&
            int.TryParse(match.Groups[2].Value, out int minor) &&
            int.TryParse(match.Groups[3].Value, out int patch))
        {
            version = new(major, minor, patch);
            return true;
        }
        version = default;
        return false;
    }

    public int CompareTo(AppReleaseVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) :
        Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

    public override string ToString() => $"app-v{Major}.{Minor}.{Patch}";
}

/// <summary>Checks approved app tags using the user's existing source checkout.</summary>
public sealed class AppUpdateService : IProjectSyncVersionGate, IDisposable
{
    private readonly AppInstallManifest? _install;
    private readonly string _requiredPath;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, Task<string>>? _queryOverride;
    private readonly string _installedUpdaterDirectory;

    /// <summary>The scripts that perform an update; they live in <c>scripts/</c> of every release.</summary>
    internal static readonly IReadOnlyList<string> UpdaterScripts =
        ["Update-EntityTracker.ps1", "Install-EntityTracker.ps1", "Install-Common.ps1"];
    private Task? _loop;
    private AppReleaseVersion _installedVersion;
    private string? _requiredTag;
    private volatile AppUpdateState _state;

    public AppUpdateService(ApplicationDataPaths dataPaths, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _requiredPath = Path.Combine(dataPaths.RootDirectory, "required-app-update.txt");
        _installedUpdaterDirectory = Path.Combine(AppContext.BaseDirectory, "Updater");
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "app-install.json");
        if (!File.Exists(manifestPath))
        {
            _state = AppUpdateState.Development;
            return;
        }

        _install = JsonSerializer.Deserialize<AppInstallManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("The app install manifest is empty.");
        if (!AppReleaseVersion.TryParse(_install.Version, out _installedVersion) ||
            string.IsNullOrWhiteSpace(_install.SourcePath))
            throw new InvalidDataException("The app install manifest has an invalid version or source path.");
        Version? binaryVersion = typeof(App).Assembly.GetName().Version;
        if (binaryVersion is null || binaryVersion.Major != _installedVersion.Major ||
            binaryVersion.Minor != _installedVersion.Minor ||
            binaryVersion.Build != _installedVersion.Patch)
            throw new InvalidDataException("The installed app version does not match its manifest.");
        _state = AppUpdateState.Checking;
        if (File.Exists(_requiredPath) &&
            AppReleaseVersion.TryParse(File.ReadAllText(_requiredPath).Trim(), out var required) &&
            required.CompareTo(_installedVersion) > 0)
        {
            _requiredTag = required.ToString();
            _state = AppUpdateState.Required;
        }
    }

    internal AppUpdateService(ApplicationDataPaths dataPaths, AppInstallManifest install,
        Func<CancellationToken, Task<string>>? query, string? installedUpdaterDirectory = null)
    {
        _clock = TimeProvider.System;
        _requiredPath = Path.Combine(dataPaths.RootDirectory, "required-app-update.txt");
        _install = install;
        _queryOverride = query;
        _installedUpdaterDirectory = installedUpdaterDirectory ?? Path.Combine(AppContext.BaseDirectory, "Updater");
        if (!AppReleaseVersion.TryParse(install.Version, out _installedVersion))
            throw new InvalidDataException("Invalid test app release version.");
        _state = AppUpdateState.Checking;
        if (File.Exists(_requiredPath) &&
            AppReleaseVersion.TryParse(File.ReadAllText(_requiredPath).Trim(), out var required) &&
            required.CompareTo(_installedVersion) > 0)
        {
            _requiredTag = required.ToString();
            _state = AppUpdateState.Required;
        }
    }

    public event EventHandler? StateChanged;
    public AppUpdateState State => _state;
    public string? RequiredTag => Volatile.Read(ref _requiredTag);
    public bool IsRequired => State == AppUpdateState.Required;
    public bool IsManagedInstall => _install is not null;

    public void Start()
    {
        if (_install is null || _loop is not null) return;
        _loop = Task.Run(async () =>
        {
            while (!_lifetime.IsCancellationRequested)
            {
                try { await CheckAsync(_lifetime.Token); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
                try { await Task.Delay(TimeSpan.FromMinutes(15), _clock, _lifetime.Token); }
                catch (OperationCanceledException) { break; }
            }
        });
    }

    public async Task EnsureSyncAllowedAsync(CancellationToken cancellationToken)
    {
        if (_install is null) return;
        if (IsRequired) throw new ProjectSyncUpdateRequiredException(RequiredTag!);
        await CheckAsync(cancellationToken);
        if (IsRequired) throw new ProjectSyncUpdateRequiredException(RequiredTag!);
        if (State != AppUpdateState.Current)
            throw new ProjectSyncVersionCheckUnavailableException();
    }

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_install is null || IsRequired) return;
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRequired) return;
            using CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            try
            {
                string output;
                if (_queryOverride is not null)
                {
                    output = await _queryOverride(timeout.Token);
                }
                else
                {
                    (int exitCode, byte[] result) = await RunGitAsync(
                        ["ls-remote", "--refs", "--tags", "origin", "app-v*"], timeout.Token);
                    if (exitCode != 0) throw new IOException("Release check failed.");
                    output = System.Text.Encoding.UTF8.GetString(result);
                }
                AppReleaseVersion? newest = FindLatest(output);
                if (newest is null) throw new InvalidDataException("No approved app release was found.");
                if (newest is { } release && release.CompareTo(_installedVersion) > 0)
                {
                    Volatile.Write(ref _requiredTag, release.ToString());
                    ChangeState(AppUpdateState.Required);
                    File.WriteAllText(_requiredPath, release.ToString());
                }
                else
                {
                    if (File.Exists(_requiredPath)) File.Delete(_requiredPath);
                    ChangeState(AppUpdateState.Current);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
            {
                if (!IsRequired) ChangeState(AppUpdateState.Offline);
            }
        }
        finally { _checkLock.Release(); }
    }

    public static AppReleaseVersion? FindLatest(string lsRemoteOutput)
    {
        AppReleaseVersion? latest = null;
        foreach (string line in lsRemoteOutput.Split('\n'))
        {
            string[] parts = line.Trim().Split('\t');
            if (parts.Length != 2 || !parts[1].StartsWith("refs/tags/", StringComparison.Ordinal)) continue;
            if (AppReleaseVersion.TryParse(parts[1]["refs/tags/".Length..], out var version) &&
                (latest is null || version.CompareTo(latest.Value) > 0)) latest = version;
        }
        return latest;
    }

    /// <summary>
    /// Starts the updater for the required release. The updater scripts are taken from that release
    /// itself, so fixes to the updater take effect on the very update that ships them; if the
    /// release cannot be read, the scripts installed with this version are used instead.
    /// </summary>
    public async Task LaunchUpdaterAsync(int appProcessId, CancellationToken cancellationToken = default)
    {
        if (_install is null || RequiredTag is not { } tag)
            throw new InvalidOperationException("No update is required.");
        UpdaterStaging staging = await PrepareUpdaterAsync(tag, cancellationToken);
        ProcessStartInfo start = new("powershell.exe")
        {
            UseShellExecute = true,
            WorkingDirectory = staging.Directory,
            WindowStyle = ProcessWindowStyle.Normal
        };
        foreach (string argument in UpdaterArguments(staging, tag, appProcessId))
            start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new InvalidOperationException("The updater could not start.");
    }

    /// <summary>
    /// Builds the updater command line. It names the updater's own version, which is the release
    /// being installed or, after a fallback, the version installed now, so the updater window and
    /// install log show which updater ran.
    /// </summary>
    internal IReadOnlyList<string> UpdaterArguments(UpdaterStaging staging, string tag, int appProcessId)
    {
        if (_install is null) throw new InvalidOperationException("This is not a managed install.");
        return
        [
            "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass",
            "-File", Path.Combine(staging.Directory, "Update-EntityTracker.ps1"),
            "-SourcePath", _install.SourcePath,
            "-Tag", tag,
            "-WaitForProcess", appProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-UpdaterVersion", staging.FromRelease ? tag : _install.Version,
            "-UpdaterSource", staging.FromRelease ? "Release" : "Installed"
        ];
    }

    /// <summary>Copies the updater scripts for <paramref name="tag"/> into a fresh temporary folder.</summary>
    internal async Task<UpdaterStaging> PrepareUpdaterAsync(string tag, CancellationToken cancellationToken)
    {
        if (_install is null) throw new InvalidOperationException("This is not a managed install.");
        string staging = Path.Combine(Path.GetTempPath(), "EntityTracker-Updater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        if (await TryStageReleaseScriptsAsync(tag, staging, cancellationToken))
            return new UpdaterStaging(staging, FromRelease: true);

        foreach (string name in UpdaterScripts)
            File.Copy(Path.Combine(_installedUpdaterDirectory, name), Path.Combine(staging, name), overwrite: true);
        return new UpdaterStaging(staging, FromRelease: false);
    }

    /// <summary>
    /// Reads the updater scripts from the release tag. The tag is verified the same way the installer
    /// verifies it before building: it must exist on origin, and the local tag must match origin's.
    /// </summary>
    private async Task<bool> TryStageReleaseScriptsAsync(string tag, string staging, CancellationToken cancellationToken)
    {
        if (!AppReleaseVersion.TryParse(tag, out _)) return false;
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            (int lsExit, byte[] lsOutput) = await RunGitAsync(
                ["ls-remote", "--refs", "--tags", "origin", $"refs/tags/{tag}"], timeout.Token);
            string[] remote = System.Text.Encoding.UTF8.GetString(lsOutput)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lsExit != 0 || remote.Length != 1) return false;
            string remoteHash = remote[0].Split('\t')[0];

            (int fetchExit, _) = await RunGitAsync(
                ["fetch", "--no-tags", "origin", $"refs/tags/{tag}:refs/tags/{tag}"], timeout.Token);
            (int revExit, byte[] revOutput) = await RunGitAsync(["rev-parse", $"refs/tags/{tag}"], timeout.Token);
            if (fetchExit != 0 || revExit != 0 ||
                !string.Equals(System.Text.Encoding.UTF8.GetString(revOutput).Trim(), remoteHash, StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (string name in UpdaterScripts)
            {
                (int showExit, byte[] script) = await RunGitAsync(["show", $"{tag}:scripts/{name}"], timeout.Token);
                if (showExit != 0 || script.Length == 0) return false;
                await File.WriteAllBytesAsync(Path.Combine(staging, name), script, timeout.Token);
            }

            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Runs git in the source checkout without prompting, and returns its exit code and raw output.</summary>
    private async Task<(int ExitCode, byte[] Output)> RunGitAsync(IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo start = new("git")
        {
            WorkingDirectory = _install!.SourcePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "never";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Git could not start.");
        try
        {
            using MemoryStream output = new();
            Task copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            Task<string> errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await copy;
            _ = await errors;
            return (process.ExitCode, output.ToArray());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private void ChangeState(AppUpdateState state)
    {
        if (State == state) return;
        _state = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _lifetime.Cancel();
    }
}
