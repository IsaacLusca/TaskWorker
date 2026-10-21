using Microsoft.EntityFrameworkCore;
using TaskWorker.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace TaskWorker.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("taskworker_tests")
        .WithUsername("taskworker")
        .WithPassword("taskworker_tests")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await DatabaseInitializer.MigrateAsync(db, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public TaskDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TaskDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        return new TaskDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE task_effects, tasks CASCADE;");
    }
}
