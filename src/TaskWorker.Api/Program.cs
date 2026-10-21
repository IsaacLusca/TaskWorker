using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using TaskWorker.Api.Endpoints;
using TaskWorker.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TaskWorker.Api")
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddProblemDetails();

var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Postgres' is required. Configure ConnectionStrings__Postgres.");
}

builder.Services.AddTaskWorkerPersistence(connectionString);

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

await using (var migrationScope = app.Services.CreateAsyncScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await DatabaseInitializer.MigrateAsync(db, CancellationToken.None);
}

app.MapGet("/health", async (TaskDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapTaskEndpoints();

await app.RunAsync();
