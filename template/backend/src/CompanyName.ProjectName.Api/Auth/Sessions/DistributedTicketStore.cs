using System.Security.Cryptography;
using Leistd.Lock.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.Api.Auth.Sessions;

/// <summary>浏览器只带会话引用，票据与 OAuth 令牌经数据保护后留在服务端缓存。</summary>
public sealed class DistributedTicketStore(IDistributedCache cache, IDataProtectionProvider protection, TimeProvider clock, IDistributedLock locks) : ITicketStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("CompanyName.ProjectName.AuthTicket.v1");

    private const string ReferenceVersion = "ticket.reference.version";

    public static void ConfigureCookie(CookieAuthenticationOptions cookie)
    {
        cookie.CookieManager = new ReferenceCookieManager(cookie.CookieManager ?? new ChunkingCookieManager(), cookie);
        var signingIn = cookie.Events.OnSigningIn;
        cookie.Events.OnSigningIn = async context =>
        {
            await signingIn(context);
            // 官方处理器显式再次登录会复用缓存键；随机版本使旧 Cookie 不指向新会话。
            context.Properties.Items[ReferenceVersion] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        };
    }

    private sealed class ReferenceCookieManager(ICookieManager inner, CookieAuthenticationOptions options) : ICookieManager
    {
        public string? GetRequestCookie(HttpContext context, string key) => inner.GetRequestCookie(context, key);
        public void DeleteCookie(HttpContext context, string key, CookieOptions cookieOptions) => inner.DeleteCookie(context, key, cookieOptions);
        public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions cookieOptions)
        {
            var reference = value is null ? null : options.TicketDataFormat.Unprotect(value);
            if (reference is not null && context.Items.TryGetValue(ReferenceVersion + ":" + reference.AuthenticationScheme, out var version))
            {
                // SessionStore 默认输出的引用票据不含 Properties；这里只附加引用版本，不附加完整票据。
                reference.Properties.Items[ReferenceVersion] = (string)version!;
                value = options.TicketDataFormat.Protect(reference);
            }
            inner.AppendResponseCookie(context, key, value, cookieOptions);
        }
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        httpContext.Items[ReferenceVersion + ":" + ticket.AuthenticationScheme] = ticket.Properties.Items[ReferenceVersion];
        return await StoreAsync(ticket);
    }

    private static AuthenticationTicket? ReadReference(HttpContext context, string scheme)
    {
        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var value = options.CookieManager.GetRequestCookie(context, options.Cookie.Name!);
        return value is null ? null : options.TicketDataFormat.Unprotect(value);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var ticket = await RetrieveAsync(key);
        if (ticket is null) return null;
        var reference = ReadReference(httpContext, ticket.AuthenticationScheme);
        return reference is not null &&
            reference.Properties.Items.TryGetValue(ReferenceVersion, out var version) &&
            ticket.Properties.Items.TryGetValue(ReferenceVersion, out var expected) && version == expected ? ticket : null;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var reference = ReadReference(httpContext, ticket.AuthenticationScheme);
        httpContext.Items[ReferenceVersion + ":" + ticket.AuthenticationScheme] = ticket.Properties.Items[ReferenceVersion];
        var previous = reference is not null && reference.Properties.Items.TryGetValue(ReferenceVersion, out var prior) ? prior : null;
        ticket.Properties.Items.TryGetValue(ReferenceVersion, out var version);
        if (version is not null && version != previous)
        {
            await using var handle = await locks.LockAsync(key + ":ticket", cancellationToken);
            await WriteAsync(key, ticket, handle.LockLost);
        }
        else await RenewAsync(key, ticket);
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = "CompanyName.ProjectName:AuthTicket:" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ticket.Properties.Items["ticket.key"] = key;
        await WriteAsync(key, ticket, CancellationToken.None);
        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        await using var handle = await locks.LockAsync(key + ":ticket");
        var current = await RetrieveAsync(key);
        if (current is not null &&
            current.Properties.Items.TryGetValue(ReferenceVersion, out var currentVersion) &&
            ticket.Properties.Items.TryGetValue(ReferenceVersion, out var renewingVersion) && currentVersion == renewingVersion)
            await WriteAsync(key, ticket, handle.LockLost);
        // 已退出或过期的引用不能被在飞请求的滑动续期复活。
    }

    private Task WriteAsync(string key, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        ticket.Properties.Items["ticket.key"] = key;
        return cache.SetAsync(key,
        _protector.Protect(TicketSerializer.Default.Serialize(ticket)),
        new DistributedCacheEntryOptions { AbsoluteExpiration = ticket.Properties.ExpiresUtc ?? clock.GetUtcNow().AddMinutes(5) }, cancellationToken);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var bytes = await cache.GetAsync(key);
        if (bytes is null) return null;
        try { return TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes)); }
        catch (CryptographicException) { return null; }
    }

    public async Task RemoveAsync(string key)
    {
        await using var handle = await locks.LockAsync(key + ":ticket");
        await cache.RemoveAsync(key, handle.LockLost);
    }

    public async Task RemoveAsync(string key, HttpContext httpContext, CancellationToken cancellationToken)
    {
        await using var handle = await locks.LockAsync(key + ":ticket", cancellationToken);
        var current = await RetrieveAsync(key);
        if (current is null) return;
        var reference = ReadReference(httpContext, current.AuthenticationScheme);
        if (reference is not null &&
            reference.Properties.Items.TryGetValue(ReferenceVersion, out var version) &&
            current.Properties.Items.TryGetValue(ReferenceVersion, out var expected) && version == expected)
            await cache.RemoveAsync(key, handle.LockLost);
        // 在飞的旧请求只能删除自己引用的版本，不能撤销显式再次登录的新票据。
    }
}
