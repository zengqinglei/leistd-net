#if (LocalIdentity)
namespace CompanyName.ProjectName.Domain.Auth.VerificationCodes;

/// <summary>
/// 短期验证码的摘要能力
/// </summary>
/// <remarks>
/// <para><b>刻意与口令哈希分开。</b>两者安全语义不同：口令是长期机密、搜索空间大，
/// 需要高成本的自适应哈希抬高离线破解代价；六位验证码只有 10⁶ 空间、寿命几分钟，
/// 再高的工作因子也挡不住穷举——真正起作用的是短有效期 + 尝试次数限制。</para>
/// <para>复用口令哈希器的坏处是双向的：验证码这边白付昂贵的哈希开销（每次校验都跑完整成本），
/// 口令那边被迫接受一个"还要给验证码用"的接口，调成本参数时两种用途互相牵制。</para>
/// <para>这里用带服务端密钥的 HMAC：确定性、可直接比对、开销恒定；
/// 密钥使得数据库泄漏后无法离线枚举 10⁶ 空间反查验证码。</para>
/// </remarks>
public interface IVerificationCodeDigest
{
    /// <summary>计算验证码摘要</summary>
    string Compute(string code);

    /// <summary>恒定时间比对</summary>
    bool Matches(string digest, string code);
}
#endif
