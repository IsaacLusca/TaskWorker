using TaskWorker.Core.Tasks;

namespace TaskWorker.Core.Processing;

public sealed class TaskHandlerRegistry
{
    private readonly IReadOnlyDictionary<string, ITaskHandler> _handlers;

    public TaskHandlerRegistry(IEnumerable<ITaskHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        var registered = new Dictionary<string, ITaskHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(handler.Type);

            if (!registered.TryAdd(handler.Type, handler))
            {
                throw new InvalidOperationException(
                    $"More than one handler was registered for task type '{handler.Type}'.");
            }
        }

        _handlers = registered;
    }

    public ITaskHandler? Find(string type) =>
        _handlers.GetValueOrDefault(type);
}
