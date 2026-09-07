using CompanyName.ProjectName.Application.Permissions.Provider;
using CompanyName.ProjectName.Application.Settings.Dtos;
using CompanyName.ProjectName.Application.Settings.Provider;
using CompanyName.ProjectName.Application.Settings.Timing;
using Leistd.Authorization.Abstractions;
using Leistd.ExceptionHandling;
using Leistd.Security.Users;
using Leistd.Settings.Abstractions;
using Leistd.Settings.Definitions;
using Leistd.UnitOfWork.Attributes;
using Leistd.Ddd.Application.AppService;
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
    ICurrentUser currentUser
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
        var tenantValues = await settingStore.GetAllAsync(SettingScopes.Tenant, userId: null, cancellationToken);

        var userId = currentUser.Id?.ToString();
        var userValues = userId is null
            ? new Dictionary<string, string>()
            : await settingStore.GetAllAsync(SettingScopes.User, userId, cancellationToken);

        return [.. settingDefinitionManager.GetAll()
            .Where(definition => definition.IsVisibleToClients)
            .Select(definition => new SettingOutputDto(
                definition.Name,
                Localize(definition.Name, definition.DisplayName),
                definition.Scopes.HasFlag(SettingScopes.User) ? userValues.GetValueOrDefault(definition.Name) : null,
                definition.Scopes.HasFlag(SettingScopes.Tenant) ? tenantValues.GetValueOrDefault(definition.Name) : null,
                definition.DefaultValue,
                definition.Scopes.HasFlag(SettingScopes.Tenant),
                definition.Scopes.HasFlag(SettingScopes.User)))];
    }

    /// <inheritdoc />
    [UnitOfWork]
    public async Task SetForCurrentUserAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.Id?.ToString()
            ?? throw new UnauthorizedException("Only an authenticated user can change personal settings.");

        EnsureVisibleToClients(input.Name);
        EnsureValueIsValid(input.Name, input.Value);

        await settingManager.SetAsync(input.Name, input.Value, SettingScopes.User, userId, cancellationToken);
    }

    /// <inheritdoc />
    [UnitOfWork]
    public async Task SetForCurrentTenantAsync(SetSettingInputDto input, CancellationToken cancellationToken = default)
    {
        if (!await permissionChecker.IsGrantedAsync(PermissionConstant.Settings.Default, cancellationToken))
            throw new ForbiddenException("Changing tenant settings requires the settings management permission.");

        EnsureVisibleToClients(input.Name);
        EnsureValueIsValid(input.Name, input.Value);

        await settingManager.SetAsync(input.Name, input.Value, SettingScopes.Tenant, cancellationToken: cancellationToken);
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
                $"An empty value is not accepted for '{name}'. Send null to clear the override.");

        switch (name)
        {
#if (IncludeLocalization)
            case SettingConstant.Display.Language when !SupportedLanguages.Contains(value):
                throw new BadRequestException(
                    $"'{value}' is not a supported language. Supported: {string.Join(", ", SupportedLanguages)}.");
#endif
            // 与读取端共用同一套判定，不会出现"写得进去却解析不出来"。
            case SettingConstant.Display.TimeZone when !userTimeZoneProvider.IsValidId(value):
                throw new BadRequestException($"'{value}' is not a valid IANA time zone id.");
        }
    }

    // 只有面向界面的设置可以经本服务写入：不可见的设置往往是运维阈值，
    // 让它们经普通用户接口可写，等于把内部参数暴露成了业务能力。
    private void EnsureVisibleToClients(string name)
    {
        var definition = settingDefinitionManager.GetOrNull(name);
        if (definition is null || !definition.IsVisibleToClients)
            throw new NotFoundException($"Setting '{name}' is not available.");
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
