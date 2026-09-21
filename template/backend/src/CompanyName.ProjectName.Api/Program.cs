using CompanyName.ProjectName.Api.Hosting;
#if (RemoteTokenAuth)
using CompanyName.ProjectName.Api.HealthChecks;
#endif
#if (LocalIdentity)
using CompanyName.ProjectName.Api.Auth;
#endif
#if (LocalIdentity)
using CompanyName.ProjectName.Api.Middlewares;
#endif
using Leistd.MultiTenancy.AspNetCore;
using Leistd.MultiTenancy.AspNetCore.Options;
using CompanyName.ProjectName.Api.HostedServices.Initializer;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Api.Configuration;

using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.Options;
using CompanyName.ProjectName.Domain.Users.Policies;
#endif
using CompanyName.ProjectName.Domain.Shared.Json;
using CompanyName.ProjectName.Infrastructure;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.ExceptionHandling.AspNetCore;
using CompanyName.ProjectName.Api;
#if (IncludeLocalization)
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Authorization.Options;
using Leistd.Localization.AspNetCore;
using Leistd.Settings.Options;
#endif
using Leistd.BackgroundJobs.InProcess;
using Leistd.Settings.Hosting;
using Leistd.Security.AspNetCore;
using Leistd.Security.Claims;
using Leistd.Tracing.AspNetCore;
using Leistd.Authorization.AspNetCore;
using Leistd.MultiTenancy;
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.SignalR;
#if (LocalIdentity)
using CompanyName.ProjectName.Api.Notifications;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Notifications;
using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Email;
using Leistd.Notifications.Email.Recipients;
using Leistd.Notifications.Settings;
using Leistd.Notifications.Settings.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif
using Leistd.RealTime;
using Leistd.RealTime.AspNetCore.SignalR;
#if (!LocalIdentity)
using Leistd.AspNetCore.SignalR;
#endif
#endif
#if (LocalIdentity)
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
#if (OpenIddictServer)
using System.Security.Cryptography.X509Certificates;
using Leistd.ServiceClient.Constants;
using OpenIddict.Abstractions;
#endif
#endif
#if (ServiceUserContextEnabled)
using Leistd.ServiceClient.AspNetCore;
#endif
#if (!LocalIdentity)
using OpenIddict.Validation.AspNetCore;
#endif
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Authorization;
#if (LocalIdentity)
using CompanyName.ProjectName.Application.Auth.AppServices;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Application.Auth.Abstractions;
using CompanyName.ProjectName.Application.Auth.Constants;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections.Constants;
#endif
#endif
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Leistd.DependencyInjection.DynamicProxy.Registration;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting web application ...");
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory(
        new ServiceProviderOptions
        {
            ValidateScopes = builder.Environment.IsDevelopment(),
            ValidateOnBuild = builder.Environment.IsDevelopment()
        }));

    // 宿主级设置作为优先级最高的配置源（日志级别、发信参数、操作记录保留期，见 HostSettingBindings）：
    // 设置里有值的项覆盖部署配置，消费方照常注入 IOptionsMonitor<T>。写入后本进程立即应用，
    // 其它实例由周期任务跟上（Leistd:Settings:Hosting:RefreshInterval）。配置源在 Build 之后挂上（UseHostSettings）
    builder.Services.AddMyProjectHostSettings();

    // 请求体沿用 Kestrel 默认上限（约 30 MB）：需要更大上传的端点用 [RequestSizeLimit] / [RequestFormLimits] 单独放宽
    builder.AddMyProjectLogging();

    builder.Services.AddDomainServices();
    builder.Services.AddInfrastructureServices(builder.Configuration);
    builder.Services.AddApplicationServices();

