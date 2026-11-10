using Leistd.MultiTenancy.AspNetCore.Options;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Api.Options;
using Leistd.Settings.Definitions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyName.ProjectName.IntegrationTests;

/// <summary>设置接口的层级与值域契约。</summary>
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

    /// <summary>另起一个宿主，只改几个配置键；数据库与基础工厂共用。</summary>
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

    // 没有类别覆盖的业务类别：只受 Logging:LogLevel:Default 支配
    private const string ProbeCategory = "CompanyName.ProjectName.SettingsContractProbe";

    /// <summary>宿主此刻对某个类别真正生效的最小级别。</summary>
    /// <remarks>
    /// 经宿主容器的日志工厂判定，设置覆盖与清除是否真正改变了内置日志过滤；
    /// 接口的 <c>defaultValue</c> 只能证明展示值，不能证明运行时生效。
    /// </remarks>
    private static LogLevel EffectiveLevel(WebApplicationFactory<Program> host, string category = ProbeCategory)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(category);
        return Enum.GetValues<LogLevel>().First(level => level == LogLevel.None || logger.IsEnabled(level));
    }

    /// <summary>以 Information 写一条探针日志，返回收集提供程序是否收到。</summary>
    private static bool InformationReachesProviders(WebApplicationFactory<Program> host)
    {
        var marker = Guid.NewGuid().ToString("N");
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ProbeCategory).LogInformation("Probe {Marker}", marker);
        return host.Services.GetFakeLogCollector().GetSnapshot()
            .Any(record => record.Category == ProbeCategory && record.GetStructuredStateValue("Marker") == marker);
    }

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

    /// <summary>日志级别的默认值来自部署基线，清除覆盖值即回落到它。</summary>
    /// <remarks>
    /// 分工是"部署配置给基线、设置表给运行期覆盖"。若把默认值写死成 Information，
    /// 部署把 <c>Logging:LogLevel:Default</c> 配成 Warning 也会被顶掉，
    /// 而清除覆盖值同样回不到部署基线——两者都表现为"配置文件不起作用"。
    /// </remarks>
    [Fact]
    public async Task Log_level_default_comes_from_deployment_and_clearing_restores_it()
    {
        var host = HostWith(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning"
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        // 先清一次：同一套库被本类其它用例写过，不清就依赖用例执行顺序
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);

        var baseline = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Equal("Warning", baseline.GetProperty("defaultValue").GetString());
        Assert.Null(OverrideOrNull(baseline, "tenantValue"));

        Assert.Equal(LogLevel.Warning, EffectiveLevel(host));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, "Debug")).StatusCode);
        var overridden = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Equal("Debug", OverrideOrNull(overridden, "tenantValue"));
        // 写入即生效：设置经配置源覆盖 Logging:LogLevel:Default，内置日志过滤随配置重载
        Assert.Equal(LogLevel.Debug, EffectiveLevel(host));

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);
        var cleared = await ReadSettingAsync(admin.Client, SettingConstant.Logging.MinimumLevel);
        Assert.Null(OverrideOrNull(cleared, "tenantValue"));
        Assert.Equal("Warning", cleared.GetProperty("defaultValue").GetString());
        Assert.Equal(LogLevel.Warning, EffectiveLevel(host));
    }

    /// <summary>模板配置下，最小级别写入即过滤、清除回到部署配置；按类别列出的覆盖不受它影响。</summary>
    [Fact]
    public async Task Template_log_level_follows_the_setting_without_touching_category_overrides()
    {
        // 不注入级别键：本用例保护 appsettings.json 提供的基线与类别覆盖
        var host = HostWith();
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);
        try
        {
            Assert.Equal(LogLevel.Information, EffectiveLevel(host));
            Assert.True(InformationReachesProviders(host));
            Assert.Equal(LogLevel.Warning, EffectiveLevel(host, "Microsoft.EntityFrameworkCore.Database.Command"));
            Assert.Equal(LogLevel.Information, EffectiveLevel(host, "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware"));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, "Warning")).StatusCode);
            Assert.Equal(LogLevel.Warning, EffectiveLevel(host));
            Assert.False(InformationReachesProviders(host));
            // 显式类别覆盖不随全局级别变化：请求完成日志仍是 Information
            Assert.Equal(LogLevel.Information, EffectiveLevel(host, "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware"));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, "Trace")).StatusCode);
            Assert.Equal(LogLevel.Trace, EffectiveLevel(host));
            Assert.Equal(LogLevel.Warning, EffectiveLevel(host, "Microsoft.EntityFrameworkCore.Database.Command"));

            Assert.Equal(
                HttpStatusCode.NoContent,
                (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null)).StatusCode);
            Assert.Equal(LogLevel.Information, EffectiveLevel(host));
            Assert.True(InformationReachesProviders(host));
        }
        finally
        {
            await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, null);
        }
    }

    /// <summary>候选值是 <c>LogLevel</c> 的名称；旧取值域与 <c>None</c> 写不进去。</summary>
    [Theory]
    [InlineData("Verbose")]
    [InlineData("Fatal")]
    [InlineData("None")]
    public async Task Minimum_level_rejects_values_outside_the_log_level_names(string value)
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await WriteAsync(admin.Client, SettingConstant.Logging.MinimumLevel, value)).StatusCode);
    }

    // 请求日志开关经配置源进 Options：写入即生效，清除回到类型默认值（开启）
    [Fact]
    public async Task Request_logging_follows_the_setting_and_resets_to_enabled()
    {
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            factory, "admin", ProjectWebApplicationFactory.TestAdminPassword);
        var monitor = factory.Services.GetRequiredService<IOptionsMonitor<RequestLoggingOptions>>();

        var baseline = await ReadSettingAsync(admin.Client, SettingConstant.Logging.RequestLogging);
        Assert.Equal("true", baseline.GetProperty("defaultValue").GetString());
        Assert.True(baseline.GetProperty("isBoolean").GetBoolean());

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.RequestLogging, "false")).StatusCode);
        Assert.False(monitor.CurrentValue.Enabled);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await WriteAsync(admin.Client, SettingConstant.Logging.RequestLogging, null)).StatusCode);
        Assert.True(monitor.CurrentValue.Enabled);
    }

