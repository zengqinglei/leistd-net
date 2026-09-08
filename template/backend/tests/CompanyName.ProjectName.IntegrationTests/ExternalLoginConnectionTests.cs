#if (ExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Entities;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class ExternalLoginConnectionTests
{
    private static readonly DateTime InitialSync = new(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Creation_records_the_callers_time()
    {
        var connection = CreateConnection(InitialSync);

        Assert.Equal(InitialSync, connection.LastSyncTime);
    }

    [Fact]
    public void Profile_update_records_the_callers_time()
    {
        var connection = CreateConnection(InitialSync);
        var updatedAt = InitialSync.AddMinutes(1);

        connection.Update(updatedAt, "updated-user", "updated@example.com", "https://example.com/avatar.png");

        Assert.Equal(updatedAt, connection.LastSyncTime);
        Assert.Equal("updated-user", connection.ProviderUsername);
    }

    [Fact]
    public void Token_update_records_the_callers_time()
    {
        var connection = CreateConnection(InitialSync);
        var updatedAt = InitialSync.AddMinutes(2);

        connection.UpdateTokens(updatedAt, "access-token", "refresh-token", updatedAt.AddHours(1));

        Assert.Equal(updatedAt, connection.LastSyncTime);
        Assert.Equal("access-token", connection.AccessToken);
    }

    private static ExternalLoginConnection CreateConnection(DateTime syncedAt) =>
        new(
            Guid.Parse("01991a40-8a00-7000-8000-000000000001"),
            "github",
            "provider-user-id",
            syncedAt,
            "provider-user",
            "provider@example.com");
}
#endif