#if (LocalIdentity)
    var defaultAdminOptions = builder.Services.AddOptions<DefaultAdminOptions>()
        .Bind(builder.Configuration.GetSection(DefaultAdminOptions.SectionName));

    // 邮箱验证开启时，HMAC 密钥必须跨实例和重启稳定。
    builder.Services.AddOptions<VerificationCodeOptions>()
        .Bind(builder.Configuration.GetSection(VerificationCodeOptions.SectionName))
        .Validate<IConfiguration>(
            (options, config) =>
                !config.GetValue<bool>("UserRegistration:EnableEmailVerification")
                || options.IsKeyUsable,
            $"{VerificationCodeOptions.SectionName}:Key is required when " +
            "UserRegistration:EnableEmailVerification is true, and must be at least " +
            $"{VerificationCodeOptions.MinimumKeyBytes} base64-encoded bytes.")
        .ValidateOnStart();

    // 拒绝缺失或已公开的超级管理员密码。
    defaultAdminOptions
        .Validate(
            options => options.IsPasswordUsable,
            $"{DefaultAdminOptions.SectionName}:Password is required and must satisfy the password " +
            $"policy (at least {PasswordPolicy.MinimumLength} characters). Inject it from the " +
            "deployment (environment variable or user-secrets); there is deliberately no default.")
        .ValidateOnStart();
#endif
#if (LocalIdentity)
#if (OpenIddictServer)
    builder.Services.AddOptions<OAuthOptions>()
        .Bind(builder.Configuration.GetSection(OAuthOptions.SectionName));
#endif
    builder.Services.AddOptions<UserRegistrationOptions>()
        .Bind(builder.Configuration.GetSection(UserRegistrationOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
#endif

#if (OpenIddictServer)
    builder.Services.AddOpenIddict()
        .AddCore(options =>
        {
            options.UseEntityFrameworkCore()
                .UseDbContext<OpenIddictDbContext>();
        })
        .AddServer(options =>
        {
            options.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token")
                .SetUserInfoEndpointUris("/connect/userinfo")
                .SetEndSessionEndpointUris("/connect/logout");
            options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));

            var oauthOpts = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
            if (!string.IsNullOrWhiteSpace(oauthOpts.Issuer))
            {
                options.SetIssuer(new Uri(oauthOpts.Issuer));
            }

            options.AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange();
            options.AllowRefreshTokenFlow();
            options.AllowClientCredentialsFlow();

            // 资源服务需要直接验证 access token。
            options.DisableAccessTokenEncryption();

            options.RegisterScopes(
                OpenIddictConstants.Scopes.OpenId,
                OpenIddictConstants.Scopes.Profile,
                OpenIddictConstants.Scopes.Email,
                OpenIddictConstants.Scopes.Roles,
                OpenIddictConstants.Scopes.OfflineAccess,
                // 机器令牌只在显式拥有 delegation scope 时才能代表用户。
                ServiceClientScopes.Delegation,
                TenantConnectionScopes.RuntimeRead,
                TenantConnectionScopes.MigrationRead);

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

            // 每个请求按令牌记录确认令牌未被撤销：停用、删除账号时撤销的令牌立即失效，
            // 在认证阶段就以 invalid_token 拒绝。API 与授权服务器同库部署，这次查库替代了逐请求查用户
            options.EnableTokenEntryValidation();

            // 普通 API 只接受 Bearer 头，避免令牌进入 URL 和访问日志。
            // SignalR 若需 query 令牌，应只在 Hub 路径定向转换。
            options.UseAspNetCore()
                   .DisableAccessTokenExtractionFromQueryString()
                   .DisableAccessTokenExtractionFromBodyForm();
        });
#endif
#if (!LocalIdentity)
    // Resource 只验证 Identity 签发的 Bearer token。
    const string RemoteIdentityConfigurationError =
        "Resource services require Authentication:Issuer (an absolute http(s) URI) and Authentication:Audience.";

    builder.Services.AddOptions<RemoteIdentityOptions>()
        .Bind(builder.Configuration.GetSection(RemoteIdentityOptions.SectionName));

    // 校验放在这里而不是 ValidateOnStart：OpenIddict 在**组合期**就要用 issuer，
    // 比启动期校验早一步。两处都写等于同一条件维护两份，出错时还分不清是哪一处报的。
    var remoteIdentity = builder.Configuration
        .GetSection(RemoteIdentityOptions.SectionName)
        .Get<RemoteIdentityOptions>() ?? new RemoteIdentityOptions();
    if (!remoteIdentity.IsUsable)
    {
        throw new InvalidOperationException(RemoteIdentityConfigurationError);
    }

    builder.Services.AddOpenIddict()
        .AddValidation(options =>
        {
            options.SetIssuer(remoteIdentity.IssuerUri!);
            options.AddAudiences(remoteIdentity.Audience!);
            options.UseSystemNetHttp();
            options.UseAspNetCore()
                .DisableAccessTokenExtractionFromQueryString()
                .DisableAccessTokenExtractionFromBodyForm();
        });
