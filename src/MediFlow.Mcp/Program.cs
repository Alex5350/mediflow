using MediFlow.Infrastructure;
using MediFlow.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

// MCP hosts launch us from arbitrary working directories — pin the content
// root to the assembly location so appsettings.json always loads.
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// The transport is stdio: the channel is the protocol. No console logging —
// anything written to stdout would corrupt the JSON-RPC stream.
builder.Logging.ClearProviders();
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);

builder.Services.AddMediFlowInfrastructure(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
