namespace MediFlow.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

/// <summary>
/// Design-time factory so `dotnet ef migrations add` works from the class library
/// without a host. Migrations are generated, never connected — the placeholder
/// connection string is never opened.
/// </summary>
public sealed class MediFlowDbContextDesignFactory : IDesignTimeDbContextFactory<MediFlowDbContext>
{
    public MediFlowDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MediFlowDbContext>()
            .UseSqlServer("Server=design-time;Database=MediFlow;User ID=sa;Password=design-time;TrustServerCertificate=True")
            .Options;
        return new MediFlowDbContext(options);
    }
}
