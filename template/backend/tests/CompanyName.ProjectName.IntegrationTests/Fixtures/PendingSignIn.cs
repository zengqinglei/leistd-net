#if (LocalIdentity)
using CompanyName.ProjectName.Api.Auth.Sessions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests.Fixtures;

/// <summary>待完成登录的临时 Cookie：测试客户端不自动管理 Cookie，第一步写下的临时 Cookie 由用例显式带到后续请求。</summary>
/// <remarks>用例手里的"待完成登录"就是这个 Cookie 的 <c>name=value</c>，像浏览器一样只能经 Cookie 头出示。</remarks>
internal static class PendingSignIn
{
    /// <summary>临时 Cookie 在这个宿主上的名字（部署环境带 <c>__Host-Http-</c> 前缀）。</summary>
    public static string CookieName(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(PendingSignInCookie.Scheme).Cookie.Name!;

    /// <summary>响应写下的临时 Cookie（<c>name=value</c>）；同一响应先删旧再写新时取最后写入的那个。没写时返回 null。</summary>
    public static string? Read(WebApplicationFactory<Program> host, HttpResponseMessage response)
    {
        var last = SetCookies(host, response).LastOrDefault();
        return last is null || IsDeletion(last) ? null : last.Split(';', 2)[0];
    }

    /// <summary>响应的最后一个临时 Cookie 写入是删除。</summary>
    public static bool Deletes(WebApplicationFactory<Program> host, HttpResponseMessage response) =>
        SetCookies(host, response).LastOrDefault() is { } last && IsDeletion(last);

    /// <summary>响应里写临时 Cookie 的那几行 Set-Cookie（含删除），按写入顺序。</summary>
    public static IReadOnlyList<string> SetCookies(WebApplicationFactory<Program> host, HttpResponseMessage response)
    {
        var prefix = CookieName(host) + "=";
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith(prefix, StringComparison.Ordinal)).ToList()
            : [];
    }

    /// <summary>带着临时 Cookie 的客户端（还没有会话）。</summary>
    public static HttpClient Client(WebApplicationFactory<Program> host, string pendingCookie)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(host);
        client.DefaultRequestHeaders.Add("Cookie", pendingCookie);
        return client;
    }

    // 官方删除写空值并把过期设到 1970 年
    private static bool IsDeletion(string setCookie) =>
        setCookie.Split(';', 2)[0].EndsWith('=') ||
        setCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
}
#endif