#endif

    builder.Services.AddHostedService<ApplicationInitializer>();

    // 周期任务调度与进程内队列：组件登记的维护任务（操作记录归档、通知保留期、宿主级设置刷新）由它执行。
    // 集群任务经分布式锁 + 完成水位保证多副本同一时段只跑一次（多副本部署须配置 Redis）
    builder.Services.AddInProcessBackgroundJobs();

    builder.Services.AddGlobalExceptionHandler(builder.Configuration);
#if (IncludeLocalization)
    builder.Services.AddJsonLocalization(
        supportedCultures: [.. SettingConstant.Display.SupportedLanguages],
        configure: options =>
        {
            options.ResourceAssemblies.Add(typeof(Program).Assembly);
            // 只有显式登记的强类型资源才路由到 JSON。
            options.JsonResourceTypes.Add(typeof(ApiResource));
        });
    // 设置页与权限树的显示名按 Setting:{名称}、SettingGroup:{分组} 与权限定义的显示名键查 ApiResource
    builder.Services.Configure<SettingManagementOptions>(options => options.LocalizationResource = typeof(ApiResource));
    builder.Services.Configure<PermissionManagementOptions>(options => options.LocalizationResource = typeof(ApiResource));
#endif
    // liveness 只表示本进程存活，不依赖外部服务。
    var healthChecks = builder.Services.AddHealthChecks()
        .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: ["live"]);
#if (RemoteTokenAuth)
    // readiness 在启动时确认 Identity 元数据可达，并锁存结果。
    builder.Services.AddSingleton<RemoteIdentityReadinessHealthCheck>();
    builder.Services.AddHttpClient(nameof(RemoteIdentityReadinessInitializer));
    builder.Services.AddHostedService<RemoteIdentityReadinessInitializer>();
    healthChecks.AddCheck<RemoteIdentityReadinessHealthCheck>("remote-identity", tags: ["ready"]);
#else
    healthChecks.AddCheck("ready-self",
        () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
        tags: ["ready"]);
#endif
    builder.Services.AddMyProjectSpaProxy();
    // HTTP 与 MVC 共用同一 JSON 配置，统一业务响应和 ProblemDetails。
    builder.Services.ConfigureHttpJsonOptions(options => JsonOptions.ConfigureWebApi(options.SerializerOptions));
    builder.Services.AddControllers()
        .AddJsonOptions(options => JsonOptions.ConfigureWebApi(options.JsonSerializerOptions))
#if (IncludeLocalization)
        // DataAnnotations 使用 ApiResource 的 JSON 资源。
        .AddDataAnnotationsLocalization(options =>
            options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(ApiResource)))
#endif
        .ConfigureApiValidation();

    // X-Forwarded-Host 影响租户解析和绝对 URL，因此只信任显式配置的代理。
    // 在 Options 回调内读取 Build 阶段已合并的最终配置。
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                   ForwardedHeaders.XForwardedProto |
                                   ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;

        var forwardedConfig = builder.Configuration.GetSection("ForwardedHeaders");

        foreach (var proxy in forwardedConfig.GetSection("KnownProxies").Get<string[]>() ?? [])
        {
            if (!IPAddress.TryParse(proxy, out var address))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{proxy}'.");
            }

            options.KnownProxies.Add(address);
        }

        foreach (var network in forwardedConfig.GetSection("KnownNetworks").Get<string[]>() ?? [])
        {
            if (!System.Net.IPNetwork.TryParse(network, out var parsed))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid CIDR range: '{network}'.");
            }

            options.KnownIPNetworks.Add(parsed);
        }
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
#if (LocalIdentity)

            // 跨域前端需要读取租户失效响应头以触发会话恢复。
            policy.WithExposedHeaders(TenantSessionRecoveryOptions.DefaultTenantInvalidHeader);
#endif

            if (allowAnyLocalhost)
            {
                policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost");
            }
            else if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins);
            }
        });
    });

    builder.Services.AddSecurity();

