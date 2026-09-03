using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Needly.Infrastructure;

/// <summary>
/// Creates the Needly context for explicit local EF Core tooling commands.
/// </summary>
public sealed class NeedlyDbContextFactory : IDesignTimeDbContextFactory<NeedlyDbContext>
{
    private const string ConnectionStringVariable = "NEEDLY_MIGRATIONS_CONNECTION";

    private const string DefaultConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Needly;Trusted_Connection=True;TrustServerCertificate=True";

    /// <summary>
    /// Creates a context targeting the SQL Server database used by EF Core tooling.
    /// </summary>
    /// <param name="args">Arguments supplied by EF Core tooling.</param>
    /// <returns>A configured Needly database context.</returns>
    public NeedlyDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnectionString;
        }

        var options = new DbContextOptionsBuilder<NeedlyDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new NeedlyDbContext(options);
    }
}