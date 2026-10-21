using TaskWorker.Core.Processing;
using TaskWorker.Core.Tasks;

namespace TaskWorker.UnitTests;

public sealed class TaskHandlerRegistryTests
{
    [Fact]
    public void Finds_a_handler_ignoring_case()
    {
        var handler = new StubHandler("Demo");
        var registry = new TaskHandlerRegistry([handler]);

        Assert.Same(handler, registry.Find("demo"));
        Assert.Same(handler, registry.Find("DEMO"));
    }

    [Fact]
    public void Returns_null_when_no_handler_is_registered_for_the_type()
    {
        var registry = new TaskHandlerRegistry([new StubHandler("demo")]);

        Assert.Null(registry.Find("unknown"));
    }

    [Fact]
    public void Rejects_duplicate_handlers_for_the_same_type()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TaskHandlerRegistry([new StubHandler("demo"), new StubHandler("DEMO")]));

        Assert.Contains("demo", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_handlers_with_empty_types()
    {
        Assert.Throws<ArgumentException>(() => new TaskHandlerRegistry([new StubHandler(" ")]));
    }

    private sealed class StubHandler(string type) : ITaskHandler
    {
        public string Type => type;

        public Task<string?> HandleAsync(
            TaskContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }
}
