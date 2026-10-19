using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaskWorker.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    private const long MigrationLockKey = 4_242_424_242;

    public static async Task MigrateAsync(TaskDbContext db, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var mustClose = connection.State != ConnectionState.Open;

        if (mustClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using (var lockCommand = new NpgsqlCommand(
                             $"SELECT pg_advisory_lock({MigrationLockKey})",
                             connection))
            {
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
            finally
            {
                await using var unlockCommand = new NpgsqlCommand(
                    $"SELECT pg_advisory_unlock({MigrationLockKey})",
                    connection);

                await unlockCommand.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
        finally
        {
            if (mustClose)
            {
                await connection.CloseAsync();
            }
        }
    }
}
