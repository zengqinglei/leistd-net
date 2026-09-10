using System.Globalization;
using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Dtos;
using CompanyName.ProjectName.Application.Settings.Hosting;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Application.Settings.Timing;
using Leistd.Authorization.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.Abstractions;
using Leistd.Security.Users;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.UnitOfWork.Attributes;
using Leistd.Ddd.Application.AppService;
#if (LocalIdentity)
using CompanyName.ProjectName.Domain.Auth.Options;
using Microsoft.Extensions.Options;
#endif
#if (IncludeLocalization)
using Microsoft.Extensions.Localization;
#endif

namespace CompanyName.ProjectName.Application.Settings.AppServices;

/// <summary>
/// 设置读写应用服务实现
/// </summary>
/// <remarks>
/// 只下发标记为可见客户端的设置：定义里可能有运维阈值一类不该出现在界面上的项。
/// 写入分两条路——改自己的偏好任何登录用户都可以，改租户默认值需要管理权限。
/// <para>
/// 读取直接走 <see cref="ISettingStore"/> 按层取原始覆盖值，而不是 <see cref="ISettingProvider"/>
/// 的回落结果：设置页要分层编辑，回落后的值分不出"这层设过"和"从下一层继承来的"。
/// 业务代码消费设置值时仍应注入 <see cref="ISettingProvider"/>。
/// </para>
/// </remarks>
public class SettingAppService(
    ISettingStore settingStore,
    ISettingDefinitionManager settingDefinitionManager,
    IUserTimeZoneProvider userTimeZoneProvider,
    ISettingManager settingManager,
    IPermissionChecker permissionChecker,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IEnumerable<IHostSettingApplier> hostSettingAppliers
#if (LocalIdentity)
    ,
    IOptions<VerificationCodeOptions> verificationCodeOptions
#endif
#if (IncludeLocalization)
    ,
    IStringLocalizerFactory localizerFactory
#endif
    ) : BaseAppService, ISettingAppService
{
#if (IncludeLocalization)
    // 与前端 SUPPORTED_LANGS 和 Program.cs 的 AddJsonLocalization 保持一致；
    // 三处任一漂移都会让某个语言只在一半链路上可用。
    private static readonly string[] SupportedLanguages = ["en", "zh-CN"];

#endif
    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingOutputDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        // 宿主上下文下这一层读到的就是宿主行，进程级设置与宿主的租户级共用它。
        var tenantValues = await settingStore.GetAllAsync(SettingScopes.Tenant, userId: null, cancellationToken);
        var isHost = currentTenant.Id is null;

        var userId = currentUser.Id?.ToString();
        var userValues = userId is null
            ? new Dictionary<string, string>()
            : await settingStore.GetAllAsync(SettingScopes.User, userId, cancellationToken);

        return [.. settingDefinitionManager.GetAll()
            .Where(definition => definition.IsVisibleToClients)
            // 进程级设置在租户上下文里既改不了也不适用于该租户，索性不下发：
            // 下发一个只能看、改了还会被拒的项，比看不到更让人困惑。
            .Where(definition => isHost || !definition.Scopes.HasFlag(SettingScopes.Host))
            .Select(definition => new SettingOutputDto(
                definition.Name,
                Localize(definition.Name, definition.DisplayName),
                GroupOf(definition.Group),
                LocalizeGroup(GroupOf(definition.Group)),
                definition.Scopes.HasFlag(SettingScopes.User) ? userValues.GetValueOrDefault(definition.Name) : null,
                // 进程级设置也存在这一层（宿主行），只按 Tenant 标记取值会让它永远显示成"未覆盖"：
                // 存下去了、读不回来，界面上就是"改了没生效"。
                definition.Scopes.HasFlag(SettingScopes.Tenant) || definition.Scopes.HasFlag(SettingScopes.Host)
                    ? tenantValues.GetValueOrDefault(definition.Name)
                    : null,
                definition.DefaultValue,
                definition.Scopes.HasFlag(SettingScopes.Tenant),
                definition.Scopes.HasFlag(SettingScopes.User),
                definition.Scopes.HasFlag(SettingScopes.Host),
                RangeOf(definition.Name)?.Minimum,
                RangeOf(definition.Name)?.Maximum))];
    }

    /// <inheritdoc />
    [UnitOfWork]
    public async Task SetForCurrentUserAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString()
            ?? throw new UnauthorizedException("Only an authenticated user can change personal settings.");

        _ = EnsureVisibleToClients(input.Name);
        EnsureValueIsValid(input.Name, input.Value);

        await settingManager.SetAsync(input.Name, input.Value, SettingScopes.User, userId, cancellationToken);
    }

    /// <inheritdoc />
    [UnitOfWork]
    public async Task SetForCurrentTenantAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.Settings.Default, cancellationToken))
            throw new ForbiddenException("Changing tenant settings requires the settings management permission.");

        var definition = EnsureVisibleToClients(input.Name);
        EnsureValueIsValid(input.Name, input.Value);

        // 进程级设置只有宿主那一行。租户上下文下必须就地拒绝，而不是写成一条租户行：
        // 那条行永远不会被任何 logger 读到，界面却会把它显示成"已生效"。
        var scope = definition.Scopes.HasFlag(SettingScopes.Host)
            ? SettingScopes.Host
            : SettingScopes.Tenant;

        if (scope == SettingScopes.Host && currentTenant.Id is not null)
        {
            throw new ForbiddenException(
                $"Setting '{input.Name}' is process-wide and can only be changed on the host.");
        }

        await settingManager.SetAsync(input.Name, input.Value, scope, cancellationToken: cancellationToken);

        // 写完立刻对本进程生效；其它实例在下一次刷新周期跟上。
        if (scope == SettingScopes.Host)
        {
            foreach (var applier in hostSettingAppliers)
            {
                await applier.ApplyAsync(cancellationToken);
            }
        }
    }

    // 值域在服务端把关，不能只靠界面的候选项：脚本、旧版客户端和迁移进来的数据都绕得过界面，
    // 一个非法值留在库里，之后每个消费方都得自己防御。控件长什么样是界面的事，值合不合法是这里的事。
    private void EnsureValueIsValid(string name, string? value)
    {
        // 只有 null 表示清除，这是 DTO 与存储共用的唯一语义。空字符串不能一起放行：
        // 存储层只删 null，"" 会作为真实值落库，既挡住向下一层的回落，
        // 又被语言、时区这些消费方当成「未设置」——一个值同时是两种意思。
        if (value is null)
            return;

        // 空串对任何设置都不是合法值：语义上「空」就是未设置，而清除已经有 null 表示它。
        // 放行会让每个消费方都要再判一次空，等于把清除做成两套协议。
        if (value.Length == 0)
            throw new BadRequestException(
                $"An empty value is not accepted for '{name}'. Send null to clear the override.")
#if (IncludeLocalization)
                .WithCode("Setting:EmptyValueRejected").WithData("Name", name)
#endif
                ;

        switch (name)
        {
#if (IncludeLocalization)
            case SettingConstant.Display.Language when !SupportedLanguages.Contains(value):
                throw new BadRequestException(
                    $"'{value}' is not a supported language. Supported: {string.Join(", ", SupportedLanguages)}.")
                    .WithCode("Setting:LanguageUnsupported")
                    .WithData("Value", value)
                    .WithData("Supported", string.Join(", ", SupportedLanguages));
#endif
            // 与读取端共用同一套判定，不会出现"写得进去却解析不出来"。
            case SettingConstant.Display.TimeZone when !userTimeZoneProvider.IsValidId(value):
                throw new BadRequestException($"'{value}' is not a valid IANA time zone id.")
#if (IncludeLocalization)
                    .WithCode("Setting:TimeZoneInvalid").WithData("Value", value)
#endif
                    ;

            case SettingConstant.Logging.MinimumLevel or SettingConstant.Logging.RequestLevel
                when !SettingConstant.Logging.Levels.Contains(value, StringComparer.Ordinal):
                throw new BadRequestException(
                    $"'{value}' is not a valid log level. Valid: {string.Join(", ", SettingConstant.Logging.Levels)}.")
#if (IncludeLocalization)
                    .WithCode("Setting:LogLevelInvalid")
                    .WithData("Value", value)
                    .WithData("Valid", string.Join(", ", SettingConstant.Logging.Levels))
#endif
                    ;
#if (LocalIdentity)

            case SettingConstant.Registration.EnableEmailVerification
                when value is not ("true" or "false"):
                throw new BadRequestException($"'{value}' is not a boolean; use 'true' or 'false'.")
#if (IncludeLocalization)
                    .WithCode("Setting:BooleanRequired").WithData("Value", value)
#endif
                    ;

            // 开启前必须确认部署已经给了可用的摘要密钥。
            //
            // 密钥仍然只由配置/密钥设施提供，不进设置表；但"能不能开"取决于它在不在。
            // 启动校验只看配置里的那个布尔值，所以默认部署（关闭邮箱验证、未配密钥）
            // 启动是正常的——管理员随后在设置页打开，写入端只校验布尔值就放行，
            // 直到真的发码时才在摘要计算处抛 500。那不是"更强的安全措施"的问题，
            // 而是这个开关允许进入一个缺少运行前提的状态。
            case SettingConstant.Registration.EnableEmailVerification
                when value == "true" && !verificationCodeOptions.Value.IsKeyUsable:
                throw new BadRequestException(
                    "Email verification cannot be enabled: this deployment has no usable "
                    + $"{VerificationCodeOptions.SectionName}:Key. Provide a stable Base64 key of at "
                    + $"least {VerificationCodeOptions.MinimumKeyBytes} bytes from the deployment "
                    + "(environment variable, user-secrets or a secret store) and restart.")
#if (IncludeLocalization)
                    .WithCode("Setting:EmailVerificationKeyMissing")
                    .WithData("Section", VerificationCodeOptions.SectionName)
                    .WithData("MinimumKeyBytes", VerificationCodeOptions.MinimumKeyBytes)
#endif
                    ;

            // 区间读 SettingConstant.Registration.Ranges 那一份，与下发给界面的
            // minimum/maximum 同源：分开写两份时，界面让填的和服务端收的会各走一边。
            case var _ when SettingConstant.Registration.Ranges.TryGetValue(name, out var range):
                EnsureInRange(name, value, range.Minimum, range.Maximum);
                break;
#endif
        }
    }

    // 只有面向界面的设置可以经本服务写入：不可见的设置往往是运维阈值，
    // 让它们经普通用户接口可写，等于把内部参数暴露成了业务能力。
    private ISettingDefinition EnsureVisibleToClients(string name)
    {
        var definition = settingDefinitionManager.GetOrNull(name);
        if (definition is null || !definition.IsVisibleToClients)
            throw new NotFoundException($"Setting '{name}' is not available.");

        return definition;
    }

    // 数值区间下发给界面，让它渲染带上下界的数字输入框。**值域仍以服务端为准**——
    // 界面的约束管不住脚本与历史数据，这里只是让界面别把明知非法的值放进来。
    private static (int Minimum, int Maximum)? RangeOf(string name)
    {
#if (LocalIdentity)
        return SettingConstant.Registration.Ranges.TryGetValue(name, out var range) ? range : null;
#else
        _ = name;
        return null;
#endif
    }

    // 未分组的设置归入固定的"其他"，而不是让它落在界面之外：新增设置忘了写分组时，
    // 界面上少一项是查不出来的，摆在"其他"里则一眼就能看到该归类了。
    private static string GroupOf(string? group)
        => string.IsNullOrWhiteSpace(group) ? SettingConstant.Groups.Other : group;

