using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Compact;
using TaskWorker.Core.Options;
using TaskWorker.Core.Processing;
using TaskWorker.Core.Tasks;
using TaskWorker.Infrastructure.Persistence;
using TaskWorker.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "TaskWorker.Worker")
    .WriteTo.Console(new CompactJsonFormatter()));

var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'Postgres' is required. Configure ConnectionStrings__Postgres.");
}

builder.Services.AddTaskWorkerPersistence(connectionString);

builder.Services
    .AddOptions<WorkerOptions>()
    .Bind(builder.Configuration.GetSection(WorkerOptions.SectionName))
    .Validate(
        options => options.BatchSize is > 0 and <= 100,
        "Worker:BatchSize must be between 1 and 100.")
    .Validate(
        options => options.PollInterval > TimeSpan.Zero,
        "Worker:PollInterval must be greater than zero.")
    .Validate(
        options => options.LeaseTimeout > TimeSpan.Zero,
        "Worker:LeaseTimeout must be greater than zero.")
    .Validate(
        options => options.LeaseRenewalInterval > TimeSpan.Zero
                   && options.LeaseRenewalInterval < options.LeaseTimeout,
        "Worker:LeaseRenewalInterval must be greater than zero and less than Worker:LeaseTimeout.")
    .Validate(
        options => options.LeaseRecoveryInterval > TimeSpan.Zero,
        "Worker:LeaseRecoveryInterval must be greater than zero.")
    .Validate(
        options => options.Retry.BaseDelay > TimeSpan.Zero,
        "Worker:Retry:BaseDelay must be greater than zero.")
    .Validate(
        options => options.Retry.MaxDelay >= options.Retry.BaseDelay,
        "Worker:Retry:MaxDelay must be greater than or equal to Worker:Retry:BaseDelay.")
    .ValidateOnStart();

builder.Services.AddTaskWorkerProcessing();
builder.Services.AddTransient<ITaskHandler, DemoTaskHandler>();

var host = builder.Build();

await using (var validationScope = host.Services.CreateAsyncScope())
{
    _ = validationScope.ServiceProvider.GetRequiredService<TaskHandlerRegistry>();
}

await using (var migrationScope = host.Services.CreateAsyncScope())
{
    var db = migrationScope.ServiceProvider.GetRequiredService<TaskDbContext>();
    await DatabaseInitializer.MigrateAsync(db, CancellationToken.None);
}

await host.RunAsync();
