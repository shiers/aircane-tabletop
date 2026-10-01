using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace Aircane.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations add</c> and related tooling.
/// Reads the connection string from the AIRCANE_CONNECTION_STRING environment variable,
/// falling back to the local development default when not set.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AircaneDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=aircane;Username=aircane;Password=aircane_dev";

    public AircaneDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("AIRCANE_CONNECTION_STRING")
            ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<AircaneDbContext>();
        optionsBuilder.UseNpgsql(connectionString, o =>
        {
            o.UseVector();
            o.MigrationsAssembly("Aircane.Infrastructure");
        });

        return new AircaneDbContext(optionsBuilder.Options);
    }
}
