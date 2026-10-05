using CompanyName.ProjectName.Api.Hosting;
#if (RemoteTokenAuth)
using CompanyName.ProjectName.Api.HealthChecks;
#endif
using CompanyName.ProjectName.Api.Auth;
using CompanyName.ProjectName.Api.Middlewares;
#if (IncludeMultiTenancy)
using Leistd.MultiTenancy.AspNetCore;
using Leistd.MultiTenancy.AspNetCore.Options;
#endif
using CompanyName.ProjectName.Api.HostedServices.Initializer;
using CompanyName.ProjectName.Api.Options;
using CompanyName.ProjectName.Api.Configuration;

using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Application.Shared;
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
using CompanyName.ProjectName.Application.Settings.Provider;
using Leistd.Authorization.Options;
using Leistd.Localization.AspNetCore;
using Leistd.Settings.Options;
#endif
using Leistd.BackgroundJobs.InProcess;
using Leistd.Settings.Hosting;
using Leistd.Security.AspNetCore;
#if (!IncludeOperationRecords)
using Leistd.OperationRecords.Logging;
#endif
using Leistd.Security.Claims;
using Leistd.Tracing.AspNetCore;
using Leistd.Authorization.AspNetCore;
using Leistd.MultiTenancy;
using Leistd.MultiTenancy.Context;
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.SignalR;
using Leistd.Notifications.Settings;
#if (LocalIdentity)
using CompanyName.ProjectName.Api.Notifications;
using CompanyName.ProjectName.Application.Auth.SecurityAlerts;
using CompanyName.ProjectName.Application.Notifications;
using Leistd.Notifications.Channels;
using Leistd.Notifications.Errors;
using Leistd.Notifications.Publishing;
using Leistd.Notifications.Stores;
using Leistd.Notifications.Settings.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
#endif
#if (Email)
using Leistd.Notifications.Email;
using Leistd.Notifications.Email.Recipients;
#endif
#endif
#if (IncludeRealTime)
using CompanyName.ProjectName.Application.RealTime;
using Leistd.RealTime;
using Leistd.RealTime.AspNetCore.SignalR;
using Leistd.RealTime.AspNetCore.SignalR.Hubs;
using Leistd.RealTime.Subscriptions;
#endif
#if (SpaFrontend)
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
#endif
#if (LocalIdentity)
#if (OpenIddictServer)
using System.Security.Cryptography.X509Certificates;
using CompanyName.ProjectName.Application.Auth.OAuth;
#endif
#endif
#if (RemoteTokenAuth)
using Leistd.ServiceClient.Abstractions;
#endif
#if (ResourceBrowserSession)
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
#endif
#if (RemoteTokenAuth)
using OpenIddict.Validation.AspNetCore;
#endif
#if (!SpaFrontend && (IncludeNotifications || IncludeRealTime))
using Leistd.AspNetCore.SignalR;
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

