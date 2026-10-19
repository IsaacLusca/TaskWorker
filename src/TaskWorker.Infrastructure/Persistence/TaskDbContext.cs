using Microsoft.EntityFrameworkCore;
using TaskWorker.Core.Tasks;

namespace TaskWorker.Infrastructure.Persistence;

public sealed class TaskDbContext(DbContextOptions<TaskDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskEffect> Effects => Set<TaskEffect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureTasks(modelBuilder);
        ConfigureEffects(modelBuilder);
    }

    private static void ConfigureTasks(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();

        task.ToTable("tasks", table =>
        {
            table.HasCheckConstraint(
                "ck_tasks_state",
                "state IN ('Pending', 'Processing', 'Completed', 'Failed')");
            table.HasCheckConstraint("ck_tasks_attempts", "attempts >= 0");
            table.HasCheckConstraint("ck_tasks_max_attempts", "max_attempts >= 1");
        });

        task.HasKey(t => t.Id);
        task.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        task.Property(t => t.Type).HasColumnName("type").HasMaxLength(100).IsRequired();
        task.Property(t => t.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        task.Property(t => t.State).HasColumnName("state").HasConversion<string>().HasMaxLength(20).IsRequired();
        task.Property(t => t.Attempts).HasColumnName("attempts").IsRequired();
        task.Property(t => t.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        task.Property(t => t.LastError).HasColumnName("last_error").HasMaxLength(TaskItem.MaxErrorLength);
        task.Property(t => t.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        task.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        task.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        task.Property(t => t.NextAttemptAt).HasColumnName("next_attempt_at");
        task.Property(t => t.LockedAt).HasColumnName("locked_at");
        task.Property(t => t.LockedBy).HasColumnName("locked_by").HasMaxLength(200);
        task.Property(t => t.FinishedAt).HasColumnName("finished_at");

        task.HasIndex(t => t.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("ux_tasks_idempotency_key")
            .HasFilter("\"idempotency_key\" IS NOT NULL");
        task.HasIndex(t => new { t.State, t.NextAttemptAt })
            .HasDatabaseName("ix_tasks_state_next_attempt_at");
        task.HasIndex(t => new { t.State, t.LockedAt })
            .HasDatabaseName("ix_tasks_state_locked_at");
    }

    private static void ConfigureEffects(ModelBuilder modelBuilder)
    {
        var effect = modelBuilder.Entity<TaskEffect>();

        effect.ToTable("task_effects");
        effect.HasKey(e => e.TaskId);
        effect.Property(e => e.TaskId).HasColumnName("task_id").ValueGeneratedNever();
        effect.Property(e => e.Result).HasColumnName("result").HasColumnType("text");
        effect.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        effect.HasOne<TaskItem>()
            .WithOne()
            .HasForeignKey<TaskEffect>(e => e.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
