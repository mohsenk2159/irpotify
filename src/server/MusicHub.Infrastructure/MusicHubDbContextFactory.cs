using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MusicHub.Infrastructure;

public sealed class MusicHubDbContextFactory : IDesignTimeDbContextFactory<MusicHubDbContext>
{
    public MusicHubDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("MUSICHUB_CONNECTION_STRING")
            ?? "Host=127.0.0.1;Port=5433;Database=musichub;Username=musichub;Password=musichub";
        var options = new DbContextOptionsBuilder<MusicHubDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(MusicHubDbContext).Assembly.FullName))
            .Options;
        return new MusicHubDbContext(options);
    }
}
