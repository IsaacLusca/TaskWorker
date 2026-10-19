namespace TaskWorker.Core.Tasks;

public interface ITaskHandler
{
    string Type { get; }

    Task<string?> HandleAsync(TaskContext context, CancellationToken cancellationToken);
}
