#if (LocalIdentity)
using Leistd.Authorization;
using System.Net;
using System.Net.Http.Json;
using CompanyName.ProjectName.Application.Users.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Leistd.Authorization.Abstractions;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 授权类集成测试的共享装配：建用户、授予权限，以及两处共用的常量。
/// </summary>
/// <remarks>
/// <para>提到基类是因为这些装配被<b>两批剪裁归属不同</b>的用例共用：
/// <c>AuthorizationAndAuditingTests</c> 覆盖权限、审计、撤权等横切主题，只要有本地身份就成立；
/// 另一批（仅在签发令牌的形态下存在）需要一个能签发令牌的授权服务器。</para>
/// <para>两批必须分文件（否则关掉 OIDC 时没法只剪掉后者），但装配相同——
/// 复制一份会让"改一处忘另一处"成为常态。</para>
/// </remarks>
public abstract class AuthorizationTestBase(ProjectWebApplicationFactory factory)
{
    /// <summary>
    /// 测试用户口令。必须满足服务端 <c>PasswordPolicy</c>：不满足时失败会出现在
    /// "创建测试用户"这种与被测行为无关的地方，很难一眼看出根因
    /// </summary>
    protected const string TestPassword = "IntegrationTests!User";

    /// <summary>授权码流程的回调地址。必须是 https——OIDC 端点只收 https</summary>
    protected const string RedirectUri = "https://localhost/callback";

    /// <summary>供派生类访问宿主工厂</summary>
    protected ProjectWebApplicationFactory Factory { get; } = factory;

    /// <summary>直接经管理器授予权限，绕过 HTTP 层——用于铺设前置状态，不作为被测路径</summary>
    protected async Task GrantAsync(string providerName, Guid providerKey, params string[] permissions)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IPermissionGrantManager>();
        foreach (var permission in permissions)
        {
            await manager.GrantAsync(permission, providerName, providerKey.ToString());
        }
    }

    /// <summary>经 API 建一个测试用户。失败时把状态码与响应体一起报出，避免只看到断言假</summary>
    protected static async Task<UserManagementOutputDto> CreateUserAsync(
        HttpClient client,
        List<Guid>? roleIds = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var response = await client.PostAsJsonAsync(
            "/api/v1/users",
            new CreateUserInputDto
            {
                Username = $"user_{suffix}",
                Email = $"user_{suffix}@example.test",
                DisplayName = "Integration user",
                Password = TestPassword,
                IsActive = true,
                RoleIds = roleIds ?? []
            });

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Create user failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<UserManagementOutputDto>())!;
    }
}
#endif
