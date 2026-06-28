using CompanyName.ProjectName.Api.Extensions;
using CompanyName.ProjectName.Api.HostedServices.Initializer;

using CompanyName.ProjectName.Application;
using CompanyName.ProjectName.Domain;
using CompanyName.ProjectName.Domain.Users.Options;
using CompanyName.ProjectName.Domain.Shared.Json;
using CompanyName.ProjectName.Infrastructure;
using CompanyName.ProjectName.Infrastructure.Persistence;
using Leistd.DependencyInjection;
using Leistd.Exception.AspNetCore;
using Leistd.Security.AspNetCore;
using Leistd.Tracing.AspNetCore;
#if (IncludeNotifications)
using Leistd.Notifications.AspNetCore.SignalR;
#endif
#if (IncludeIdentity)
using Microsoft.AspNetCore.Authentication;
#if (IncludeOpenIddict || IncludeExternalLogin)
using CompanyName.ProjectName.Domain.Auth.Options;
#endif
#if (IncludeOpenIddict)
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
    builder.Host.UseServiceProviderFactory(new ServiceRegistrationCallbackFactory());

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
                OpenIddictConstants.Scopes.Roles,
                OpenIddictConstants.Scopes.OfflineAccess);

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
                        System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
                            oauthOpts.EncryptionCertificatePath,
                            oauthOpts.EncryptionCertificatePassword));
                }
                if (!string.IsNullOrEmpty(oauthOpts.SigningCertificatePath))
                {
                    options.AddSigningCertificate(
                        System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(
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
            options.UseAspNetCore();
        });
#endif
// (IncludeOpenIddict)

    // 3. 注册应用启动引导程序 (替代手动 InitializeApplicationAsync)
    builder.Services.AddHostedService<ApplicationBootstrapper>();

    // 4. API 层基础设施 (Exception, HealthChecks, Controllers)
    builder.Services.AddGlobalExceptionHandler(builder.Configuration);
    builder.Services.AddHealthChecks();
    builder.Services.AddMyProjectSpaProxy();
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // 使用 Domain.Shared 层的 WebApi 配置
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonOptions.WebApi.PropertyNamingPolicy;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonOptions.WebApi.DefaultIgnoreCondition;

            // 复制转换器
            foreach (var converter in JsonOptions.WebApi.Converters)
            {
                options.JsonSerializerOptions.Converters.Add(converter);
            }
        });

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

#if (IncludeNotifications)
    // 4.5.1 Leistd Notifications — SignalR 实时通知 + EF Core 持久化
    builder.Services.AddNotificationsSignalR(opt =>
    {
        opt.EnableDetailedErrors = builder.Environment.IsDevelopment();
    });
#endif

    // 4.6. DataProtection 配置（生产环境必需）
    builder.Services.AddMyProjectDataProtection(builder.Configuration, builder.Environment);

    // 5. 安全配置 (AuthN & AuthZ)
#if (IncludeIdentity)
    builder.Services.AddAuthentication(options =>
    {
#if (IncludeOpenIddict)
        // 启用 OpenIddict 时，默认走其 Bearer 校验；未启用时默认走 Cookie。
        options.DefaultAuthenticateScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
#else
        options.DefaultAuthenticateScheme = "MyProjectCookie";
        options.DefaultChallengeScheme = "MyProjectCookie";
#endif
    })
    .AddCookie("MyProjectCookie", options =>
    {
        options.LoginPath = "/auth/login";
        options.Cookie.Name = "CompanyName.ProjectName.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.None;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;

#if (IncludeOpenIddict)
        var oauthConfig = builder.Configuration.GetSection(OAuthOptions.SectionName).Get<OAuthOptions>() ?? new OAuthOptions();
        options.ExpireTimeSpan = TimeSpan.FromDays(oauthConfig.CookieExpireDays);
#else
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
#endif
        options.SlidingExpiration = true;
    });

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
            .Build();

        // 超级管理员策略：基于 is_super_admin claim 判定（无需查库）
        options.AddPolicy("SuperAdmin", policy => policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .RequireClaim(Leistd.Security.Claims.CustomClaimTypes.IsSuperAdmin, "true"));
    });
#else
    builder.Services.AddAuthorization();
#endif

    // --- 构建应用 ---
    var app = builder.Build();

    // ✅ 在应用启动前执行数据库迁移（确保数据库就绪后再接收请求）
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MyProjectDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        if (db.Database.IsRelational())
        {
            var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
            if (pendingMigrations.Any())
            {
                logger.LogInformation("检测到关系型数据库，正在应用 {Count} 个待执行迁移...", pendingMigrations.Count());
                await db.Database.MigrateAsync();
                logger.LogInformation("数据库迁移完成");
            }
            else
            {
                logger.LogInformation("检测到关系型数据库，无待执行迁移，使用 EnsureCreated 创建表结构...");
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
#endif
    app.UseAuthorization();

    app.MapControllers();

#if (IncludeNotifications)
    // SignalR 通知 / 实时业务事件 Hub 端点
    app.MapNotificationsHubs();
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
