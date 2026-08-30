using MediFlow.Claims.Api;
using MediFlow.Infrastructure;
using MediFlow.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddMediFlowWebService("mediflow-claims-api");
builder.Services.AddMediFlowInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMediFlowWebService("mediflow-claims-api");
app.MapClaimsEndpoints();
app.MapRollupEndpoints();

app.Run();

namespace MediFlow.Claims.Api
{
    /// <summary>Named entry-point marker for WebApplicationFactory (the generated
    /// Program class is global and ambiguous across the two API projects).</summary>
    public sealed class ClaimsApiEntryPoint { }
}
