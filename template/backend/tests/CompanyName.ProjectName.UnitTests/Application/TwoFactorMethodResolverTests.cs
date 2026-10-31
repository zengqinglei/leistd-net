#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.TwoFactor;
#if (Email)
using Leistd.Settings.Resolution;
#endif
using CompanyName.ProjectName.Domain.Auth.Entities;
using CompanyName.ProjectName.Domain.Auth.Passkeys;
using CompanyName.ProjectName.Domain.Users.Entities;
using CompanyName.ProjectName.Domain.Users.ValueObjects;

namespace CompanyName.ProjectName.UnitTests.Application;

/// <summary>首选方式失效后的系统顺序与部署不可用时的拒绝：恢复码是独立因子不可用时的最后出路。</summary>
public sealed class TwoFactorMethodResolverTests
{
    [Theory]
    [InlineData(false, false, false, null, "PasskeyUnavailable")]
    [InlineData(false, false, true, "RecoveryCode", null)]
    [InlineData(false, true, true, "Authenticator", null)]
    [InlineData(true, true, true, "Passkey", null)]
    [InlineData(true, false, false, "Passkey", null)]
    public async Task Sign_in_uses_the_preference_then_system_order_or_refuses(
        bool available, bool authenticator, bool recoveryCodes, string? expectedDefault, string? expectedRefusal)
    {
        var user = new User("resolver", "resolver@example.test", "hash");
        user.RecordPasskeyRegistered(["hash-1"]);
        user.SetPreferredTwoFactorMethod(TwoFactorMethod.Passkey);
        if (authenticator)
            user.EnableTwoFactor("secret", null, 1);
        if (!recoveryCodes)
            Assert.True(user.TryConsumeRecoveryCode("hash-1"));
        var resolver = new TwoFactorMethodResolver(
#if (Email)
            new DefaultSettings(), null!,
#endif
            new AvailabilityProtocol(available));

        var result = await resolver.ResolveForSignInAsync(user, firstFactorViaEmail: false);

        Assert.Equal(expectedDefault, result.DefaultMethod?.ToString());
        Assert.Equal(expectedRefusal, result.Refusal?.ToString());
        Assert.Equal(available, result.Allows(TwoFactorMethod.Passkey));
        Assert.Equal(authenticator, result.Allows(TwoFactorMethod.Authenticator));
        Assert.Equal(recoveryCodes, result.Allows(TwoFactorMethod.RecoveryCode));
    }

#if (Email)
    private sealed class DefaultSettings : ISettingProvider
    {
        public Task<string?> GetOrNullAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> GetOrNullForUserAsync(string name, string userId, CancellationToken cancellationToken = default) => GetOrNullAsync(name, cancellationToken);
        public Task<T?> GetAsync<T>(string name, CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);
        public Task<IReadOnlyDictionary<string, string?>> GetAllAsync(bool visibleToClientsOnly = false, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
    }

#endif
    private sealed class AvailabilityProtocol(bool available) : IPasskeyProtocol
    {
        public bool IsAvailable => available;
        public PasskeyCeremony BeginRegistration(PasskeyAccount account, IEnumerable<PasskeyDescriptor> existing) => throw new NotSupportedException();
        public Task<PasskeyCredentialData> CompleteRegistrationAsync(string state, string response, CancellationToken cancellationToken) => throw new NotSupportedException();
        public PasskeyCeremony BeginAssertion(IEnumerable<PasskeyDescriptor> allowed) => throw new NotSupportedException();
        public PasskeyAssertionIdentity? ReadAssertionIdentity(string response) => throw new NotSupportedException();
        public Task<PasskeyAssertion> CompleteAssertionAsync(string state, string response, PasskeyStoredKey stored, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
#endif
