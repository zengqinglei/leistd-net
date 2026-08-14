using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Api.HostedServices.Initializer;

using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Domain.Users.Options;
using CompanyName.ProjectName.Domain.Shared.Json;
using CompanyName.ProjectName.Infrastructure;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.DependencyInjection.DynamicProxy;
using Leistd.Exception.AspNetCore;
#if (IncludeLocalization)
using CompanyName.ProjectName.Api;
#if (IncludeRoles)
using CompanyName.ProjectName.Application.Permissions.AppServices;
#endif
using Leistd.Localization.AspNetCore;
#endif
using Leistd.Security.AspNetCore;
using Leistd.Tracing.AspNetCore;
#if (IncludeRoles)
using Leistd.Authorization.AspNetCore;
#endif
#if (IncludeTenancy)
using Leistd.MultiTenancy;
#endif
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.SignalR;
using Leistd.RealTime;
using Leistd.RealTime.AspNetCore.SignalR;
#endif
#if (IncludeIdentity)
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
#if (IncludeOpenIddict || IncludeExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Options;
#endif
#if (IncludeOpenIddict)
using System.Security.Cryptography.X509Certificates;
using Leistd.ServiceClient.AspNetCore;
using Leistd.ServiceClient.Constants;
using OpenIddict.Abstractions;
#endif
#endif
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Authorization;
#if (IncludeIdentity)
using CompanyName.ProjectName.Domain.Shared.Email.Options;
#endif
using Microsoft.Extensions.FileProviders;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting web application ...");
    var builder = WebApplication.CreateBuilder(args);

    // 1. 基础架构设置 (DI Factory, Logging, WebServer, HttpClient)
    builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory());

    builder.AddMyProjectInfrastructure();
    builder.Services.AddMyProjectWebServer();

    // 2. 业务层服务注册 (Domain -> Infrastructure -> Application)
    builder.Services.AddDomainServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApplicationServices();

    // 2.5. 配置选项
    builder.Services.AddOptions<DefaultAdminOptions>()
        .Bind(builder.Configuration.GetSection(DefaultAdminOptions.SectionName));
#if (IncludeIdentity)
#if (IncludeOpenIddict)
    builder.Services.AddOptions<OAuthOptions>()
        .Bind(builder.Configuration.GetSection(OAuthOptions.SectionName));
#endif
#if (IncludeExternalLogin)
    builder.Services.AddOptions<ExternalAuthOptions>()
        .Bind(builder.Configuration.GetSection(ExternalAuthOptions.SectionName));
