using Microsoft.Data.Sqlite;

namespace EntityTracker.Infrastructure.Persistence;

internal delegate Task SqliteBeforeCommit(
    SqliteConnection connection,
    SqliteTransaction transaction,
    CancellationToken cancellationToken);
