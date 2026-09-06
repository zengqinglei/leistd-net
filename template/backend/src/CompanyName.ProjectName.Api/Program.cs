using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Api.Middlewares;
using Leistd.MultiTenancy.AspNetCore;
using CompanyName.ProjectName.Api.HostedServices.Initializer;

using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Domain.Users.Options;
#endif
using CompanyName.ProjectName.Domain.Shared.Json;
using CompanyName.ProjectName.Infrastructure;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.ExceptionHandling.AspNetCore;
using CompanyName.ProjectName.Api;
#if (IncludeLocalization)
using CompanyName.ProjectName.Application.Permissions.AppServices;
using Leistd.Localization.AspNetCore;
#endif
using Leistd.Security.AspNetCore;
using Leistd.Security.Claims;
using Leistd.Tracing.AspNetCore;
using Leistd.Authorization.AspNetCore;
using Leistd.MultiTenancy;
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.SignalR;
using Leistd.RealTime.Abstractions;
using Leistd.RealTime.AspNetCore.SignalR;
using CompanyName.ProjectName.Api.RealTime;
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
using CompanyName.ProjectName.Api.Options;
using OpenIddict.Validation.AspNetCore;
#endif
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Authorization;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Shared.Email.Options;
using CompanyName.ProjectName.Application.Auth;
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.TenantConnections;
#endif
#endif
using Microsoft.Extensions.FileProviders;
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

    builder.AddMyProjectInfrastructure();
    builder.Services.AddMyProjectWebServer();

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
            $"{DefaultAdminOptions.SectionName}:Password is required and must not be one of the sample " +
            "values published with this template. Inject it from the deployment (environment variable " +
            "or user-secrets); there is deliberately no default.")
        .ValidateOnStart();
#endif
#if (LocalIdentity)
#if (OpenIddictServer)
    builder.Services.AddOptions<OAuthOptions>()
        .Bind(builder.Configuration.GetSection(OAuthOptions.SectionName));
#endif
#if (ExternalLogin)
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
        .Bind(builder.Configuration.GetSection(RemoteIdentityOptions.SectionName))
        .Validate(options => options.IsUsable, RemoteIdentityConfigurationError)
        .ValidateOnStart();

    // OpenIddict 组合期需要已验证的 issuer。
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

    builder.Services.AddHostedService<ApplicationBootstrapper>();

    builder.Services.AddGlobalExceptionHandler(builder.Configuration);
#if (IncludeLocalization)
    builder.Services.AddJsonLocalization(
        supportedCultures: ["en", "zh-CN"],
        configure: options =>
        {
            options.ResourceAssemblies.Add(typeof(Program).Assembly);
            // 只有显式登记的强类型资源才路由到 JSON。
            options.JsonResourceTypes.Add(typeof(ApiResource));
            options.JsonResourceTypes.Add(typeof(PermissionAppService));
        });
#endif
    // liveness 只表示本进程存活，不依赖外部服务。
    var healthChecks = builder.Services.AddHealthChecks()
        .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(),
            tags: ["live"]);
#if (RemoteTokenAuth)
    // readiness 在启动时确认 Identity 元数据可达，并锁存结果。
    builder.Services.AddSingleton<RemoteIdentityReadinessGate>();
    builder.Services.AddHttpClient(nameof(RemoteIdentityReadinessInitializer));
    builder.Services.AddHostedService<RemoteIdentityReadinessInitializer>();
    healthChecks.AddCheck<RemoteIdentityReadinessCheck>("remote-identity", tags: ["ready"]);
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
            policy.WithExposedHeaders(TenantSessionRecoveryMiddleware.TenantInvalidHeader);
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

    // 订阅授权必须由宿主明确选择：Subscribe 无条件走授权器，框架不给默认实现。
    // 组名不含租户段，Subscribe 收的是客户端给的任意字符串，所以默认只放行显式公共的
    // public: 命名空间；订阅租户内资源时在 PublicResourceSubscriptionAuthorizer 里加判定。
    builder.Services.AddSingleton<IRealTimeSubscriptionAuthorizer, PublicResourceSubscriptionAuthorizer>();
#endif

    builder.Services.AddMyProjectDataProtection(builder.Configuration, builder.Environment);

#if (LocalIdentity)
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

#if (OpenIddictServer)
        var oauthConfig = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
        options.ExpireTimeSpan = TimeSpan.FromDays(oauthConfig.CookieExpireDays);
#else
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
#endif
        options.SlidingExpiration = true;

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

    app.UseForwardedHeaders();
#if (IncludeLocalization)
    // 在所有读取当前区域性的中间件之前解析请求区域性。
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
    app.UseTenantSessionRecovery(AuthenticationSchemeNames.SessionCookie);
#endif
    // 租户在认证后、授权前解析；未解析到租户表示宿主上下文。
    app.UseMultiTenancy();
    app.UseAuthorization();

    app.MapControllers();

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