#if (Email)
    /// <summary>缺少摘要密钥的部署不允许开启邮箱验证。</summary>
    /// <remarks>
    /// 启动校验只看配置里的那个布尔值，所以默认部署（关闭邮箱验证、未配密钥）启动是正常的。
    /// 管理员随后在设置页打开时，若写入端只校验布尔值就放行，直到真的发码时才在摘要计算处
    /// 抛 500——这个开关允许进入一个缺少运行前提的状态。密钥仍由配置提供，不进设置表。
    /// </remarks>
    [Fact]
    public async Task Email_verification_cannot_be_enabled_without_a_digest_key()
    {
        var host = HostWith(new Dictionary<string, string?>
        {
            ["Leistd:Security:VerificationCodes:Key"] = ""
        });
        using var admin = await ProjectWebApplicationFactory.LoginAsync(
            host, "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var rejected = await WriteAsync(
            admin.Client, SettingConstant.Registration.EnableEmailVerification, "true");
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // 关闭仍然允许：没有密钥的部署本来就不需要它，不能连关都关不掉
        var accepted = await WriteAsync(
            admin.Client, SettingConstant.Registration.EnableEmailVerification, "false");
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
    }

    // 有密钥的部署可以按租户开启：这条挡住"把校验写成一律拒绝"。
    [Fact]
    public async Task Email_verification_can_be_enabled_with_a_digest_key()
    {
        using var admin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        try
        {
            var accepted = await WriteAsync(
                admin.Client, SettingConstant.Registration.EnableEmailVerification, "true");
            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
        }
        finally
        {
            // 这一项是本类共用那套库里的宿主行，留着 true 会影响其它用例
            await WriteAsync(admin.Client, SettingConstant.Registration.EnableEmailVerification, null);
        }
    }
#endif

    /// <summary>租户管理员登录：登录请求与后续请求都携带租户提示头。</summary>
    private async Task<HttpClient> LoginTenantAdminAsync(Guid tenantId)
    {
        var client = ProjectWebApplicationFactory.CreateProjectClient(factory);
        _disposables.Add(client);
        client.DefaultRequestHeaders.Add(MultiTenancyOptions.DefaultHeaderName, tenantId.ToString());

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/session-login",
            new { UsernameOrEmail = "admin", Password = "Tenant@123456" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0]));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

#if (LocalIdentity)
    /// <summary>布尔型设置由服务端标注，界面据此渲染开关；写入同样按这份清单把关。</summary>
    /// <remarks>界面各记一份清单时，漏登记的布尔设置会渲染成要手打 true 的文本框。</remarks>
    [Fact]
    public async Task Boolean_settings_are_flagged_and_accept_only_true_or_false()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var listed = await hostAdmin.Client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var flagged = listed.EnumerateArray()
            .Where(s => s.TryGetProperty("isBoolean", out var b) && b.GetBoolean())
            .Select(s => s.GetProperty("name").GetString()!)
            .ToHashSet();

        // 期望集合取自定义本身：值域声明在定义上，界面与写入端读的是同一份
        var expected = factory.Services.GetRequiredService<ISettingDefinitionManager>().GetAll()
            .Where(d => d.IsVisibleToClients && d.ValueType == SettingValueType.Boolean)
            .Select(d => d.Name)
            .ToHashSet();
        Assert.Contains(SettingConstant.Security.RequireTwoFactor, expected);
        Assert.Equal(expected, flagged);

        var rejected = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Security.RequireTwoFactor, Value = "yes" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

