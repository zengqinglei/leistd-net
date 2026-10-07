using System.Security.Cryptography;
using CompanyName.ProjectName.Domain.Shared.Security.PasswordHash;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.Extensions.Options;
using System.Buffers.Binary;

namespace CompanyName.ProjectName.Infrastructure.Shared.Security.PasswordHash;

/// <summary>
/// 口令哈希：PBKDF2-HMAC-SHA256，<b>带格式版本与工作因子</b>
/// </summary>
/// <remarks>
/// 密文自描述格式为 <c>[版本][大端迭代数][16 字节盐][32 字节哈希]</c>，
/// 校验按密文记录的参数重算。新密文的迭代数取 <see cref="PasswordHashOptions"/>。
/// 未知版本或损坏的密文按不匹配处理。
/// </remarks>
public class PasswordHasher(IOptions<PasswordHashOptions> options) : IPasswordHasher
{
    /// <summary>格式版本。改变布局或算法时递增，校验按密文里记录的版本分派</summary>
    private const byte FormatVersion = 1;

    private const int SaltSize = 16;    // 128 bits
    private const int HashSize = 32;    // 256 bits
    private const int HeaderSize = 1 + 4;

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var iterations = options.Value.IterationCount;
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Derive(password, salt, iterations);

        var payload = new byte[HeaderSize + SaltSize + HashSize];
        payload[0] = FormatVersion;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, 4), iterations);
        salt.CopyTo(payload.AsSpan(HeaderSize, SaltSize));
        hash.CopyTo(payload.AsSpan(HeaderSize + SaltSize, HashSize));

        return Convert.ToBase64String(payload);
    }

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(hashedPassword);
        ArgumentException.ThrowIfNullOrEmpty(providedPassword);

        // 格式损坏一律按"不匹配"处理，不上抛：这条路径直接面向登录请求，
        // 抛异常会把"这条哈希坏了"变成 500，还能被用来区分账号是否存在
        if (!TryRead(hashedPassword, out var iterations, out var salt, out var storedHash))
        {
            return false;
        }

        // 按密文里记录的迭代数重算，而不是当前配置——否则调高成本会让存量口令全部失效
        var providedHash = Derive(providedPassword, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(storedHash, providedHash);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        KeyDerivation.Pbkdf2(
            password: password,
            salt: salt,
            prf: KeyDerivationPrf.HMACSHA256,
            iterationCount: iterations,
            numBytesRequested: HashSize);

    private static bool TryRead(
        string hashedPassword,
        out int iterations,
        out byte[] salt,
        out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(hashedPassword);
        }
        catch (FormatException)
        {
            return false;
        }

        if (payload.Length != HeaderSize + SaltSize + HashSize || payload[0] != FormatVersion)
        {
            return false;
        }

        iterations = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4));
        if (iterations <= 0)
        {
            return false;
        }

        salt = payload.AsSpan(HeaderSize, SaltSize).ToArray();
        hash = payload.AsSpan(HeaderSize + SaltSize, HashSize).ToArray();
        return true;
    }
}
