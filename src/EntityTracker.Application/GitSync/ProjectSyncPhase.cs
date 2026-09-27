namespace EntityTracker.Application.GitSync;

public enum ProjectSyncPhase
{
    CheckingEdits,
    Inspecting,
    Fetching,
    Reviewing,
    Applying,
    Committing,
    Pushing
}

public sealed class ProjectSyncReviewCancelledException(string message) : InvalidOperationException(message);

public sealed class ProjectSyncReviewRequiredException(string message) : InvalidOperationException(message);
