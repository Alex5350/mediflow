using MediFlow.Infrastructure;
using MediFlow.Worker;
using OpenTelemetry.Metrics;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog(cfg => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .Enrich.WithProperty("Service", "mediflow-worker")
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Service} {Message:lj}{NewLine}{Exception}", formatProvider: System.Globalization.CultureInfo.InvariantCulture));

builder.Services.AddMediFlowInfrastructure(builder.Configuration);

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter(AdjudicationMetrics.MeterName)
        .AddRuntimeInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddOtlpExporter());
}

builder.Services.AddHostedService<AdjudicationWorker>();

var host = builder.Build();
host.Run();