#endif
    builder.Services.AddOptions<UserRegistrationOptions>()
        .Bind(builder.Configuration.GetSection(UserRegistrationOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddOptions<SmtpOptions>()
        .Bind(builder.Configuration.GetSection(SmtpOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
#endif

#if (IncludeOpenIddict)
    // 2.6. OpenIddict OAuth 2.0 / OIDC 服务端
    builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                .UseDbContext<MyProjectDbContext>();
        })
        .AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token")
                .SetUserInfoEndpointUris("/connect/userinfo")
                .SetEndSessionEndpointUris("/connect/logout");

            // 设置固定 issuer（跨服务验证场景必须）
            var oauthOpts = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
            if (!string.IsNullOrWhiteSpace(oauthOpts.Issuer))
            {
                options.SetIssuer(new Uri(oauthOpts.Issuer));
            }

            options.AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange();
            options.AllowRefreshTokenFlow();
            options.AllowClientCredentialsFlow();

            // 禁用 access token 加密，允许跨服务验证（官方推荐）
            options.DisableAccessTokenEncryption();

            options.RegisterScopes(
                OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Scopes.Email,
#if (IncludeRoles)
                OpenIddictConstants.Scopes.Roles,
#endif
                OpenIddictConstants.Scopes.OfflineAccess,
                // 服务间调用的用户委托 scope：只有被显式授予它的客户端，携带的 X-User-* 头
                // 才会被采信并恢复为用户主体（见 Leistd.ServiceClient 的信任边界）。
                // 拿到机器令牌 ≠ 有权代表用户，两者必须分开授予。
                ServiceClientScopes.Delegation);

            if (oauthOpts.UseDevelopmentCertificates)
            {
                options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();
            }
            else
            {
                if (!string.IsNullOrEmpty(oauthOpts.EncryptionCertificatePath))
                {
                    options.AddEncryptionCertificate(
                        X509CertificateLoader.LoadPkcs12FromFile(
                            oauthOpts.EncryptionCertificatePath,
                            oauthOpts.EncryptionCertificatePassword));
                }
                if (!string.IsNullOrEmpty(oauthOpts.SigningCertificatePath))
                {
                    options.AddSigningCertificate(
                        X509CertificateLoader.LoadPkcs12FromFile(
                            oauthOpts.SigningCertificatePath,
                            oauthOpts.SigningCertificatePassword));
                }
            }

            // 内网 S2S 调用禁用 HTTPS 要求
            if (oauthOpts.DisableHttpsRequirement)
            {
                options.UseAspNetCore()
                    .DisableTransportSecurityRequirement()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            }
            else
            {
                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough();
            }
        })
        .AddValidation(options =>
        {
            options.UseLocalServer();

            // 只接受 Authorization: Bearer。RFC 6750 §2.3 对 URI query 传令牌的措辞是
            // "除非无法用 Authorization 头，否则 SHOULD NOT"——令牌一旦进 URL，就会进
            // 反向代理与网关的访问日志、APM、浏览器历史和 Referer，且难以察觉。
            // OpenIddict 作为实现规范的库把三种方式都开着是本分，但"哪种方式可用"是应用的
            // 策略选择；ASP.NET Core 自己的 JwtBearerHandler 默认同样只读 Authorization 头。
            //
            // 改用 Bearer 认证的 SignalR 时不要把这两行删掉：浏览器的 WebSocket/SSE 设不了
            // 自定义头，令牌只能走 query，但那是 Hub 路径的需要，不是全部 API 的。
            // 按 Hub 路径定向搬运即可，配方见实时通信组件文档的"Bearer 认证下的 Hub 令牌传递"。
            // 模板自带的实时通知走 Cookie 会话（见前端 SignalRService），不受这里影响。
            options.UseAspNetCore()
                   .DisableAccessTokenExtractionFromQueryString()
                   .DisableAccessTokenExtractionFromBodyForm();
        });
#endif
// (IncludeOpenIddict)

    // 3. 注册应用启动引导程序 (替代手动 InitializeApplicationAsync)
    builder.Services.AddHostedService<ApplicationBootstrapper>();

    // 4. API 层基础设施 (Exception, HealthChecks, Controllers)
    builder.Services.AddGlobalExceptionHandler(builder.Configuration);
#if (IncludeLocalization)
    // 多语言：默认英语，支持中英；错误消息与校验消息随请求 culture 本地化。
    // 业务错误文案键的资源在本项目 Resources/{en,zh-CN}.json（覆盖/扩展框架默认 Error:* 键）。
    builder.Services.AddJsonLocalization(
        supportedCultures: ["en", "zh-CN"],
        configure: options =>
        {
            options.ResourceAssemblies.Add(typeof(Program).Assembly);
            // 显式登记 DataAnnotations 校验消息的标记类型走 JSON（组合工厂按类型精确路由，
            // 未登记则委派官方 RESX）；否则 factory.Create(typeof(ApiResource)) 取不到 JSON 校验文案。
            options.JsonResourceTypes.Add(typeof(ApiResource));
#if (IncludeRoles)
            // 权限定义的显示名存的是本地化键，由 PermissionAppService 在响应阶段翻译；
            // 组合工厂按类型精确路由，这里必须登记它才会走 JSON 词条而非 RESX。
            options.JsonResourceTypes.Add(typeof(PermissionAppService));
#endif
        });