#endif
using Microsoft.Extensions.Options;
using Leistd.DependencyInjection.DynamicProxy.Registration;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting web application ...");
    var builder = WebApplication.CreateBuilder(args);

    // 生产以外都校验作用域与构建期依赖：开发、集成测试宿主（Testing）、预发在启动时就暴露注册错误，
    // 只有生产为启动耗时省掉这一步
    var validateServiceProvider = !builder.Environment.IsProduction();
    builder.Host.UseServiceProviderFactory(new DynamicProxyServiceRegistrationCallbackFactory(
        new ServiceProviderOptions
        {
            ValidateScopes = validateServiceProvider,
            ValidateOnBuild = validateServiceProvider
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
#if (!IncludeOperationRecords)
    builder.Services.AddOperationRecordsLogging();
#endif

#if (LocalIdentity)
    // 口令不在启动期校验：只有真的要创建管理员时才需要，由初始化器在那一刻按口令策略校验并报出键名
    builder.Services.AddOptions<DefaultAdminOptions>()
        .Bind(builder.Configuration.GetSection(DefaultAdminOptions.SectionName));

#if (Email)
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
#endif
#endif
#if (LocalIdentity)
#if (OpenIddictServer)
    builder.Services.AddSingleton<IValidateOptions<OAuthOptions>, OAuthOptionsValidator>();
    builder.Services.AddOptions<OAuthOptions>()
        .Bind(builder.Configuration.GetSection(OAuthOptions.SectionName))
        .ValidateOnStart();
    // 退出确认是本源表单 POST，用官方防伪令牌校验；Cookie 规则与会话 Cookie 一致（部署环境 __Host- 前缀、仅 HTTPS）
    builder.Services.AddAntiforgery(options =>
    {
        var isDevelopmentEnvironment = builder.Environment.IsDevelopment();
        options.Cookie.Name = isDevelopmentEnvironment ? "CompanyName.ProjectName.Antiforgery" : "__Host-CompanyName.ProjectName.Antiforgery";
        options.Cookie.Path = "/";
        options.Cookie.SecurePolicy = isDevelopmentEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    });
    builder.Services.AddSingleton<CompanyName.ProjectName.Api.Auth.ConnectInteractionProtector>();
#endif
    builder.Services.AddOptions<UserRegistrationOptions>()
        .Bind(builder.Configuration.GetSection(UserRegistrationOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();
#endif

#if (OpenIddictServer)
    // 组合期读取并校验：下面 AddServer 的回调要到首次解析 OpenIddict 选项时才执行，那时才报错已经晚了。
    // 默认必须显式提供证书：开发证书生成在运行用户的证书存储里、每台机器各一份，多副本互不认，
    // 重建容器后已签发的令牌全部失效，只适合本机开发（由 appsettings.Development.json 打开）。
    var oauthOpts = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
    // 与启动期校验同一个验证器：这里更早，是因为下面组合 OpenIddict 时就要加载证书
    if (new OAuthOptionsValidator().Validate(null, oauthOpts) is { Failed: true } oauthValidation)
        throw new OptionsValidationException(OAuthOptions.SectionName, typeof(OAuthOptions), oauthValidation.Failures);
    // 证书在组合期逐张加载：路径缺失、文件损坏或口令错误时报出带下标的键名，而不是首个请求时的笼统异常
    var signingCertificates = oauthOpts.UseDevelopmentCertificates ? []
        : OAuthCertificateLoader.Load(oauthOpts.SigningCertificates, "OAuth:SigningCertificates");
    var encryptionCertificates = oauthOpts.UseDevelopmentCertificates ? []
        : OAuthCertificateLoader.Load(oauthOpts.EncryptionCertificates, "OAuth:EncryptionCertificates");

    var oauthScopes = OAuthScopes.All(oauthOpts);
    var conflictingScope = oauthScopes.GroupBy(scope => scope.Name, StringComparer.Ordinal)
        .FirstOrDefault(group => group.Count() > 1)?.Key;
    if (oauthOpts.ApiResources.Any(api => string.IsNullOrWhiteSpace(api.Name) ||
            string.IsNullOrWhiteSpace(api.ScopeName) || string.IsNullOrWhiteSpace(api.Owner)) ||
        oauthOpts.ApiResources.GroupBy(api => api.Name, StringComparer.Ordinal).Any(group => group.Count() > 1) ||
        oauthOpts.ApiResources.Any(api => api.Name == oauthOpts.Resource) || conflictingScope is not null)
    {
        throw new InvalidOperationException(
            "OAuth:ApiResources entries must be non-empty and distinct from each other, from OAuth:Resource " +
            $"and from the built-in scopes (conflict: '{conflictingScope}').");
    }

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
            options.SetAccessTokenLifetime(oauthOpts.AccessTokenLifetime);

            if (!string.IsNullOrWhiteSpace(oauthOpts.Issuer))
            {
                options.SetIssuer(new Uri(oauthOpts.Issuer));
            }

            options.AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange();
            // 授权与退出请求先存为 request token（控制库）再以 request_uri 重入：依赖方可以跨站 POST 发起，
            // 重入是顶层 GET，Lax 会话 Cookie 随之送达；id_token_hint 也不再留在浏览器地址里。
            options.EnableAuthorizationRequestCaching()
                .EnableEndSessionRequestCaching();
            options.AllowRefreshTokenFlow();
            options.AllowClientCredentialsFlow();
            options.AllowTokenExchangeFlow();
            options.Configure(server =>
            {
                server.SubjectTokenTypes.Clear();
                server.SubjectTokenTypes.Add(OpenIddict.Abstractions.OpenIddictConstants.TokenTypeIdentifiers.AccessToken);
                server.ActorTokenTypes.Clear();
                server.RequestedTokenTypes.Clear();
                server.RequestedTokenTypes.Add(OpenIddict.Abstractions.OpenIddictConstants.TokenTypeIdentifiers.AccessToken);
            });
            options.RemoveEventHandler(OpenIddict.Server.OpenIddictServerHandlers.Exchange.ValidateAuthorizedParty.Descriptor);
            options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ValidateTokenRequestContext>(handler =>
                handler.UseScopedHandler<CompanyName.ProjectName.Api.Auth.ResourceOwnerAuthorizedPartyHandler>()
                    .SetOrder(OpenIddict.Server.OpenIddictServerHandlers.Exchange.ValidateAuthorizedParty.Descriptor.Order));
            options.RegisterAudiences(oauthScopes.SelectMany(scope => scope.Resources).Distinct().ToArray());
            options.AddEventHandler<OpenIddict.Server.OpenIddictServerEvents.ProcessSignInContext>(handler =>
                handler.UseScopedHandler<CompanyName.ProjectName.Api.Auth.TokenExchangeExpirationHandler>()
                    .SetOrder(OpenIddict.Server.OpenIddictServerHandlers.PrepareIssuedTokenPrincipal.Descriptor.Order + 1));

            // 跨服务用签名 JWT：资源服务经 discovery/JWKS 验签，无需分发解密密钥；claim 对持有者可读（见 api.md 认证小节）。
            options.DisableAccessTokenEncryption();

            options.RegisterScopes(oauthScopes.Select(scope => scope.Name).ToArray());

            if (oauthOpts.UseDevelopmentCertificates)
            {
                options.AddDevelopmentEncryptionCertificate()
                    .AddDevelopmentSigningCertificate();
            }
            else
            {
                // 全部登记：重叠轮换期间新旧证书同时发布进 JWKS，旧证书签发或加密的令牌仍可验证、解密
                foreach (var certificate in encryptionCertificates) options.AddEncryptionCertificate(certificate);
                foreach (var certificate in signingCertificates) options.AddSigningCertificate(certificate);
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
            // 只接受签给本服务 API 的令牌：签给下游 API 的令牌（受众是那个 API）不能用来调用这里
            options.AddAudiences(oauthOpts.Resource);

            // 每个请求按令牌记录确认令牌未被撤销：停用、删除账号时撤销的令牌立即失效，
            // 在认证阶段就以 invalid_token 拒绝。API 与授权服务器同库部署，这次查库替代了逐请求查用户
            options.EnableTokenEntryValidation();

            // 普通 API 只接受 Bearer 头，避免令牌进入 URL 和访问日志。
            options.UseAspNetCore()
                   .DisableAccessTokenExtractionFromQueryString()
                   .DisableAccessTokenExtractionFromBodyForm();
        });
#endif
#if (RemoteTokenAuth)
    // Resource 的机器 Bearer 与服务端 OIDC 会话使用同一签发方和资源受众。
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
            // 发现文档与 JWKS 的抓取有界：签名公钥按需刷新时请求会等它（含官方重试）
            options.UseSystemNetHttp()
                .ConfigureHttpClient(client => client.Timeout = RefreshSigningKeysOnUnknownKeyIdentifier.FetchTimeout);
            options.AddEventHandler(RefreshSigningKeysOnUnknownKeyIdentifier.Descriptor);
            options.UseAspNetCore()
                .DisableAccessTokenExtractionFromQueryString()
                .DisableAccessTokenExtractionFromBodyForm();
        });
    // 在 OpenIddict 建好配置管理器之后给"请求刷新"限频（注册顺序即执行顺序）
    builder.Services.AddSingleton<IPostConfigureOptions<OpenIddict.Validation.OpenIddictValidationOptions>, ThrottleSigningKeyRefresh>();
#endif

    builder.Services.AddHostedService<ApplicationInitializer>();

    // 周期任务调度与进程内队列：组件登记的维护任务（操作记录归档、通知保留期、宿主级设置刷新）由它执行。
    // 集群任务经分布式锁 + 完成水位保证多副本同一时段只跑一次（多副本部署须配置 Redis）
    builder.Services.AddInProcessBackgroundJobs();

    builder.Services.AddGlobalExceptionHandler(ApiExceptionMappings.Configure);
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
    // 官方 OpenAPI 文档：控制器与组件端点都经 ApiExplorer 收录，只在 Development 映射（见下方 MapOpenApi）
    builder.Services.AddOpenApi();

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
        // 只用于前端部署在另一个源的形态；本机开发经前端开发服务器转发，同源，不需要跨域
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

        options.AddDefaultPolicy(policy =>
        {
            policy.AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
#if (LocalIdentity && IncludeMultiTenancy)

            // 跨域前端需要读取租户失效响应头以触发会话恢复。
            policy.WithExposedHeaders(TenantSessionRecoveryOptions.DefaultTenantInvalidHeader);
#endif

            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins);
            }
        });
    });

    builder.Services.AddSecurity();

