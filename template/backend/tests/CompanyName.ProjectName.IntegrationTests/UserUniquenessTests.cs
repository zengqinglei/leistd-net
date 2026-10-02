#if (LocalIdentity)
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 用户名与邮箱的可用性判定必须和数据库实际允许的插入一致。
/// </summary>
/// <remarks>
/// 两者的唯一索引都没有排除 <c>IsDeleted</c>，被删用户仍然占着名字和邮箱，
/// 而仓储默认把软删除行过滤掉。可用性判定不关掉这层过滤就会答"可用"，随后落库撞唯一索引：
/// 用户看到 500，而不是"已被占用"。
/// </remarks>
public sealed class UserUniquenessTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>
{
    private const string Password = "UniquenessTests!Passw0rd";

    [Fact]
    public async Task A_username_and_email_held_by_a_deleted_user_are_reported_as_taken()
    {
        var username = $"uniq_{Guid.NewGuid():N}"[..24];
        var email = $"{username}@example.test";

        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var id = await CreateAsync(admin, username, email);
        using (var delete = await admin.Client.DeleteAsync($"/api/v1/users/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        // 同名：数据库仍然占着，必须是业务错误而不是 500
        using (var sameUsername = await PostAsync(admin, username, $"other_{username}@example.test"))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameUsername.StatusCode);
            Assert.Equal("User:UsernameTaken", await ErrorCodeAsync(sameUsername));
        }

        // 同邮箱：同理
        using (var sameEmail = await PostAsync(admin, $"other_{username}"[..24], email))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
            Assert.Equal("User:EmailTaken", await ErrorCodeAsync(sameEmail));
        }
    }

    /// <summary>改资料走的是排除自身的那两个重载，同样要看见软删除行。</summary>
    [Fact]
    public async Task Updating_a_profile_to_a_deleted_users_name_or_email_is_reported_as_taken()
    {
        var taken = $"uniq_{Guid.NewGuid():N}"[..24];
        var takenEmail = $"{taken}@example.test";
        var mine = $"uniq_{Guid.NewGuid():N}"[..24];

        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var id = await CreateAsync(admin, taken, takenEmail);
        using (var delete = await admin.Client.DeleteAsync($"/api/v1/users/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        }

        await CreateAsync(admin, mine, $"{mine}@example.test");
        using var session = await ProjectWebApplicationFactory.LoginAsync(factory, mine, Password);

        using (var sameUsername = await session.Client.PutAsJsonAsync("/api/v1/auth/me", new
        {
            Username = taken,
            Email = $"{mine}@example.test"
        }))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameUsername.StatusCode);
            Assert.Equal("User:UsernameTaken", await ErrorCodeAsync(sameUsername));
        }

        using (var sameEmail = await session.Client.PutAsJsonAsync("/api/v1/auth/me", new
        {
            Username = mine,
            Email = takenEmail
        }))
        {
            Assert.Equal(HttpStatusCode.Conflict, sameEmail.StatusCode);
            Assert.Equal("User:EmailTaken", await ErrorCodeAsync(sameEmail));
        }
    }

    private async Task<Guid> CreateAsync(AuthenticatedSession admin, string username, string email)
    {
        using var create = await PostAsync(admin, username, email);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> PostAsync(AuthenticatedSession admin, string username, string email) =>
        admin.Client.PostAsJsonAsync("/api/v1/users", new
        {
            Username = username,
            Email = email,
            Password,
            IsActive = true
        });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
#endif