#endif
    builder.Services.AddHealthChecks();
    builder.Services.AddMyProjectSpaProxy();
    // HTTP 管道 JSON 配置：ProblemDetails / IProblemDetailsService（业务 422、异常响应）走此配置——
    // 与下方 MVC 的 AddJsonOptions 用同一 ConfigureWebApi，令业务响应 / 400 / 422 命名策略一致、跟随宿主。
    builder.Services.ConfigureHttpJsonOptions(options => JsonOptions.ConfigureWebApi(options.SerializerOptions));
    builder.Services.AddControllers()
        .AddJsonOptions(options => JsonOptions.ConfigureWebApi(options.JsonSerializerOptions))
#if (IncludeLocalization)
        // DataAnnotations 校验消息随 culture 本地化：ErrorMessage/Display 的英文句子即文案键（en 默认值），
        // zh-CN.json 用同一句子作键映射中文。Provider 指向 ApiResource 标记类型（工厂返回共享视图）。
        .AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(ApiResource)))
#endif
        // [ApiController] 自动 400 校验产出与业务 422 一致的 RFC 9457 errors 数组形态（统一两条校验路径）。
        .ConfigureApiValidation();

    // 4.1. CORS 配置
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                   ForwardedHeaders.XForwardedProto |
                                   ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });

    builder.Services.AddCors(options =>
    {
        var corsConfig = builder.Configuration.GetSection("Cors");
        var allowAnyLocalhost = corsConfig.GetValue<bool>("AllowAnyLocalhost");
        var allowedOrigins = corsConfig.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();

            if (allowAnyLocalhost)
            {
                // 允许所有 Localhost 端口访问 (仅用于开发环境配置)
                policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost");
            }
            else if (allowedOrigins.Length > 0)
            {
                // 生产环境限制特定域名
                policy.WithOrigins(allowedOrigins);
            }
            // 否则默认不允许任何来源 (安全默认值)
        });
    });

    // 4.5. Leistd Security 服务
    builder.Services.AddSecurity();

#if (IncludeTenancy)
    // 4.5.-1 多租户：环境上下文与解析链（Claim 定案 → X-Tenant-Id 头 → tenant 查询串），
    // 配置节 Leistd:MultiTenancy；租户存储/管理器在 Infrastructure 层注册
    builder.Services.AddMultiTenancy(builder.Configuration);
#endif

#if (IncludeOpenIddict)
    // 4.5.0 服务间调用：受信恢复调用方携带的 X-User-* 用户上下文（配置节 Leistd:ServiceUserContext）。
    // 仅当调用方以 client credentials 令牌通过认证时才采信这些头，其余请求一律剥离，阻断伪造。
    builder.Services.AddServiceUserContext(builder.Configuration);
#endif

#if (IncludeNotifications)
    // 4.5.1 Leistd Notifications — 通知 Hub 与业务实时 Hub 显式注册
    // AddNotificationsSignalR 只注册通知传输；模板前端还会连接 /hubs/realtime 订阅业务事件，
    // 因此业务实时能力需要显式调用 AddRealTimeSignalR。
    void ConfigureSignalR(RealTimeOptions opt)
    {
        opt.EnableDetailedErrors = builder.Environment.IsDevelopment();
    }

    builder.Services.AddRealTimeSignalR(ConfigureSignalR);
    builder.Services.AddNotificationsSignalR(ConfigureSignalR);
#endif

    // 4.6. DataProtection 配置（生产环境必需）
    builder.Services.AddMyProjectDataProtection(builder.Configuration, builder.Environment);

    // 5. 安全配置 (AuthN & AuthZ)
