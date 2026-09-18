#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;
using CompanyName.ProjectName.Domain.Users.Policies;

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
        user.RecordAccessFailed(Now, Lockout);

        user.RecordLoginSuccess(Now, "127.0.0.1");

        Assert.Equal(Now, user.LastLoginTime);
        Assert.Equal("127.0.0.1", user.LastLoginIp);
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public void Reaching_the_threshold_locks_temporarily_and_resets_the_count()
    {
        var user = CreateUser();
        for (var i = 1; i < Lockout.MaxFailedAttempts; i++)
        {
            Assert.False(user.RecordAccessFailed(Now, Lockout));
        }

        Assert.True(user.RecordAccessFailed(Now, Lockout));
        Assert.Equal(UserAccessStatus.LockedOut, user.GetAccessStatus(Now));
        Assert.True(user.IsTemporarilyLockedOut(Now));
        Assert.Equal(Now + Lockout.Duration, user.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);
    }

    // 过期锁定留在库里的 IsLocked 要在下一次失败时清掉，否则会一直被当成"已锁定"
    [Fact]
    public void A_failure_after_an_expired_lockout_starts_a_fresh_count()
    {
        var user = CreateUser();
        user.Lock(Now);

        Assert.False(user.RecordAccessFailed(Now.AddMinutes(1), Lockout));
        Assert.False(user.IsLocked);
        Assert.Equal(1, user.AccessFailedCount);
    }

    [Fact]
    public void A_zero_threshold_never_locks()
    {
        var user = CreateUser();
        var disabled = Lockout with { MaxFailedAttempts = 0 };

        for (var i = 0; i < 20; i++)
        {
            Assert.False(user.RecordAccessFailed(Now, disabled));
        }

        Assert.Equal(UserAccessStatus.Allowed, user.GetAccessStatus(Now));
    }

    // 管理员锁定没有截止时间，不能被当作"失败锁定"放过已在线的会话
    [Fact]
    public void An_administrator_lock_is_not_a_temporary_lockout()
    {
        var user = CreateUser();
        user.Lock();

        Assert.Equal(UserAccessStatus.LockedOut, user.GetAccessStatus(Now));
        Assert.False(user.IsTemporarilyLockedOut(Now));
    }

    private static readonly LoginLockoutPolicy Lockout = new(5, TimeSpan.FromMinutes(15));

    private static User CreateUser() =>
        new("access-status-user", "access-status@example.com", "password-hash");
}
#endif