#if (LocalIdentity)
    private static void EnsureInRange(string name, string value, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < min || parsed > max)
        {
            throw new BadRequestException($"'{name}' must be an integer between {min} and {max}.")
#if (IncludeLocalization)
                .WithCode("Setting:ValueOutOfRange")
                .WithData("Name", name)
                .WithData("Minimum", min)
                .WithData("Maximum", max)
#endif
                ;
        }
    }

#endif
    // 分组名与设置名同一套办法：词条键按 SettingGroup:{group} 推导，查不到就用标识本身。
    private string LocalizeGroup(string group)
    {
#if (IncludeLocalization)
        var localized = localizerFactory.Create(typeof(SettingAppService))[$"SettingGroup:{group}"];
        if (!localized.ResourceNotFound)
            return localized.Value;
#endif
        return group;
    }

    // 定义是 Singleton、启动时加载，翻译必须发生在响应阶段，否则先到的那个请求的语言
    // 会被固化给所有人。词条键按 Setting:{name} 推导，定义里只写回落文案。
    private string Localize(string name, string? displayName)
    {
#if (IncludeLocalization)
        var localized = localizerFactory.Create(typeof(SettingAppService))[$"Setting:{name}"];
        if (!localized.ResourceNotFound)
            return localized.Value;
#endif
        return string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }
}
