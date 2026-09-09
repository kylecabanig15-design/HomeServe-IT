using HomeServeIT.Web.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace HomeServeIT.Web.Tests.Infrastructure;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class MySqlTestDatabaseCollection : ICollectionFixture<MySqlTestDatabase>
{
    public const string CollectionName = "MySQL integration";
}

public sealed class MySqlTestDatabase : IAsyncLifetime
{
    private DbContextOptions<ApplicationDbContext>? _options;

    public bool IsConfigured { get; private set; }

    public async Task InitializeAsync()
    {
        var serverConnectionString = Environment.GetEnvironmentVariable("HOMESERVE_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(serverConnectionString))
            return;

        var serverVersion = ServerVersion.AutoDetect(serverConnectionString);
        var connectionBuilder = new MySqlConnectionStringBuilder(serverConnectionString)
        {
            Database = $"homeserve_test_{Guid.NewGuid():N}"
        };

        _options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseMySql(connectionBuilder.ConnectionString, serverVersion)
            .Options;

        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        IsConfigured = true;
    }

    public ApplicationDbContext CreateContext() => _options == null
        ? throw new InvalidOperationException("Set HOMESERVE_TEST_CONNECTION to a MySQL server connection before creating a test context.")
        : new ApplicationDbContext(_options);

    public async Task DisposeAsync()
    {
        if (!IsConfigured)
            return;

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
