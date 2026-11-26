#if (LocalIdentity)
#if (Email)
using Leistd.Security.OneTimeCodes.VerificationCodes;
#endif
using CompanyName.ProjectName.Api.Auth.Sessions;
#if (!IncludeMultiTenancy)
using CompanyName.ProjectName.Api.Middlewares;
#endif
using Leistd.Security.AspNetCore;
using CompanyName.ProjectName.Application.Auth.Sessions;
using CompanyName.ProjectName.Application.Shared;
using CompanyName.ProjectName.Domain.Auth.Options;
using CompanyName.ProjectName.Application.Users.Options;
using Leistd.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
#if (OpenIddictServer)
using OpenIddict.Validation.AspNetCore;
#endif

namespace CompanyName.ProjectName.Api.Auth.Authentication;

/// <summary>本地身份的账号配置与认证：服务端登记的会话 Cookie（签发形态另有 Bearer）。</summary>
internal static class LocalSessionAuthenticationExtensions
{
    public static void AddLocalSessionAuthentication(this WebApplicationBuilder builder)
    {
        AddAccountOptions(builder.Services, builder.Configuration);

        builder.Services.AddOptions<UserSessionOptions>()
            .BindConfiguration(UserSessionOptions.SectionName)
            .Validate(options => options.SessionHours is >= 1 and <= 8760
                && options.KeepSignedInDays is >= 1 and <= 365
                && options.IdleDays >= 1 && options.IdleDays <= options.KeepSignedInDays
                && options.SessionHours <= options.KeepSignedInDays * 24
                && options.RememberDeviceMaxDays is >= 1 and <= 365,
                "SessionCookie lifetimes are out of range.")
            .ValidateOnStart();
        builder.Services.AddRequestClientInfo();
        builder.Services.TryAddTransient<SessionCookieIssuer>();
        builder.Services.TryAddTransient<PendingSignInCookie>();
        builder.Services.TryAddTransient<RememberedDeviceCookie>();

        builder.Services.AddAuthentication(options =>
        {
#if (OpenIddictServer)
            // 多租户解析前必须按请求恢复 Bearer 或 Cookie 主体，防止请求头改写已登录用户的租户。
            options.DefaultAuthenticateScheme = AuthenticationSchemeNames.Smart;
            // 挑战（及回落到它的禁止）与认证同一分流：带 Authorization 头由 OpenIddict 回应，否则由会话 Cookie 写 401/403。
            // 不指定方案的策略（组件端点）匿名被拒走默认挑战；若交给 OpenIddict，它对未经它认证的请求答 insufficient_access（403）
            options.DefaultChallengeScheme = AuthenticationSchemeNames.Smart;
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
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
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

            // 每个请求确认业务会话仍有效，服务端票据存储另负责 Cookie 引用的撤销。
            //
            // 撤销（退出其他设备、改密码）才能对已发出的 Cookie 生效。有效性带短缓存，受限判定不缓存，见 IUserSessionValidator
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
                var services = context.HttpContext.RequestServices;
                var result = context.Principal is null
                    ? SessionValidationResult.Invalid
                    : await services.GetRequiredService<IUserSessionValidator>()
                        .ValidateAsync(context.Principal, context.HttpContext.RequestAborted);
                if (!result.IsValid ||
                    !Guid.TryParse(context.Principal!.FindFirst(CustomClaimTypes.SessionId)?.Value, out var sessionId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(AuthenticationSchemeNames.SessionCookie);
                    return;
                }

                // 受限状态只写入本请求，账号或租户策略变更后无需换发票据。
                services.GetRequiredService<ICurrentSessionState>().Set(sessionId, result.TwoFactorSetupRequired);
            };

            options.Events.OnSigningIn = async context =>
            {
                var reader = context.HttpContext.RequestServices.GetRequiredService<ISessionExpiryReader>();
                if (context.Principal is not null
                    && await reader.ReadAsync(context.Principal, context.HttpContext.RequestAborted) is { } expiresAt)
                    context.Properties.ExpiresUtc = new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc));
                context.Properties.AllowRefresh = false;
            };

            // API 拒绝必须返回 401/403，不能被 Cookie 重定向和 SPA 回退转换为 HTML 200。
            options.Events.OnRedirectToLogin = context => WriteStatusAsync(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => WriteStatusAsync(context, StatusCodes.Status403Forbidden);

            static Task WriteStatusAsync(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
            {
                context.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            }
        })
        .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
        {
            var development = builder.Environment.IsDevelopment();
            options.Cookie.Name = development ? "CompanyName.ProjectName.RememberDevice" : "__Host-Http-CompanyName.ProjectName.RememberDevice";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.IsEssential = true;
            options.SlidingExpiration = false;
        })
        // 待完成登录（口令或外部登录已过、尚待第二步）的临时 Cookie：官方两步登录方案，见 PendingSignInCookie。
        // 不进默认方案与 Smart 选择器，它不是正式身份；名字与属性沿用会话 Cookie 的约定。
        // SameSite 固定 Lax：依赖方跨站顶层导航到授权端点时要带上它，授权端点据此接回第二步
        .AddTwoFactorUserIdCookie()
        .Configure(options =>
        {
            var isDevelopmentEnvironment = builder.Environment.IsDevelopment();

            options.Cookie.Name = isDevelopmentEnvironment ? "CompanyName.ProjectName.TwoFactor" : "__Host-Http-CompanyName.ProjectName.TwoFactor";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = isDevelopmentEnvironment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.IsEssential = true;
            // 绝对期限等于挑战到期（签入时写入 ExpiresUtc），不滑动：口令已过的中间状态要有固定上界
            options.SlidingExpiration = false;
        });
    }

    // 账号相关的部署基线：管理员引导、邮箱验证码密钥、注册策略
    private static void AddAccountOptions(IServiceCollection services, IConfiguration configuration)
    {
        // 口令不在启动期校验：只有真的要创建管理员时才需要，由初始化器在那一刻按口令策略校验并报出键名
        services.AddOptions<DefaultAdminOptions>()
            .Bind(configuration.GetSection(DefaultAdminOptions.SectionName));

#if (Email)
        // 邮箱验证开启时，HMAC 密钥必须跨实例和重启稳定。
        services.AddOptions<VerificationCodeOptions>()
            .Validate<IConfiguration>(
                (options, config) =>
                    !config.GetValue<bool>("UserRegistration:EnableEmailVerification")
                    || options.IsKeyUsable,
                $"{VerificationCodeOptions.SectionName}:Key is required when " +
                "UserRegistration:EnableEmailVerification is true, and must be at least " +
                $"{VerificationCodeOptions.MinimumKeyBytes} base64-encoded bytes.")
            .ValidateOnStart();
#endif
        services.AddOptions<UserRegistrationOptions>()
            .Bind(configuration.GetSection(UserRegistrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }
}
#endif
