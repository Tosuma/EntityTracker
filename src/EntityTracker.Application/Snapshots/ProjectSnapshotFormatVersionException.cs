namespace EntityTracker.Application.Snapshots;

public sealed class ProjectSnapshotFormatVersionException : IOException
{
    public ProjectSnapshotFormatVersionException(int projectFormatVersion)
        : base(CreateMessage(projectFormatVersion))
    {
        ProjectFormatVersion = projectFormatVersion;
    }

    public int ApplicationFormatVersion => ProjectSnapshot.CurrentFormatVersion;

    public int ProjectFormatVersion { get; }

    private static string CreateMessage(int projectFormatVersion)
    {
        string versions = $"This app's Project format version is {ProjectSnapshot.CurrentFormatVersion}; " +
                          $"the Project's format version is {projectFormatVersion}.";
        return projectFormatVersion > ProjectSnapshot.CurrentFormatVersion
            ? versions + " The app is behind this Project. Update the app to open it."
            : versions + " The app is ahead of this Project, whose format is older than " +
              $"the supported range (1–{ProjectSnapshot.CurrentFormatVersion}). " +
              "Open it with a compatible app version.";
    }
}