#if (IncludeIdentity)
    builder.Services.AddAuthentication(options =>
    {
#if (IncludeTenancy && IncludeOpenIddict)
        // 多租户 + OpenIddict：默认认证方案改为按请求选择的转发方案。
        // 多租户中间件在 UseAuthentication 之后立即依赖 HttpContext.User 做"已认证主体的
        // 租户由 claim 定案"——若默认方案固定为 Bearer 校验，Cookie 会话在中间件阶段
        // 是匿名的，伪造的 X-Tenant-Id 头就能改写已登录用户的租户上下文。
        // 转发方案让 Bearer 请求走 OpenIddict 校验、其余走 Cookie，两类主体在
        // 中间件阶段都已就绪；默认授权策略仍显式列出两个方案，行为不变。
        options.DefaultAuthenticateScheme = "MyProjectSmart";
        options.DefaultChallengeScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
#elif (IncludeOpenIddict)
        // 启用 OpenIddict 时，默认走其 Bearer 校验；未启用时默认走 Cookie。
        options.DefaultAuthenticateScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
#else
        options.DefaultAuthenticateScheme = "MyProjectCookie";
        options.DefaultChallengeScheme = "MyProjectCookie";
#endif
    })
#if (IncludeTenancy && IncludeOpenIddict)
    .AddPolicyScheme("MyProjectSmart", "按请求选择 Bearer 或 Cookie", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.Any(value =>
                value != null && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                ? OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                : "MyProjectCookie";
    })
#endif
    .AddCookie("MyProjectCookie", options =>
    {
        var isDevelopmentEnvironment = builder.Environment.IsDevelopment();

        options.LoginPath = "/auth/login";
        options.Cookie.Name = "CompanyName.ProjectName.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = isDevelopmentEnvironment ? SameSiteMode.Lax : SameSiteMode.None;
        options.Cookie.SecurePolicy = isDevelopmentEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;

#if (IncludeOpenIddict)
        var oauthConfig = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
        options.ExpireTimeSpan = TimeSpan.FromDays(oauthConfig.CookieExpireDays);
#else
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
#endif
        options.SlidingExpiration = true;

        // 一律返回状态码，不重定向。默认行为是 302 到 /Account/AccessDenied——本应用没有这个
        // 路由，再叠上 SPA 兜底，跟随重定向的客户端最后收到的是一个 HTML 200，把"未认证/无权限"
        // 伪装成了成功。这里也不去按 Accept 猜"是不是浏览器导航"：本宿主是 SPA + API，
        // 没有任何受保护的 SSR 页面需要这条重定向分支——`/connect/authorize` 的交互式登录跳转
        // 由它自己处理。将来真加了 Razor/SSR，再按端点元数据定向重定向，不要靠嗅探请求头。
        options.Events.OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden);

        static Task WriteStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    });

    // 撤权要对已签发的凭据生效：登录时的启用/锁定检查挡不住已在线的会话。
    // 覆盖范围是每一次新的 HTTP 请求和每一次新的 Hub 连接握手；
    // 已经建立的 SignalR 连接不在其中，见 ActiveUserRequirement 的说明。
    builder.Services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();

    // 账号失效要返回 401 而不是 403：前端只把 401 当会话失效来清理登录态。
    builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, InvalidAccountResultHandler>();

    builder.Services.AddAuthorization(options =>
    {
        var schemes = new[]
        {
#if (IncludeOpenIddict)
            OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
#endif
            "MyProjectCookie"
        };

        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveUserRequirement())
            .Build();

        // 超级管理员策略：claim 判定超管身份，但同样要求账号可用——
        // 超管被禁用后也必须立刻失去权限，否则这条策略就成了绕过撤权的旁路。
        options.AddPolicy("SuperAdmin", policy => policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .AddRequirements(new ActiveUserRequirement())
            .RequireClaim(Leistd.Security.Claims.CustomClaimTypes.IsSuperAdmin, "true"));
    });
#else
    builder.Services.AddAuthorization();
