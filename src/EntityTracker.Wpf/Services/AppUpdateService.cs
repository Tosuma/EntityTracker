using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using EntityTracker.Application.GitSync;

namespace EntityTracker.Wpf.Services;

public enum AppUpdateState { Development, Checking, Current, Offline, Required }

public sealed record AppInstallManifest(string SourcePath, string Version);

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
    private Task? _loop;
    private AppReleaseVersion _installedVersion;
    private string? _requiredTag;
    private volatile AppUpdateState _state;

    public AppUpdateService(ApplicationDataPaths dataPaths, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _requiredPath = Path.Combine(dataPaths.RootDirectory, "required-app-update.txt");
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
        Func<CancellationToken, Task<string>> query)
    {
        _clock = TimeProvider.System;
        _requiredPath = Path.Combine(dataPaths.RootDirectory, "required-app-update.txt");
        _install = install;
        _queryOverride = query;
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
                    ProcessStartInfo start = new("git")
                    {
                        WorkingDirectory = _install.SourcePath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    start.ArgumentList.Add("ls-remote");
                    start.ArgumentList.Add("--refs");
                    start.ArgumentList.Add("--tags");
                    start.ArgumentList.Add("origin");
                    start.ArgumentList.Add("app-v*");
                    start.Environment["GIT_TERMINAL_PROMPT"] = "0";
                    start.Environment["GCM_INTERACTIVE"] = "never";
                    using Process process = Process.Start(start)
                        ?? throw new InvalidOperationException("Git could not start.");
                    try
                    {
                        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
                        Task<string> errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
                        await process.WaitForExitAsync(timeout.Token);
                        output = await outputTask;
                        _ = await errorTask;
                    }
                    catch (OperationCanceledException)
                    {
                        if (!process.HasExited) process.Kill(entireProcessTree: true);
                        throw;
                    }
                    if (process.ExitCode != 0) throw new IOException("Release check failed.");
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

    public void LaunchUpdater(int appProcessId)
    {
        if (_install is null || RequiredTag is null)
            throw new InvalidOperationException("No update is required.");
        string source = Path.Combine(AppContext.BaseDirectory, "Updater");
        string staging = Path.Combine(Path.GetTempPath(), "EntityTracker-Updater-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        foreach (string name in new[] { "Update-EntityTracker.ps1", "Install-EntityTracker.ps1", "Install-Common.ps1" })
            File.Copy(Path.Combine(source, name), Path.Combine(staging, name));
        ProcessStartInfo start = new("powershell.exe")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-STA");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(staging, "Update-EntityTracker.ps1"));
        start.ArgumentList.Add("-SourcePath");
        start.ArgumentList.Add(_install.SourcePath);
        start.ArgumentList.Add("-Tag");
        start.ArgumentList.Add(RequiredTag);
        start.ArgumentList.Add("-WaitForProcess");
        start.ArgumentList.Add(appProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _ = Process.Start(start) ?? throw new InvalidOperationException("The updater could not start.");
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
