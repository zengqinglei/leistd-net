#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;

namespace CompanyName.ProjectName.IntegrationTests;

public sealed class UserAccessStatusTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Active_unlocked_user_is_allowed()
    {
        var user = CreateUser();

        Assert.Equal(UserAccessStatus.Allowed, user.GetAccessStatus(Now));
    }

    [Fact]
    public void Disabled_user_is_denied_before_lock_state_is_considered()
    {
        var user = CreateUser();
        user.Disable();
        user.Lock(Now.AddMinutes(5));

        Assert.Equal(UserAccessStatus.Disabled, user.GetAccessStatus(Now));
    }

    [Fact]
    public void Indefinitely_locked_user_is_denied()
    {
        var user = CreateUser();
        user.Lock();

        Assert.Equal(UserAccessStatus.LockedOut, user.GetAccessStatus(Now));
    }

    [Fact]
    public void Timed_lock_uses_the_callers_clock_and_expires_at_its_boundary()
    {
        var user = CreateUser();
        var lockoutEnd = Now.AddMinutes(5);
        user.Lock(lockoutEnd);

        Assert.Equal(UserAccessStatus.LockedOut, user.GetAccessStatus(lockoutEnd.AddTicks(-1)));
        Assert.Equal(UserAccessStatus.Allowed, user.GetAccessStatus(lockoutEnd));
    }

    [Fact]
    public void Login_success_records_the_callers_time()
    {
        var user = CreateUser();
        user.RecordAccessFailed();

        user.RecordLoginSuccess(Now, "127.0.0.1");

        Assert.Equal(Now, user.LastLoginTime);
        Assert.Equal("127.0.0.1", user.LastLoginIp);
        Assert.Equal(0, user.AccessFailedCount);
    }

    private static User CreateUser() =>
        new("access-status-user", "access-status@example.com", "password-hash");
}
#endif
