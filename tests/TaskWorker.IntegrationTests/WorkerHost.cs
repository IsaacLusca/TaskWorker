using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskWorker.Core.Options;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;
using TaskWorker.Worker;

namespace TaskWorker.IntegrationTests;

internal sealed class WorkerHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _stopped;

    private WorkerHost(IHost host)
    {
        _host = host;
    }

    public static WorkerHost Create(
        string connectionString,
        ITaskHandler handler,
        Action<WorkerOptions>? configure = null)
    {
        var options = new WorkerOptions
        {
            BatchSize = 5,
            PollInterval = TimeSpan.FromMilliseconds(50),
            LeaseTimeout = TimeSpan.FromSeconds(5),
            LeaseRenewalInterval = TimeSpan.FromSeconds(1),
            LeaseRecoveryInterval = TimeSpan.FromMilliseconds(200),
            Retry = new RetryOptions
            {
                BaseDelay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(1)
            }
        };

        configure?.Invoke(options);

        var host = new HostBuilder()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.None);
            })
            .ConfigureServices(services =>
            {
                services.AddTaskWorkerPersistence(connectionString);
                services.AddSingleton<IOptions<WorkerOptions>>(Options.Create(options));
                services.AddSingleton<ITaskHandler>(handler);
                services.AddTaskWorkerProcessing();
            })
            .Build();

        return new WorkerHost(host);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _host.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        await _host.StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await StopAsync(cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _host.Dispose();
        }
    }
}

internal sealed class DelegateTaskHandler(
    string type,
    Func<TaskContext, CancellationToken, Task<string?>> handler) : ITaskHandler
{
    public string Type => type;

    public Task<string?> HandleAsync(
        TaskContext context,
        CancellationToken cancellationToken) =>
        handler(context, cancellationToken);
}
