#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 会话 Cookie 里的角色能被官方 <c>IsInRole</c> / <c>RequireRole</c> 认出。
/// </summary>
/// <remarks>
/// 角色写成 <c>role</c> claim，身份的 RoleClaimType 必须同名；否则官方判定按默认的
/// <c>ClaimTypes.Role</c> 去找，静默判为不在角色中。探针走真实链路：登录发出 Cookie，
/// 再按会话方案认证（票据反序列化）后判定，而不是检查登录时手里的那个对象。
/// </remarks>
public sealed class SessionRoleClaimTests(ProjectWebApplicationFactory factory)
    : AuthorizationTestBase(factory), IClassFixture<ProjectWebApplicationFactory>
{
    private const string ProbePath = "/__role-probe";

    [Fact]
    public async Task Official_role_checks_recognise_the_roles_in_the_session_cookie()
    {
        var suffix = Guid.CreateVersion7().ToString("N")[..8];
        var roleName = $"auditor_{suffix}";
        using var superAdmin = await Factory.LoginAsync("admin", ProjectWebApplicationFactory.TestAdminPassword);
        var roleId = await CreateRoleAsync(superAdmin.Client, roleName);
        var user = await CreateUserAsync(superAdmin.Client, [roleId]);

        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new RoleProbe())));
        using var session = await ProjectWebApplicationFactory.LoginAsync(host, user.Username, TestPassword);

        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync($"{ProbePath}?role={roleName}")).StatusCode);
        // 反向：没有的角色必须判否，证明探针不是恒为真
        Assert.Equal(HttpStatusCode.Forbidden, (await session.Client.GetAsync($"{ProbePath}?role=other_{suffix}")).StatusCode);
    }

    private static async Task<Guid> CreateRoleAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/roles", new
        {
            name,
            displayName = name,
            sort = 0,
            isDefault = false
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    // 挂在管道最前：自己按会话方案认证，不依赖后面的认证中间件是否已跑
    private sealed class RoleProbe : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map(ProbePath, probe => probe.Run(async context =>
            {
                var role = context.Request.Query["role"].ToString();
                var result = await context.AuthenticateAsync(AuthenticationSchemeNames.SessionCookie);
                var authorization = context.RequestServices.GetRequiredService<IAuthorizationService>();
                var policy = new AuthorizationPolicyBuilder().RequireRole(role).Build();

                var granted = result.Principal is { } principal
                    && principal.IsInRole(role)
                    && (await authorization.AuthorizeAsync(principal, policy)).Succeeded;
                context.Response.StatusCode = granted ? StatusCodes.Status200OK : StatusCodes.Status403Forbidden;
            }));
            next(app);
        };
    }
}
#endif