#if (IncludeMultiTenancy)
#if (LocalIdentity)
    // Identity 持有租户注册表，因此校验解析结果。
    builder.Services.AddMultiTenancy();
#else
    // Resource 不持有注册表，只信已验证令牌的 tenant_id claim。
    builder.Services.AddMultiTenancy(options => options.ValidateResolvedTenant = false);
#endif
#else
    builder.Services.AddMultiTenancyCore();
#endif

#if (RemoteTokenAuth)
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddSingleton<IUserAccessTokenAccessor, ResourceUserAccessTokenAccessor>();
#endif

#if (IncludeNotifications || IncludeRealTime)
    // 心跳、超时、详细错误是 SignalR 自身的选项，由宿主直接配置。
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });
#endif
#if (IncludeRealTime)

    // 业务实时：资源事件推给订阅者。订阅授权必须由宿主明确选择（框架不给默认实现）：
    // 资源键须属于当前租户或宿主作用域，且订阅者持有查看该资源的权限
    builder.Services.AddRealTimeSignalR();
    builder.Services.AddTransient<IRealTimeSubscriptionAuthorizer, AppRealTimeSubscriptionAuthorizer>();
#endif
#if (IncludeNotifications)
#if (IncludeRealTime)

    // 通知与业务事件共用实时 Hub：客户端只建一条连接，通知的接收授权即该 Hub 的授权要求
    builder.Services.AddNotificationsSignalR<RealTimeHub>();
