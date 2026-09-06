using CompanyName.ProjectName.Domain.Users.Entities;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class UserManagementRulesTests
{
    [Fact]
    public void Regular_user_can_be_managed_disabled_and_deleted()
    {
        var user = CreateUser();

        Assert.True(user.CanBeManagedBy(Guid.NewGuid()));
        Assert.True(user.CanBeDisabled());
        Assert.True(user.CanBeDeleted());
    }

    [Fact]
    public void Built_in_super_admin_can_only_be_managed_by_itself()
    {
        var user = CreateUser();
        user.MarkAsSuperAdmin();

        Assert.True(user.CanBeManagedBy(user.Id));
        Assert.False(user.CanBeManagedBy(Guid.NewGuid()));
        Assert.False(user.CanBeManagedBy(null));
    }

    [Fact]
    public void Built_in_super_admin_cannot_be_disabled_or_deleted()
    {
        var user = CreateUser();
        user.MarkAsSuperAdmin();

        Assert.False(user.CanBeDisabled());
        Assert.False(user.CanBeDeleted());
    }

    private static User CreateUser() =>
#if (LocalIdentity)
        new("management-user", "management@example.com", "password-hash");
#else
        new(
            Guid.Parse("01991a40-8a00-7000-8000-000000000002"),
            "management-user",
            "management@example.com");
#endif
}
