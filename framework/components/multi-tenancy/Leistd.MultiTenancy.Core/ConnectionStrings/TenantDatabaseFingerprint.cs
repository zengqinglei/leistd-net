using System.Security.Cryptography;
using System.Text;

namespace Leistd.MultiTenancy.ConnectionStrings;

/// <summary>
/// 连接配置的指纹：判定"两处用的是不是同一份连接配置"。
/// </summary>
/// <remarks>
/// <para>算法只此一处：宿主库、迁移目标与运行时库目录都用它，算出来的指纹才可比。
/// 指纹不可逆推连接串，可以安全地出现在日志、接口响应与运维面板里。</para>
/// <para><b>判的是连接配置相同，不是物理库相同。</b>同一个库若用不同凭据连、或键值对顺序不同、
/// 或写了等价但不同形的主机名，都会得到不同指纹，于是被当成两个库，逐库作业会跑两遍。
/// 当前按"一个库一份连接配置"使用，这个近似成立；真出现多凭据连同一个库的场景时，
/// 应当引入可替换的指纹提供器按提供器规范化，而不是在这里堆特例。</para>
/// </remarks>
public static class TenantDatabaseFingerprint
{
    /// <summary>算连接串的指纹。</summary>
    /// <param name="connectionString">连接串。</param>
    public static string Of(string connectionString)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
}
