using DbExecPlanMonitor.Worker;
using DbExecPlanMonitor.Worker.HealthChecks;
using DbExecPlanMonitor.Worker.Scheduling;
using DbExecPlanMonitor.Infrastructure;
using DbExecPlanMonitor.Infrastructure.Configuration;
using DbExecPlanMonitor.Infrastructure.Logging;
using Microsoft.Extensions.Options;
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

    // Register configuration services with validation
    builder.Services.AddMonitoringConfiguration(builder.Configuration);
    builder.Services.AddInstanceConfiguration(builder.Configuration);

    // Register Infrastructure layer services (SQL Server monitoring)
    builder.Services.AddSqlServerMonitoring(builder.Configuration);
    builder.Services.AddMonitoringStorage(builder.Configuration);
    builder.Services.AddPlanCollection(builder.Configuration);
    builder.Services.AddAnalysis(builder.Configuration);
    builder.Services.AddAlerting(builder.Configuration);
    builder.Services.AddRemediation(builder.Configuration);
    builder.Services.AddTelemetryAndAuditing(builder.Configuration);
    builder.Services.AddSecurityServices(builder.Configuration);
    builder.Services.AddMonitoringValidation();

    // Register scheduling options with validation
    builder.Services.Configure<SchedulingOptions>(
        builder.Configuration.GetSection(SchedulingOptions.SectionName));
    builder.Services.AddSingleton<IValidateOptions<SchedulingOptions>, SchedulingOptionsValidator>();

    // Register the background worker services
    builder.Services.AddHostedService<PlanCollectionHostedService>();
    builder.Services.AddHostedService<AnalysisHostedService>();
    builder.Services.AddHostedService<BaselineRebuildHostedService>();
    builder.Services.AddHostedService<DailySummaryHostedService>();

    // Add health checks for operational monitoring
    builder.Services.AddHealthChecks()
        .AddCheck<LivenessHealthCheck>("liveness", tags: new[] { "liveness" })
        .AddCheck<StorageHealthCheck>("storage", tags: new[] { "readiness" })
        .AddCheck<SqlServerHealthCheck>("sqlserver", tags: new[] { "readiness" });

    // Enable Windows Service hosting when running as a service
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "DbExecPlanMonitor";
    });

    // Enable systemd hosting for Linux
    builder.Services.AddSystemd();

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
