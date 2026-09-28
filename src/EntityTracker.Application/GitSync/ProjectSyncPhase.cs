namespace EntityTracker.Application.GitSync;

public enum ProjectSyncPhase
{
    CheckingEdits,
    Inspecting,
    Exporting,
    Fetching,
    Reviewing,
    Applying,
    Committing,
    Pushing,
    Validating,
    Rechecking
}

public sealed class ProjectSyncReviewCancelledException(string message) : InvalidOperationException(message);

public sealed class ProjectSyncReviewRequiredException(string message) : InvalidOperationException(message);
