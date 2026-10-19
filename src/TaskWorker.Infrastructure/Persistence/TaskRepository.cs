using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskWorker.Core.Tasks;

namespace TaskWorker.Infrastructure.Persistence;

public sealed class TaskRepository(TaskDbContext db)
{
    public async Task<(TaskItem Task, bool Created)> CreateAsync(
        string type,
        string payload,
        int maxAttempts,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null)
        {
            var existing = await db.Tasks
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existing is not null)
            {
                return (existing, false);
            }
        }

        var task = new TaskItem(
            type,
            payload,
            maxAttempts,
            idempotencyKey,
            DateTimeOffset.UtcNow);

        db.Tasks.Add(task);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (task, true);
        }
        catch (DbUpdateException exception)
            when (idempotencyKey is not null
                  && exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.Entry(task).State = EntityState.Detached;

            var existing = await db.Tasks
                .AsNoTracking()
                .SingleAsync(t => t.IdempotencyKey == idempotencyKey, cancellationToken);

            return (existing, false);
        }
    }

    public Task<TaskItem?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
}