#if (LocalIdentity)
    // Identity 持有租户注册表，因此校验解析结果。
    builder.Services.AddMultiTenancy(builder.Configuration);
#else
    // Resource 不持有注册表，只信已验证令牌的 tenant_id claim。
    builder.Services.AddMultiTenancy(options =>
    {
        builder.Configuration.GetSection("Leistd:MultiTenancy").Bind(options);
        options.ValidateResolvedTenant = false;
    });
#endif

#if (ServiceUserContextEnabled)
    // 仅为已认证的 client credentials 调用恢复转发用户上下文。
    builder.Services.AddServiceUserContext(builder.Configuration);
#endif

#if (IncludeNotifications)
    // 心跳、超时、详细错误是 SignalR 自身的选项，由宿主直接配置。
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });

    // 通知与业务事件使用不同的 SignalR 传输，必须分别注册。
    builder.Services.AddRealTimeSignalR();
    builder.Services.AddNotificationsSignalR();
#if (LocalIdentity)

    // 通知偏好（收件人自己的用户级设置）决定哪类通知经哪个渠道收；安全提醒的站内通知必达，不受偏好影响
    builder.Services.AddNotificationPreferences(options =>
        options.MandatoryDeliveries.Add(new NotificationDelivery(AppNotificationTypes.Security, AppNotificationChannels.InApp)));
    // 邮件渠道只发已验证的邮箱，经后台队列发送
    builder.Services.AddEmailNotifications();
    builder.Services.AddScoped<INotificationRecipientResolver, UserEmailRecipientResolver>();
    // 安全提醒（新设备登录、密码与两步验证变更、账号锁定）经通知组件发给本人
    builder.Services.Replace(ServiceDescriptor.Transient<ISecurityAlertPublisher, NotificationSecurityAlertPublisher>());
#endif

    // 订阅授权必须由宿主明确选择：Subscribe 无条件走授权器，框架不给默认实现。
    // 组名不含租户段，Subscribe 收的是客户端给的任意字符串，所以只放行显式公共的 public: 命名空间；
    // 订阅租户内资源时换成自己的授权器。
    builder.Services.AddPrefixRealTimeSubscriptions("public:");
#endif

    builder.Services.AddMyProjectDataProtection(builder.Configuration, builder.Environment.ContentRootPath);

#if (LocalIdentity)
    // 会话 Cookie 的滑动过期与服务端会话的空闲时限是同一个值，只在这里定一次
#if (OpenIddictServer)
    var oauthConfig = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
    var sessionLifetime = TimeSpan.FromDays(oauthConfig.CookieExpireDays);
#else
    var sessionLifetime = TimeSpan.FromDays(7);
#endif
    builder.Services.Configure<UserSessionOptions>(options => options.IdleTimeout = sessionLifetime);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestClientInfo, HttpRequestClientInfo>();

    builder.Services.AddAuthentication(options =>
    {
#if (OpenIddictServer)
        // 多租户解析前必须按请求恢复 Bearer 或 Cookie 主体，防止请求头改写已登录用户的租户。
        options.DefaultAuthenticateScheme = "MyProjectSmart";
        options.DefaultChallengeScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
#else
        // 不签发令牌时只恢复 Cookie 会话。
        options.DefaultAuthenticateScheme = AuthenticationSchemeNames.SessionCookie;
        options.DefaultChallengeScheme = AuthenticationSchemeNames.SessionCookie;
#endif
    })
#if (OpenIddictServer)
    .AddPolicyScheme("MyProjectSmart", "Selects Bearer or Cookie per request", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.Any(value =>
                value != null && value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                ? OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                : AuthenticationSchemeNames.SessionCookie;
    })
