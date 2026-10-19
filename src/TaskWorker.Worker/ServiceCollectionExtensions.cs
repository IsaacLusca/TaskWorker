using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaskWorker.Core.Options;
using TaskWorker.Core.Processing;

namespace TaskWorker.Worker;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTaskWorkerProcessing(this IServiceCollection services)
    {
        services.AddScoped<TaskHandlerRegistry>();
        services.AddScoped<TaskProcessor>();
        services.AddSingleton(serviceProvider => new RetrySchedule(
            serviceProvider.GetRequiredService<IOptions<WorkerOptions>>().Value.Retry));
        services.AddHostedService<TaskWorkerService>();

        return services;
    }
}
