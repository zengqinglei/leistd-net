#if (LocalIdentity)
using System.Text;
using CompanyName.ProjectName.Domain.Shared.Security.OneTimeCodes;
using CompanyName.ProjectName.Domain.Shared.Text;

namespace CompanyName.ProjectName.UnitTests.Domain;

/// <summary>
/// TOTP 自己实现，所以按 RFC 6238 附录 B 的测试向量核对（取 8 位结果的末 6 位）。
/// </summary>
public class TotpTests
{
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Matches_the_rfc_6238_test_vectors(long unixSeconds, string expected)
    {
        var at = DateTime.UnixEpoch.AddSeconds(unixSeconds);

        Assert.Equal(expected, Totp.ComputeCode(RfcSecret, Totp.TimeStepAt(at)));
    }

    [Fact]
    public void Accepts_one_step_of_drift_either_way_and_rejects_further()
    {
        var now = DateTime.UnixEpoch.AddSeconds(1234567890);
        var step = Totp.TimeStepAt(now);

        Assert.Equal(step - 1, Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 1), now, null));
        Assert.Equal(step + 1, Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step + 1), now, null));
        Assert.Null(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step - 2), now, null));
    }

    [Fact]
    public void A_step_already_used_is_not_accepted_again()
    {
        var now = DateTime.UnixEpoch.AddSeconds(1234567890);
        var step = Totp.TimeStepAt(now);

        Assert.Null(Totp.Verify(RfcSecret, Totp.ComputeCode(RfcSecret, step), now, lastUsedStep: step));
    }

    [Fact]
    public void Base32_round_trips_and_ignores_case_and_spacing()
    {
        var secret = Totp.GenerateSecret();
        var encoded = Base32.Encode(secret);

        Assert.Equal(secret, Base32.Decode(encoded));
        Assert.Equal(secret, Base32.Decode(string.Join(' ', encoded.ToLowerInvariant().Chunk(4).Select(c => new string(c)))));
        Assert.Null(Base32.Decode("not*base32"));
    }

    [Fact]
    public void Recovery_codes_match_regardless_of_case_and_separators()
    {
        var code = RecoveryCodes.Generate()[0];

        Assert.Equal(RecoveryCodes.Hash(code), RecoveryCodes.Hash(code.ToUpperInvariant().Replace("-", " ")));
    }
}
#endif
