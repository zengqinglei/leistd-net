namespace CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;

/// <summary>
/// 口令哈希的工作因子（配置节 <c>PasswordHash</c>）
/// </summary>
/// <remarks>
/// <para>只影响新算出的密文：密文自带迭代数，校验按密文记录的值重算，调整后存量口令照常可用。</para>
/// <para>默认取 OWASP 对 PBKDF2-HMAC-SHA256 的现行建议值；部署可依据硬件基准调高。
/// 测试宿主为缩短登录与播种耗时调低，与 ASP.NET Core Identity 的 <c>PasswordHasherOptions.IterationCount</c> 同一做法。</para>
/// </remarks>
public sealed class PasswordHashOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "PasswordHash";

    /// <summary>PBKDF2 迭代次数，必须大于 0</summary>
    public int IterationCount { get; set; } = 600_000;
}
