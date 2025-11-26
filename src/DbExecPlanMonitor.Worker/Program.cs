using DbExecPlanMonitor.Worker;
using DbExecPlanMonitor.Infrastructure;
using Serilog;

// Configure Serilog early for bootstrap logging
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting DB Execution Plan Monitor Service");

    var builder = Host.CreateApplicationBuilder(args);

    // Configure Serilog from appsettings.json
    builder.Services.AddSerilog((services, loggerConfig) =>
    {
        loggerConfig
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                path: "logs/dbexecplanmonitor-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);
    });

    // Register Infrastructure layer services (SQL Server monitoring)
    builder.Services.AddSqlServerMonitoring(builder.Configuration);
    builder.Services.AddMonitoringStorage(builder.Configuration);
    builder.Services.AddMonitoringValidation();

    // Register the background worker service
    builder.Services.AddHostedService<MonitoringWorker>();

    // Add health checks for operational monitoring
    builder.Services.AddHealthChecks();

    // TODO: Register Application layer services (use cases, orchestrators)

    // Enable Windows Service hosting when running as a service
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "DbExecPlanMonitor";
    });

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
