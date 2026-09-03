using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Needly.Infrastructure;

/// <summary>
/// Creates the Needly context for explicit local EF Core tooling commands.
/// </summary>
public sealed class NeedlyDbContextFactory : IDesignTimeDbContextFactory<NeedlyDbContext>
{
    /// <summary>
    /// Creates a context targeting the local Needly SQLite database.
    /// </summary>
    /// <param name="args">Arguments supplied by EF Core tooling.</param>
    /// <returns>A configured Needly database context.</returns>
    public NeedlyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NeedlyDbContext>()
            .UseSqlite("Data Source=needly.db")
            .Options;

        return new NeedlyDbContext(options);
    }
}