#else

    // 通知经通知自己的 Hub 推送（/hubs/notifications）
    builder.Services.AddNotificationsSignalR();
#endif

    // 通知偏好（收件人自己的用户级设置）决定哪类通知经哪个渠道收
#if (LocalIdentity)
    // 安全提醒的站内通知必达，不受偏好影响
    builder.Services.AddNotificationPreferences(options =>
        options.MandatoryDeliveries.Add(new NotificationDelivery(AppNotificationTypes.Security, AppNotificationChannels.InApp)));
#else
    builder.Services.AddNotificationPreferences();
#endif
#if (Email)
    // 邮件渠道只发已验证的邮箱，经后台队列发送
    builder.Services.AddEmailNotifications();
    builder.Services.AddScoped<INotificationRecipientResolver, UserEmailRecipientResolver>();
#endif
#if (LocalIdentity)
    // 安全提醒（新设备登录、密码与两步验证变更、账号锁定）经通知组件发给本人
    builder.Services.Replace(ServiceDescriptor.Transient<ISecurityAlertPublisher, NotificationSecurityAlertPublisher>());
#endif
#endif

#if (SpaFrontend)
    builder.Services.AddMyProjectDataProtection(builder.Configuration, builder.Environment);
    builder.Services.AddSingleton<DistributedTicketStore>();
    builder.Services.AddOptions<SessionCookieOptions>().BindConfiguration(SessionCookieOptions.SectionName);
    builder.Services.AddOptions<CookieAuthenticationOptions>(AuthenticationSchemeNames.SessionCookie)
        .Configure<DistributedTicketStore>((cookie, store) => cookie.SessionStore = store)
        .PostConfigure(DistributedTicketStore.ConfigureCookie);
#endif
#if (ExternalLogin)
    builder.Services.AddExternalAuthentication(builder.Configuration);
#endif

