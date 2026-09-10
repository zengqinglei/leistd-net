using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Api.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using Xunit;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>
/// 设置接口的层级与值域契约。
/// </summary>
/// <remarks>
/// 重点在<b>进程级设置</b>（<c>SettingScopes.Host</c>）：日志级别这类东西一个进程只有一份，
/// 按租户各存一份根本无从生效。这里钉住三件事——租户上下文既看不到也改不了它、
/// 宿主能改、以及值域在服务端把关。少了任何一条，界面都会出现"改了却不生效"，
/// 而那是最难从现象反推回原因的一类问题。
/// </remarks>
public sealed class SettingsContractTests(ProjectWebApplicationFactory factory)
    : IClassFixture<ProjectWebApplicationFactory>, IDisposable
{
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// 另起一个宿主，只改几个配置键；数据库与基础工厂共用。
    /// </summary>
    /// <remarks>
    /// 每个字典各成一个配置源，<b>越靠后优先级越高</b>——配置合并的优先级只有多个源才测得出来。
    /// </remarks>
    private WebApplicationFactory<Program> HostWith(params Dictionary<string, string?>[] sources)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            foreach (var source in sources)
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(source));
            }
        });
        _disposables.Add(host);
        return host;
    }

    /// <summary>宿主此刻真正生效的全局最小级别。</summary>
    /// <remarks>
    /// 读的是那个 <c>LoggingLevelSwitch</c>——它正是 <c>MinimumLevel.ControlledBy</c> 接管
    /// 全局级别的东西，本轮要钉的就是"它别把部署基线顶掉"。只断言接口下发的
    /// <c>defaultValue</c> 不够：那只证明界面显示对了，证明不了 logger 真的按这个级别打。
    /// <para>
    /// 刻意不走 <c>ILoggerFactory.IsEnabled</c>：Serilog 的 <c>Log.Logger</c> 是<b>静态</b>的，
    /// 别的测试类的宿主释放时会把它关掉，于是那种断言在单独跑这一个类时是绿的、
    /// 全量跑就红——结果取决于测试类的执行与释放顺序，而不是被测行为。
    /// </para>
    /// </remarks>
    private static LogEventLevel EffectiveMinimumLevel(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<LoggingSettingState>().MinimumLevel.MinimumLevel;

    private static async Task<JsonElement> ReadSettingAsync(HttpClient client, string name)
    {
        var listed = await client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        return listed.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
    }

    /// <summary>取某一层的覆盖值；没有覆盖值时为 <see langword="null"/>。</summary>
    /// <remarks>
    /// 宿主的 JSON 选项会把值为 null 的属性整个省掉，所以"这一层没设过"在响应里有两种形态：
    /// 属性缺失、或属性是 JSON null。直接 <c>GetProperty</c> 会在前一种形态下抛
    /// <c>KeyNotFoundException</c>，那看起来像契约缺字段，其实只是没有覆盖值。
    /// </remarks>
    private static string? OverrideOrNull(JsonElement setting, string property)
        => setting.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.GetString()
            : null;

    private static Task<HttpResponseMessage> WriteAsync(HttpClient client, string name, string? value)
        => client.PutAsJsonAsync("/api/v1/settings/current-tenant", new { Name = name, Value = value });

    /// <summary>
    /// 日志级别的默认值来自部署基线，清除覆盖值即回落到它。
    /// </summary>
    /// <remarks>
    /// 分工是"部署配置给基线、设置表给运行期覆盖"。若把默认值写死成 Information，
    /// 部署把 <c>Serilog:MinimumLevel:Default</c> 配成 Warning 也会被顶掉，
    /// 而清除覆盖值同样回不到部署基线——两者都表现为"配置文件不起作用"。
    /// </remarks>
    [Fact]
    public async Task 日志级别的默认值取自部署基线_清除覆盖后回落到它()
    {
        var host = HostWith(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel:Default"] = "Warning"
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        // 先清一次：同一套库被本类其它用例写过，不清就依赖用例执行顺序
        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);

        var baseline = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Equal("Warning", baseline.GetProperty("defaultValue").GetString());
        Assert.Null(OverrideOrNull(baseline, "tenantValue"));

        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, "Debug")).StatusCode);
        var overridden = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Equal("Debug", OverrideOrNull(overridden, "tenantValue"));

        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);
        var cleared = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Null(OverrideOrNull(cleared, "tenantValue"));
        Assert.Equal("Warning", cleared.GetProperty("defaultValue").GetString());
    }

    // Serilog 的最小级别有两种合法写法，标量那种也要认出来——
    // 只读 MinimumLevel:Default 的话，写成 "MinimumLevel": "Error" 的部署会被当成没配。
    [Fact]
    public async Task 部署基线也认标量写法的最小级别()
    {
        var host = HostWith(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"] = "Error",
            // 置空模板 appsettings 里的对象写法，只留标量这一种
            ["Serilog:MinimumLevel:Default"] = null
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var level = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);

        Assert.Equal("Error", level.GetProperty("defaultValue").GetString());
    }

    /// <summary>
    /// 两种写法并存时按<b>配置源优先级</b>裁决，而不是固定优先某一种。
    /// </summary>
    /// <remarks>
    /// 这是最常见的真实形态：模板 <c>appsettings.json</c> 里已有对象写法，部署再用环境变量
    /// 给出标量写法。固定优先对象写法，就会让文件里的级别顶掉环境变量里的——而 Serilog 自己
    /// 用的是环境变量那个，于是 <c>MinimumLevel.ControlledBy</c> 把它正确解析出的级别又改回去，
    /// 表现为"环境变量配的日志级别不起作用"。所以这里连真实 logger 一起断言。
    /// </remarks>
    [Fact]
    public async Task 高优先级配置源的最小级别不被低优先级的另一种写法顶掉()
    {
        // appsettings.json 提供 Serilog:MinimumLevel:Default = Information（低优先级）
        var host = HostWith(new Dictionary<string, string?>
        {
            ["Serilog:MinimumLevel"] = "Error"
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        // 本类其它用例会在这套共用库里写下覆盖值；先清掉，让开关回到基线，
        // 否则这条断言的结果取决于用例执行顺序。清除本身就会触发本宿主的应用器。
        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);

        var level = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);

        Assert.Equal("Error", level.GetProperty("defaultValue").GetString());
        Assert.Equal(LogEventLevel.Error, EffectiveMinimumLevel(host));
    }

    // 反方向同样不能顶掉：更高优先级的源用对象写法时，它才是基线。
    // 只把 ?? 两边换个位置就会在这条上错。
    [Fact]
    public async Task 反向并存时更高优先级的对象写法胜出()
    {
        var host = HostWith(
            new Dictionary<string, string?> { ["Serilog:MinimumLevel"] = "Error" },
            new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Warning" });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        // 本类其它用例会在这套共用库里写下覆盖值；先清掉，让开关回到基线，
        // 否则这条断言的结果取决于用例执行顺序。清除本身就会触发本宿主的应用器。
        Assert.Equal(
            HttpStatusCode.OK,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);

        var level = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);

        Assert.Equal("Warning", level.GetProperty("defaultValue").GetString());
        Assert.Equal(LogEventLevel.Warning, EffectiveMinimumLevel(host));
    }

    /// <summary>
    /// 缺少摘要密钥的部署不允许开启邮箱验证。
    /// </summary>
    /// <remarks>
    /// 启动校验只看配置里的那个布尔值，所以默认部署（关闭邮箱验证、未配密钥）启动是正常的。
    /// 管理员随后在设置页打开时，若写入端只校验布尔值就放行，直到真的发码时才在摘要计算处
    /// 抛 500——这个开关允许进入一个缺少运行前提的状态。密钥仍由配置提供，不进设置表。
    /// </remarks>
    [Fact]
    public async Task 缺少摘要密钥时不允许开启邮箱验证()
    {
        var host = HostWith(new Dictionary<string, string?>
        {
            ["VerificationCodes:Key"] = ""
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var rejected = await WriteAsync(
            admin.Client, SettingConstant.Registration.EnableEmailVerification, "true");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // 关闭仍然允许：没有密钥的部署本来就不需要它，不能连关都关不掉
        var accepted = await WriteAsync(
            admin.Client, SettingConstant.Registration.EnableEmailVerification, "false");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    // 有密钥的部署可以按租户开启：这条挡住"把校验写成一律拒绝"。
    [Fact]
    public async Task 有摘要密钥时可以开启邮箱验证()
    {
        using var admin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        try
        {
            var accepted = await WriteAsync(
                admin.Client, SettingConstant.Registration.EnableEmailVerification, "true");
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        finally
        {
            // 这一项是本类共用那套库里的宿主行，留着 true 会影响其它用例
            await WriteAsync(admin.Client, SettingConstant.Registration.EnableEmailVerification, null);
        }
    }

    /// <summary>租户管理员登录：登录请求与后续请求都携带 X-Tenant-Id。</summary>
    private async Task<HttpClient> LoginTenantAdminAsync(Guid tenantId)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        _disposables.Add(client);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = "Tenant@123456" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    [Fact]
    public async Task 宿主能改进程级设置_并且值域在服务端把关()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var accepted = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Logging.MinimumLevel, Value = "Debug" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        // 界面候选项之外的取值必须被拒：脚本与旧版客户端都绕得过界面，
        // 一个非法级别留在库里，之后每次应用都要靠日志组件自己兜。
        var rejected = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Logging.MinimumLevel, Value = "Chatty" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        // 改完要能读回来，否则"生效"无从确认
        var listed = await hostAdmin.Client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var level = listed.EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == SettingConstant.Logging.MinimumLevel);

        Assert.Equal("Debug", level.GetProperty("tenantValue").GetString());
        // 进程级：两个可覆盖层级都是 false，界面靠 allowsHostScope 才把它归进系统页
        Assert.False(level.GetProperty("allowsTenantScope").GetBoolean());
        Assert.False(level.GetProperty("allowsUserScope").GetBoolean());
        Assert.True(level.GetProperty("allowsHostScope").GetBoolean());

        // 收尾清掉：这一行在本类共用的那套库里，留着会让基线相关的用例依赖执行顺序
        await WriteAsync(hostAdmin.Client, SettingConstant.Logging.MinimumLevel, null);
    }

    /// <summary>
    /// 进程级设置在租户上下文下既不下发也不可写。
    /// </summary>
    /// <remarks>
    /// 下发的话租户管理员会看到一个改不动的项；能写的话那条租户行永远不会被任何 logger 读到，
    /// 界面却把它显示成已生效。两条都要拦。
    /// </remarks>
    [Fact]
    public async Task 租户上下文既看不到也改不了进程级设置()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var name = $"settingscope-{Guid.NewGuid():N}"[..20];
        var created = await hostAdmin.Client.PostAsJsonAsync("/api/v1/tenants", new
        {
            Name = name,
            DisplayName = $"{name} Inc.",
            AdminEmail = $"admin@{name}.example.com",
            AdminPassword = "Tenant@123456"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var tenantId = body.RootElement.GetProperty("id").GetGuid();

        var tenantAdmin = await LoginTenantAdminAsync(tenantId);

        var listed = await tenantAdmin.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        Assert.DoesNotContain(
            listed.EnumerateArray(),
            s => s.GetProperty("name").GetString() == SettingConstant.Logging.MinimumLevel);

        var write = await tenantAdmin.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Logging.MinimumLevel, Value = "Debug" });
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }
}