#endif
#if (IncludeRoles)
    // 将权限定义接入微软授权 Policy 管道，使 [Authorize(Policy = "权限名")] 生效
    builder.Services.AddPermissionAuthorization();
#endif

    // --- 构建应用 ---
    var app = builder.Build();

    // 在应用启动前自动迁移或创建数据库，确保数据库就绪后再接收请求。
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (db.Database.IsRelational())
        {
            // 模板默认不内置迁移：
            // - 已添加迁移（dotnet ef migrations add）-> 走 Migrate 应用迁移；
            // - 尚无任何迁移 -> 走 EnsureCreated 直接按当前模型建表。
            // 注意：同一数据库不要在两种方式间切换。
            var hasMigrations = db.Database.GetMigrations().Any();
            if (hasMigrations)
            {
                var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (pendingMigrations.Count > 0)
                {
                    logger.LogInformation("检测到关系型数据库，正在应用 {Count} 个待执行迁移...", pendingMigrations.Count);
                    await db.Database.MigrateAsync();
                    logger.LogInformation("数据库迁移完成");
                }
                else
                {
                    logger.LogInformation("检测到关系型数据库，迁移已是最新，无需处理");
                }
            }
            else
            {
                logger.LogInformation("检测到关系型数据库且无迁移，使用 EnsureCreated 按当前模型创建表结构...");
                await db.Database.EnsureCreatedAsync();
                logger.LogInformation("数据库表结构创建完成");
            }
        }
        else
        {
            logger.LogInformation("使用内存数据库，跳过迁移");
        }
    }

    // 7. 中间件管道配置
    app.UseForwardedHeaders();
#if (IncludeLocalization)
    // 请求 culture 解析（QueryString / Cookie / Accept-Language）——须在读取 culture 的中间件（含全局异常处理）之前
    app.UseJsonRequestLocalization();
#endif
    var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var uploadsRoot = Path.Combine(webRootPath, "uploads");
    Directory.CreateDirectory(uploadsRoot);

    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsRoot),
        RequestPath = "/uploads"
    });

    app.UseSerilogRequestLogging(options =>
    {
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return Serilog.Events.LogEventLevel.Error;

            var endpoint = httpContext.GetEndpoint();
            if (endpoint != null && string.Equals(endpoint.DisplayName, "SpaProxyFallback", StringComparison.OrdinalIgnoreCase))
            {
                return Serilog.Events.LogEventLevel.Verbose;
            }

            return Serilog.Events.LogEventLevel.Information;
        };
    });
    app.UseGlobalExceptionHandler();
    app.UseCorrelationId();
    app.MapHealthChecks("/api/health").AllowAnonymous();

    app.UseCors();

    app.UseSecurity();
#if (IncludeIdentity)
    app.UseAuthentication();
#if (IncludeOpenIddict)
    // 服务间调用的用户上下文恢复：必须在认证之后（信任判定依赖已认证的调用方主体）、授权之前
    app.UseServiceUserContext();
#endif
#endif
#if (IncludeTenancy)
    // 租户会话自恢复：会话所属租户被删/停用时注销 Cookie 并恢复导航，防止死锁在错误页
    app.UseTenantSessionRecovery("MyProjectCookie");
    // 多租户解析与校验：认证（及受信恢复）之后——Claim 贡献者需要已认证主体；
    // 授权之前——权限检查必须在租户上下文内执行。未知租户 404、停用租户 403
    app.UseMultiTenancy();
#endif
    app.UseAuthorization();

    app.MapControllers();

#if (IncludeNotifications)
    // SignalR 端点：通知 Hub 与实时业务事件 Hub 各自显式映射。
    // 若项目只保留通知能力，可删除 MapRealTimeHub 以及上面的 AddRealTimeSignalR。
    app.MapNotificationHub();
    app.MapRealTimeHub();
#endif

    app.MapMyProjectSpaFallback();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// 向 WebApplicationFactory 集成测试公开顶层语句生成的入口类型。
public partial class Program
{
}