#if (LocalIdentity)
    // 会话 Cookie 的滑动过期与服务端会话的空闲时限是同一个值，只在这里定一次
    var sessionCookie = builder.Configuration.GetSection(SessionCookieOptions.SectionName).Get<SessionCookieOptions>()
        ?? new SessionCookieOptions();
    if (sessionCookie.ExpireDays < 1)
    {
        throw new InvalidOperationException(
            $"{SessionCookieOptions.SectionName}:ExpireDays must be at least 1 (was {sessionCookie.ExpireDays}).");
    }

    var sessionLifetime = TimeSpan.FromDays(sessionCookie.ExpireDays);
    builder.Services.Configure<UserSessionOptions>(options => options.IdleTimeout = sessionLifetime);
    // 应用 Cookie 策略不覆盖协议 correlation/nonce Cookie。
    builder.Services.AddOptions<CookieAuthenticationOptions>(AuthenticationSchemeNames.SessionCookie)
        .Configure<IOptions<SessionCookieOptions>>((cookie, sessionCookie) =>
        {
            // 默认 Lax；放宽前先看部署文档的 SameSite 说明（协议端点的跨站进入不需要放宽）
            if (sessionCookie.Value.SameSite is { } sameSite)
            {
                cookie.Cookie.SameSite = sameSite;
            }
        });
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IRequestClientInfo, HttpRequestClientInfo>();

    builder.Services.AddAuthentication(options =>
    {
#if (OpenIddictServer)
        // 多租户解析前必须按请求恢复 Bearer 或 Cookie 主体，防止请求头改写已登录用户的租户。
        options.DefaultAuthenticateScheme = AuthenticationSchemeNames.Smart;
        options.DefaultChallengeScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
#else
        // 不签发令牌时只恢复 Cookie 会话。
        options.DefaultAuthenticateScheme = AuthenticationSchemeNames.SessionCookie;
        options.DefaultChallengeScheme = AuthenticationSchemeNames.SessionCookie;
#endif
    })
#if (OpenIddictServer)
    .AddPolicyScheme(AuthenticationSchemeNames.Smart, "Selects Bearer or Cookie per request", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.ContainsKey("Authorization")
                ? OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                : AuthenticationSchemeNames.SessionCookie;
    })
#endif
    .AddCookie(AuthenticationSchemeNames.SessionCookie, options =>
    {
        var isDevelopmentEnvironment = builder.Environment.IsDevelopment();

        options.LoginPath = "/auth/login";
        // 部署环境用 __Host-Http- 前缀（RFC 10017 §6.1.3.2）：浏览器只接受经 HTTPS、Path=/、不带 Domain、
        // 由 HTTP 响应写入的这个名字。开发环境允许 HTTP 同源调试，而前缀要求 Secure，因此不带前缀。
        options.Cookie.Name = isDevelopmentEnvironment ? "CompanyName.ProjectName.Auth" : "__Host-Http-CompanyName.ProjectName.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = isDevelopmentEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;

        options.ExpireTimeSpan = sessionLifetime;
        options.SlidingExpiration = true;

        // 每个请求确认业务会话仍有效，服务端票据存储另负责 Cookie 引用的撤销。
        //
        // 撤销（退出其他设备、改密码）才能对已发出的 Cookie 生效。结果带短缓存，见 IUserSessionValidator
        options.Events.OnValidatePrincipal = async context =>
        {
#if (!IncludeMultiTenancy)
            if (!HostPrincipalMiddleware.IsHost(context.Principal, context.HttpContext.RequestServices.GetRequiredService<IOptions<ClaimTypeOptions>>().Value))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
                return;
            }
#endif
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

#elif (ResourceBrowserSession)
    foreach (var (key, value) in new[] { ("ClientId", remoteIdentity.ClientId), ("ClientSecret", remoteIdentity.ClientSecret) })
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"Authentication:{key} is required for the Resource OIDC confidential client.");
    var resourceCookie = builder.Configuration.GetSection(SessionCookieOptions.SectionName).Get<SessionCookieOptions>() ?? new();
    if (resourceCookie.ExpireDays < 1) throw new InvalidOperationException("SessionCookie:ExpireDays must be at least 1.");
    builder.Services.AddSingleton<ResourceSessionRefresher>();
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = AuthenticationSchemeNames.Smart;
        options.DefaultChallengeScheme = AuthenticationSchemeNames.Smart;
    })
    .AddPolicyScheme(AuthenticationSchemeNames.Smart, "Selects the request authentication scheme", options =>
        options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("Authorization")
            ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme : AuthenticationSchemeNames.SessionCookie)
    .AddCookie(AuthenticationSchemeNames.SessionCookie, options =>
    {
        // 前缀取舍同 LocalIdentity 的会话 Cookie。
        options.Cookie.Name = builder.Environment.IsDevelopment() ? "CompanyName.ProjectName.Auth" : "__Host-Http-CompanyName.ProjectName.Auth";
        options.Cookie.Path = "/";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SameSite = resourceCookie.SameSite ?? SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(resourceCookie.ExpireDays);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices.GetRequiredService<ResourceSessionRefresher>().ValidateAsync(context);
        options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
    })
    .AddOpenIdConnect(AuthenticationSchemeNames.OpenIdConnect, options =>
    {
        options.Authority = remoteIdentity.Issuer;
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.ClientId = remoteIdentity.ClientId;
        options.ClientSecret = remoteIdentity.ClientSecret;
        options.SignInScheme = AuthenticationSchemeNames.SessionCookie;
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.MapInboundClaims = false;
        options.CallbackPath = "/api/v1/auth/signin";
        // 授权与退出请求都以自动提交的表单 POST 发往 Identity：id_token_hint 不进地址栏、历史记录与 Referer
        options.AuthenticationMethod = OpenIdConnectRedirectBehavior.FormPost;
        options.SignedOutCallbackPath = "/api/v1/auth/signout";
        options.Scope.Clear();
        foreach (var scope in new[] { "openid", "profile", "email", "roles", "offline_access", remoteIdentity.Scope ?? remoteIdentity.Audience! }) options.Scope.Add(scope);
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return Task.CompletedTask;
        };
        options.Events.OnTokenValidated = async context =>
        {
            // tenant_id 等资源声明在签发方的访问令牌中，不能假定 ID token 也带这些字段。
            context.Principal = await ResourceSessionRefresher.ValidateAccessTokenAsync(context.HttpContext,
                context.TokenEndpointResponse!.AccessToken, context.HttpContext.RequestAborted);
        };
        options.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            // 保留 id_token_hint：Identity 据其中的会话标识判断能否免确认退出；client_id 在 hint 缺失时关联退出回调
            context.ProtocolMessage.ClientId = remoteIdentity.ClientId;
            return Task.CompletedTask;
        };
    });
