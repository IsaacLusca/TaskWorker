using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskWorker.Infrastructure.Persistence;

public sealed class TaskDbContextFactory : IDesignTimeDbContextFactory<TaskDbContext>
{
    public TaskDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=taskworker;Username=taskworker;Password=taskworker_dev";

        var options = new DbContextOptionsBuilder<TaskDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TaskDbContext(options);
    }
}
