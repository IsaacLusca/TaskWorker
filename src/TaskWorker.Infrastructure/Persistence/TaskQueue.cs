using System.Data;
using Npgsql;
using NpgsqlTypes;
using TaskWorker.Core.Tasks;

namespace TaskWorker.Infrastructure.Persistence;

public sealed class TaskQueue(NpgsqlDataSource dataSource)
{
    private const string ClaimSql = """
        WITH next_tasks AS (
            SELECT id
            FROM tasks
            WHERE state = 'Pending'
              AND next_attempt_at <= now()
            ORDER BY next_attempt_at, created_at
            LIMIT @batch_size
            FOR UPDATE SKIP LOCKED
        )
        UPDATE tasks AS t
        SET state = 'Processing',
            locked_at = now(),
            locked_by = @worker_id,
            attempts = t.attempts + 1,
            updated_at = now()
        FROM next_tasks
        WHERE t.id = next_tasks.id
        RETURNING t.id, t.type, t.payload, t.attempts, t.max_attempts;
        """;

    private const string CompleteSql = """
        INSERT INTO task_effects (task_id, result, created_at)
        VALUES (@task_id, @result, now())
        ON CONFLICT (task_id) DO NOTHING;
        """;

    private const string CompleteTaskSql = """
        UPDATE tasks
        SET state = 'Completed',
            locked_at = NULL,
            locked_by = NULL,
            last_error = NULL,
            next_attempt_at = NULL,
            finished_at = now(),
            updated_at = now()
        WHERE id = @task_id
          AND state = 'Processing'
          AND locked_by = @worker_id;
        """;

    private const string FailSql = """
        UPDATE tasks
        SET state = CASE
                WHEN @permanent OR attempts >= max_attempts THEN 'Failed'
                ELSE 'Pending'
            END,
            last_error = @error,
            next_attempt_at = CASE
                WHEN @permanent OR attempts >= max_attempts THEN NULL
                ELSE now() + @retry_delay
            END,
            finished_at = CASE
                WHEN @permanent OR attempts >= max_attempts THEN now()
                ELSE NULL
            END,
            locked_at = NULL,
            locked_by = NULL,
            updated_at = now()
        WHERE id = @task_id
          AND state = 'Processing'
          AND locked_by = @worker_id
        RETURNING state, attempts, next_attempt_at;
        """;

    private const string ReleaseSql = """
        UPDATE tasks
        SET state = 'Pending',
            attempts = GREATEST(attempts - 1, 0),
            locked_at = NULL,
            locked_by = NULL,
            next_attempt_at = now(),
            updated_at = now()
        WHERE id = @task_id
          AND state = 'Processing'
          AND locked_by = @worker_id;
        """;

    private const string RenewLeaseSql = """
        UPDATE tasks
        SET locked_at = now(),
            updated_at = now()
        WHERE id = @task_id
          AND state = 'Processing'
          AND locked_by = @worker_id;
        """;

    private const string RecoverStaleSql = """
        UPDATE tasks
        SET state = 'Pending',
            locked_at = NULL,
            locked_by = NULL,
            next_attempt_at = now(),
            last_error = COALESCE(last_error, 'Processing lease expired before completion.'),
            updated_at = now()
        WHERE state = 'Processing'
          AND locked_at < now() - @lease_timeout;
        """;

    public async Task<IReadOnlyList<TaskLease>> ClaimBatchAsync(
        string workerId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ClaimSql, connection);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("batch_size", batchSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var leases = new List<TaskLease>(batchSize);

        while (await reader.ReadAsync(cancellationToken))
        {
            leases.Add(new TaskLease(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return leases;
    }

    public async Task<TaskCompletionResult> CompleteAsync(
        Guid taskId,
        string workerId,
        string? result,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        int effectRows;
        await using (var effectCommand = new NpgsqlCommand(CompleteSql, connection, transaction))
        {
            effectCommand.Parameters.AddWithValue("task_id", taskId);
            effectCommand.Parameters.AddWithValue("result", NpgsqlDbType.Text, (object?)result ?? DBNull.Value);
            effectRows = await effectCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        int updated;
        await using (var taskCommand = new NpgsqlCommand(CompleteTaskSql, connection, transaction))
        {
            taskCommand.Parameters.AddWithValue("task_id", taskId);
            taskCommand.Parameters.AddWithValue("worker_id", workerId);
            updated = await taskCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new TaskCompletionResult(updated == 1, effectRows == 1);
    }

    public async Task<bool> RenewLeaseAsync(
        Guid taskId,
        string workerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RenewLeaseSql, connection);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("worker_id", workerId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<TaskFailureResult?> RecordFailureAsync(
        Guid taskId,
        string workerId,
        string error,
        bool permanent,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(FailSql, connection);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("error", NpgsqlDbType.Text, error);
        command.Parameters.AddWithValue("permanent", permanent);
        command.Parameters.AddWithValue("retry_delay", NpgsqlDbType.Interval, retryDelay);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var state = Enum.Parse<TaskState>(reader.GetString(0));
        var attempts = reader.GetInt32(1);
        DateTimeOffset? nextAttemptAt = reader.IsDBNull(2)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(2);

        return new TaskFailureResult(state, attempts, nextAttemptAt);
    }

    public async Task<bool> ReleaseAsync(
        Guid taskId,
        string workerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(ReleaseSql, connection);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("worker_id", workerId);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> RecoverStaleAsync(
        TimeSpan leaseTimeout,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(RecoverStaleSql, connection);
        command.Parameters.AddWithValue("lease_timeout", NpgsqlDbType.Interval, leaseTimeout);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken) =>
        await dataSource.OpenConnectionAsync(cancellationToken);
}