#else
    // 纯资源 API：只接受 Bearer。没有浏览器会话，也就没有 Cookie、OIDC 客户端与登录退出端点
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
    });
#endif

    builder.Services.AddApiAuthorization();
    // 将权限定义作为 ASP.NET Core 授权策略解析。
    builder.Services.AddPermissionAuthorization();

    var app = builder.Build();
    // 宿主级设置配置源要在构建之后挂上：构建期间追加的配置源（如测试宿主的覆盖）不能排在它后面
    app.UseHostSettings();

#if (SpaFrontend)
    // 缺 Redis 不阻止启动：单实例配 DataProtection:KeysPath 是合法部署，副本数又无从推断。
    // 但分布式缓存随之回落到进程内存，多副本时各副本各一份、彼此看不见，只能靠这条告警在启动日志里发现。
    // 分布式锁的回落由锁组件自己告警，这里不重复。
    if (!app.Environment.IsDevelopment() && string.IsNullOrEmpty(app.Configuration.GetConnectionString("Redis")))
    {
#if (Email)
        const string cacheBackedState = "server-side session tickets, session revocation checks, two-factor challenges, captchas and email verification codes";
#elif (LocalIdentity)
        const string cacheBackedState = "server-side session tickets, session revocation checks, two-factor challenges and captchas";
#else
        const string cacheBackedState = "server-side session tickets";
#endif
        app.Logger.LogWarning(
            "ConnectionStrings:Redis is not configured, so the distributed cache falls back to process memory: {CacheBackedState} " +
            "are kept per process. This is only valid for a single instance; configure ConnectionStrings:Redis before running more replicas.",
            cacheBackedState);
    }
#endif

    app.UseForwardedHeaders();
#if (IncludeLocalization)
    // 在所有读取当前区域性的中间件之前解析请求区域性。
    app.UseJsonRequestLocalization();
#endif
#if (SpaFrontend)
    app.UseDefaultFiles();
    app.UseStaticFiles(SpaExtensions.CreateSpaStaticFileOptions());
