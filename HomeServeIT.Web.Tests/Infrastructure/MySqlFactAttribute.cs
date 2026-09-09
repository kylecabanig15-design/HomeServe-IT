namespace HomeServeIT.Web.Tests.Infrastructure;

public sealed class MySqlFactAttribute : FactAttribute
{
    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOMESERVE_TEST_CONNECTION")))
            Skip = "Set HOMESERVE_TEST_CONNECTION to run provider-specific MySQL concurrency tests.";
    }
}