#endif
    .AddCookie(AuthenticationSchemeNames.SessionCookie, options =>
    {
        var isDevelopmentEnvironment = builder.Environment.IsDevelopment();

        options.LoginPath = "/auth/login";
        options.Cookie.Name = "CompanyName.ProjectName.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = isDevelopmentEnvironment ? SameSiteMode.Lax : SameSiteMode.None;
        options.Cookie.SecurePolicy = isDevelopmentEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;

        options.ExpireTimeSpan = sessionLifetime;
        options.SlidingExpiration = true;

        // 会话 Cookie 是自包含的，签出去就撤不回；每个请求都回服务端确认它登记的会话还在，
        // 撤销（退出其他设备、改密码）才能对已发出的 Cookie 生效。结果带短缓存，见 IUserSessionValidator
        options.Events.OnValidatePrincipal = async context =>
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<IUserSessionValidator>();
            if (context.Principal is null ||
                !await validator.ValidateAsync(context.Principal, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
            }
        };

        // API 拒绝必须返回 401/403，不能被 Cookie 重定向和 SPA 回退转换为 HTML 200。
        options.Events.OnRedirectToLogin = context => WriteStatus(context, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => WriteStatus(context, StatusCodes.Status403Forbidden);

        static Task WriteStatus(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
        {
            context.Response.StatusCode = statusCode;
            return Task.CompletedTask;
        }
    });

#else
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme =
            OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    });
#endif

    builder.Services.AddApiAuthorization();
    // 将权限定义作为 ASP.NET Core 授权策略解析。
    builder.Services.AddPermissionAuthorization();

    var app = builder.Build();
    // 宿主级设置配置源要在构建之后挂上：构建期间追加的配置源（如测试宿主的覆盖）不能排在它后面
    app.UseHostSettings();

    app.UseForwardedHeaders();
#if (IncludeLocalization)
    // 在所有读取当前区域性的中间件之前解析请求区域性。
    app.UseJsonRequestLocalization();
#endif
    var webRootPath = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var uploadsRoot = Path.Combine(webRootPath, "uploads");
    Directory.CreateDirectory(uploadsRoot);

    app.UseDefaultFiles();
    app.UseStaticFiles(SpaExtensions.CreateSpaStaticFileOptions());
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uploadsRoot),
        RequestPath = "/uploads"
    });

    var requestLogging = app.Services.GetRequiredService<IOptionsMonitor<RequestLoggingOptions>>();
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

            // 正常完成的请求记成哪一级由设置决定：调到 Verbose 就等于关掉请求日志
            // （全局最小级别通常是 Information，Verbose 不会落盘）。
            // 失败与 5xx 不受它影响——那是排障必需的，不该被一个运维开关关掉。
            return requestLogging.CurrentValue.Level;
        };
    });
    app.UseGlobalExceptionHandler();
    app.UseCorrelationId();
    app.MapHealthChecks("/api/health/live", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live")
    }).AllowAnonymous();
    app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready")
    }).AllowAnonymous();

    app.UseCors();

#if (!LocalIdentity && IncludeNotifications)
    // 仅 Hub 允许 SignalR 浏览器客户端的 access_token query；普通 API 仍只接受 Bearer header。
    app.UseHubAccessToken();
#endif
    app.UseAuthentication();
#if (ServiceUserContextEnabled)
    // 转发上下文的信任判定依赖已认证的调用方主体。
    app.UseServiceUserContext();
#endif
#if (LocalIdentity)
    // 租户失效时注销 Cookie，避免会话困在不可用租户中。
    app.UseTenantSessionRecovery(options => options.SignOutScheme = AuthenticationSchemeNames.SessionCookie);
#endif
    // 租户在认证后、授权前解析；未解析到租户表示宿主上下文。
    app.UseMultiTenancy();
#if (LocalIdentity)
    // 受限会话（租户要求两步验证而本人未启用）只放行完成设置所需的接口
    app.UseMiddleware<TwoFactorSetupEnforcementMiddleware>();
#endif
    app.UseAuthorization();

    app.MapControllers();
    // 组件自带的端点（设置、权限、操作记录、通知、租户与租户连接），路由与原控制器一致
    app.MapComponentEndpoints();

#if (IncludeNotifications)
    // 通知与业务事件 Hub 必须分别映射。
    app.MapNotificationHub();
    app.MapRealTimeHub();
#endif

    app.MapMyProjectSpaFallback();

    // API 只验证 schema；所有 DDL 由 DbMigrator 施加。
    await app.VerifyDatabaseSchemaAsync();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // 重抛以便宿主和测试观察到非零退出。
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// 向 WebApplicationFactory 集成测试公开顶层语句生成的入口类型。
public partial class Program
{
}