#endif

    var requestLogging = app.Services.GetRequiredService<IOptionsMonitor<RequestLoggingOptions>>();
    app.UseCorrelationId();
    app.UseSerilogRequestLogging(options =>
    {
        // 宿主 logger 独立持有（preserveStaticLogger）：不指定时中间件写进启动期的静态 logger，
        // 请求完成事件就丢掉配置里的输出格式、sink 与 enrich
        options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        options.GetLevel = (httpContext, elapsed, ex) =>
        {
            if (ex != null || httpContext.Response.StatusCode >= 500)
                return Serilog.Events.LogEventLevel.Error;

            // 正常完成的请求记成哪一级由设置决定：调到 Verbose 就等于关掉请求日志
            // （全局最小级别通常是 Information，Verbose 不会落盘）。
            // 失败与 5xx 不受它影响——那是排障必需的，不该被一个运维开关关掉。
            return requestLogging.CurrentValue.Level;
        };
    });
    app.UseGlobalExceptionHandler();
    // 框架只写状态码、不写响应体的失败（生产环境的请求体解析失败、415、未匹配路由、认证质询、限流）
    // 经问题详情管道补上标准响应体与 traceId。只作用于 API：SPA 页面与静态资源的 404 不改写成 JSON。
    app.UseWhen(
        context => context.Request.Path.StartsWithSegments("/api"),
        api => api.UseStatusCodePages());
    app.MapHealthChecks("/api/health/live", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("live")
    }).AllowAnonymous();
    app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready")
    }).AllowAnonymous();

    app.UseRouting();
    app.UseCors();

#if (!SpaFrontend && (IncludeNotifications || IncludeRealTime))
    // 浏览器连 Hub 只能把 Bearer 放在查询串：只在 Hub 端点上转成 Authorization 头，普通 API 仍只认请求头
    app.UseHubAccessToken();
#endif
    app.UseAuthentication();
#if (SpaFrontend)
    // 浏览器会话靠 Cookie：跨源写请求与 Hub 握手只接受本源与登记的前端源
    app.UseMiddleware<BrowserOriginMiddleware>();
#endif
#if (LocalIdentity && IncludeMultiTenancy)
    // 租户失效时注销 Cookie，避免会话困在不可用租户中。
    app.UseTenantSessionRecovery(options => options.SignOutScheme = AuthenticationSchemeNames.SessionCookie);
#endif
    // 租户在认证后、授权前解析；未解析到租户表示宿主上下文。
#if (IncludeMultiTenancy)
    app.UseMultiTenancy();
#else
    app.UseMiddleware<HostPrincipalMiddleware>();
#endif
    // 请求完成日志在租户作用域之外写出：经 Serilog 诊断上下文补上租户，键与 ICurrentTenant.Change 打开的日志作用域一致
    app.Use((context, next) =>
    {
        context.RequestServices.GetRequiredService<IDiagnosticContext>()
            .Set(TenantLogKeys.TenantId, context.RequestServices.GetRequiredService<ICurrentTenant>().Id);
        return next(context);
    });
#if (!LocalIdentity)
    // 必须在 UseMultiTenancy() 之后：用户行是 IMultiTenant，租户没解析出来会落成宿主行。
    app.UseMiddleware<ResourceUserProvisioningMiddleware>();
#endif
#if (LocalIdentity)
    // 受限会话（租户要求两步验证而本人未启用）只放行完成设置所需的接口
    app.UseMiddleware<TwoFactorSetupEnforcementMiddleware>();
#endif
    app.UseAuthorization();
    // 授权之后、租户作用域之内：组件端点被业务规则拒绝时补一条操作记录（见中间件注释）
    app.UseMiddleware<OperationFailureRecordingMiddleware>();

    app.MapControllers();
    // 组件自带的端点（设置、权限、操作记录、通知、租户与租户连接），路由与原控制器一致
    app.MapComponentEndpoints();
    if (app.Environment.IsDevelopment())
    {
        // 本机查看接口：/openapi/v1.json。其他环境不暴露接口清单
        app.MapOpenApi().AllowAnonymous();
    }

#if (IncludeRealTime)
    // 业务事件与（启用时的）通知都经实时 Hub 推送，只映射这一个
    app.MapRealTimeHub();
#elif (IncludeNotifications)
    app.MapNotificationHub();
#endif

#if (SpaFrontend)
    app.MapMyProjectSpaFallback();
#endif

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
