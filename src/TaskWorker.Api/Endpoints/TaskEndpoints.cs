using System.Text.Json;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;

namespace TaskWorker.Api.Endpoints;

public static class TaskEndpoints
{
    private const int DefaultMaxAttempts = 3;
    private const int MaxAllowedAttempts = 20;
    private const int MaxPayloadLength = 64 * 1024;

    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/tasks").WithTags("Tasks");

        group.MapPost(string.Empty, CreateAsync);
        group.MapGet("/{id:guid}", GetAsync);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateTaskRequest request,
        TaskRepository repository,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var type = request.Type?.Trim();

        if (string.IsNullOrWhiteSpace(type))
        {
            errors["type"] = ["Task type is required."];
        }
        else if (type.Length > 100)
        {
            errors["type"] = ["Task type must have at most 100 characters."];
        }

        var maxAttempts = request.MaxAttempts ?? DefaultMaxAttempts;

        if (maxAttempts is < 1 or > MaxAllowedAttempts)
        {
            errors["maxAttempts"] = [$"Max attempts must be between 1 and {MaxAllowedAttempts}."];
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        if (idempotencyKey is { Length: > 200 })
        {
            errors["idempotencyKey"] = ["Idempotency key must have at most 200 characters."];
        }

        var payload = request.Payload?.GetRawText() ?? "{}";

        if (payload.Length > MaxPayloadLength)
        {
            errors["payload"] = [$"Payload must have at most {MaxPayloadLength} characters."];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var (task, created) = await repository.CreateAsync(
            type!,
            payload,
            maxAttempts,
            idempotencyKey,
            cancellationToken);

        var response = TaskResponse.From(task);

        return created
            ? Results.Created($"/tasks/{task.Id}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        TaskRepository repository,
        CancellationToken cancellationToken)
    {
        var task = await repository.GetAsync(id, cancellationToken);

        return task is null
            ? Results.NotFound()
            : Results.Ok(TaskResponse.From(task));
    }

    private sealed record CreateTaskRequest
    {
        public string? Type { get; init; }

        public JsonElement? Payload { get; init; }

        public int? MaxAttempts { get; init; }

        public string? IdempotencyKey { get; init; }
    }

    private sealed record TaskResponse(
        Guid Id,
        string Type,
        string State,
        int Attempts,
        int MaxAttempts,
        string? LastError,
        string? IdempotencyKey,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? NextAttemptAt,
        DateTimeOffset? FinishedAt)
    {
        public static TaskResponse From(TaskItem task) => new(
            task.Id,
            task.Type,
            task.State.ToString(),
            task.Attempts,
            task.MaxAttempts,
            task.LastError,
            task.IdempotencyKey,
            task.CreatedAt,
            task.UpdatedAt,
            task.NextAttemptAt,
            task.FinishedAt);
    }
}