#endif
    /// <summary>系统设置的面板顺序取接口里各分组首次出现的先后：显示在最前（与个人偏好一致），其余按管理员的使用频率排，运维与数据保留在最后。</summary>
    [Fact]
    public async Task System_setting_groups_are_listed_in_panel_order()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var listed = await hostAdmin.Client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var groups = listed.EnumerateArray()
            .Where(s => s.GetProperty("allowsTenantScope").GetBoolean() || s.GetProperty("allowsHostScope").GetBoolean())
            .Select(s => s.GetProperty("group").GetString()!)
            .Distinct()
            .ToList();

        List<string> expected = [SettingConstant.Groups.Display];
#if (LocalIdentity)
        expected.Add(SettingConstant.Groups.Registration);
        expected.Add(SettingConstant.Groups.Security);
#if (ExternalLogin)
        expected.Add(SettingConstant.Groups.ExternalLogin);
#endif
#if (Email)
        expected.Add(SettingConstant.Groups.Email);
#endif
#endif
        expected.Add(SettingConstant.Groups.Operations);
#if (IncludeOperationRecords || IncludeNotifications)
        expected.Add(SettingConstant.Groups.Retention);
#endif
        Assert.Equal(expected, groups);
    }

    /// <summary>个人偏好的"显示"分组：先放影响能否读懂的语言，再放影响数据解读的时区，最后是外观。</summary>
    [Fact]
    public async Task Personal_display_settings_are_listed_language_time_zone_then_appearance()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var listed = await hostAdmin.Client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var display = listed.EnumerateArray()
            .Where(s => s.GetProperty("group").GetString() == SettingConstant.Groups.Display
                && s.GetProperty("allowsUserScope").GetBoolean())
            .Select(s => s.GetProperty("name").GetString()!)
            .ToList();

        List<string> expected = [];
#if (IncludeLocalization)
        expected.Add(SettingConstant.Display.Language);
#endif
        expected.Add(SettingConstant.Display.TimeZone);
        expected.Add(SettingConstant.Display.Theme);
        Assert.Equal(expected, display);
    }

    /// <summary>外观只有个人层：本人可选三种取值之一，组织层写不进去。</summary>
    [Fact]
    public async Task Appearance_is_a_personal_setting_limited_to_the_three_modes()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var accepted = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-user",
            new { Name = SettingConstant.Display.Theme, Value = "dark" });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var listed = await hostAdmin.Client.GetFromJsonAsync<JsonElement>("/api/v1/settings");
        var theme = listed.EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == SettingConstant.Display.Theme);
        Assert.Equal("dark", theme.GetProperty("userValue").GetString());
        Assert.True(!theme.TryGetProperty("defaultValue", out var defaultTheme) || defaultTheme.ValueKind == JsonValueKind.Null);
        Assert.False(theme.GetProperty("allowsTenantScope").GetBoolean());

        var unknown = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-user",
            new { Name = SettingConstant.Display.Theme, Value = "sepia" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        var organization = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Display.Theme, Value = "dark" });
        Assert.Equal(HttpStatusCode.BadRequest, organization.StatusCode);

        // 收尾清掉：这一行在本类共用的那套库里
        var cleared = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-user",
            new { Name = SettingConstant.Display.Theme, Value = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
    }

    [Fact]
    public async Task Host_can_change_process_settings_within_the_allowed_range()
    {
        using var hostAdmin = await factory.LoginAsync(
            "admin", ProjectWebApplicationFactory.TestAdminPassword);

        var accepted = await hostAdmin.Client.PutAsJsonAsync(
            "/api/v1/settings/current-tenant",
            new { Name = SettingConstant.Logging.MinimumLevel, Value = "Debug" });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

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
#if (IncludeMultiTenancy)

    /// <summary>进程级设置在租户上下文下既不下发也不可写。</summary>
    /// <remarks>
    /// 下发的话租户管理员会看到一个改不动的项；能写的话那条租户行永远不会被任何 logger 读到，
    /// 界面却把它显示成已生效。两条都要拦。
    /// </remarks>
    [Fact]
    public async Task Tenant_context_can_neither_see_nor_change_process_settings()
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
#endif

}
