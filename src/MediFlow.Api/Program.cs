using MediFlow.Api;
using MediFlow.Infrastructure;
using MediFlow.Infrastructure.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddMediFlowWebService("mediflow-api");
builder.Services.AddMediFlowInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseMediFlowWebService("mediflow-api");
app.MapMembersEndpoints();
app.MapPlansEndpoints();
app.MapEnrollmentEndpoints();

app.Run();

namespace MediFlow.Api
{
    /// <summary>Named entry-point marker for WebApplicationFactory (the generated
    /// Program class is global and ambiguous across the two API projects).</summary>
    public sealed class EnrollmentApiEntryPoint { }
}
