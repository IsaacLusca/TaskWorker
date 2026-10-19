using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace TaskWorker.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskWorkerPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());

        services.AddDbContext<TaskDbContext>((serviceProvider, options) =>
            options.UseNpgsql(serviceProvider.GetRequiredService<NpgsqlDataSource>()));

        services.AddScoped<TaskRepository>();
        services.AddSingleton<TaskQueue>();

        return services;
    }
}
