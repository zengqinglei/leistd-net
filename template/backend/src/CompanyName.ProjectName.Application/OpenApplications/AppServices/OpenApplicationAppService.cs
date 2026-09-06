#if (LocalIdentity)
using Leistd.ExceptionHandling;
using System.Security.Cryptography;
using System.Text.Json;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Application.TenantConnections;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.Extensions.Logging;
using CompanyName.ProjectName.Application.OpenApplications.Mappings;
using Leistd.ObjectMapping.Abstractions;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.Application.OpenApplications.AppServices;

public class OpenApplicationAppService(
    IOpenIddictApplicationManager applicationManager,
    IObjectMapper objectMapper,
    ILogger<OpenApplicationAppService> logger) : BaseAppService, IOpenApplicationAppService
{
    private const string PkceRequirement = "ft:pkce";

    private static readonly HashSet<string> ApplicationTypes = new(StringComparer.Ordinal)
    {
        OpenIddictConstants.ApplicationTypes.Web,
        OpenIddictConstants.ApplicationTypes.Native,
        "service"
    };

    private static readonly HashSet<string> ClientTypes = new(StringComparer.Ordinal)
    {
        OpenIddictConstants.ClientTypes.Public,
        OpenIddictConstants.ClientTypes.Confidential
    };

    /// <summary>
    /// 本项目实际注册的 OIDC scope（见 <c>Program.cs</c> 的 <c>RegisterScopes</c>）。
    /// </summary>
    /// <remarks>
    /// 只校验 <c>scp:</c> 前缀这一类：客户端可以请求一个服务端根本没注册的 scope，
    /// 存得下但发令牌时必然被拒——写入时报错比留一个"配置得上、用不了"的客户端好排查。
    /// 裁掉角色能力的项目里 <c>roles</c> 不存在，界面已不展示，接口也不该收。
    /// 其余前缀（ept:/gt:/rst:/ft:）不在此校验：为它们维护一份完整词汇表的成本远大于收益。
    /// </remarks>
    private static readonly HashSet<string> RegisteredScopePermissions = new(StringComparer.Ordinal)
    {
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.Roles,
        OpenIddictConstants.Permissions.Prefixes.Scope + OpenIddictConstants.Scopes.OfflineAccess,
        OpenIddictConstants.Permissions.Prefixes.Scope + TenantConnectionScopes.RuntimeRead,
        OpenIddictConstants.Permissions.Prefixes.Scope + TenantConnectionScopes.MigrationRead
    };

    /// <summary>
    /// 内部控制面 scope：只能发给服务间调用的机器客户端
    /// </summary>
    /// <remarks>
    /// 它们背后的端点直接暴露租户连接配置（数据落在哪个库、运行时/迁移 Secret 引用），
    /// 资源端策略已限定为机器主体（见 <c>AddApiAuthorization</c>）。这里是<b>配置入口</b>侧的
    /// 第二道：授出去就没有回收窗口，创建时挡住比事后审计便宜。两层都要有——
    /// 只靠配置入口挡不住已存在的客户端，只靠资源端则允许留下一堆"配得上、用不了"的客户端。
    /// </remarks>
    private static readonly HashSet<string> MachineOnlyScopePermissions = new(StringComparer.Ordinal)
    {
        OpenIddictConstants.Permissions.Prefixes.Scope + TenantConnectionScopes.RuntimeRead,
        OpenIddictConstants.Permissions.Prefixes.Scope + TenantConnectionScopes.MigrationRead
    };

    /// <summary>
    /// 代表自然人的授权流：与内部控制面 scope 互斥
    /// </summary>
    /// <remarks>
    /// 同一个客户端既能拿 client credentials 机器令牌、又能走用户授权流时，
    /// 机器 scope 会随用户令牌一起签发——那个令牌的 <c>sub</c> 是用户 GUID，
    /// 过不了资源端的机器主体判定，但客户端配置本身已经把两种信任级别混在一起了，
    /// 这是配置错误而不是运行期问题，应该在写入时就拒绝。
    /// </remarks>
    private static readonly string[] HumanGrantPermissions =
    [
        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
        OpenIddictConstants.Permissions.GrantTypes.Implicit,
        OpenIddictConstants.Permissions.GrantTypes.Password,
        OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
        OpenIddictConstants.Permissions.GrantTypes.DeviceCode
    ];

    private static readonly HashSet<string> ConsentTypes = new(StringComparer.Ordinal)
    {
        OpenIddictConstants.ConsentTypes.Explicit,
        OpenIddictConstants.ConsentTypes.External,
        OpenIddictConstants.ConsentTypes.Implicit,
        OpenIddictConstants.ConsentTypes.Systematic
    };

    public async Task<PagedResultDto<OpenApplicationOutputDto>> GetPagedListAsync(
        GetOpenApplicationPagedInputDto input,
        CancellationToken cancellationToken = default)
    {
        var intermediateItems = new List<IntermediateAppDto>();
        await foreach (var app in applicationManager.ListAsync(count: null, offset: null, cancellationToken))
        {
            intermediateItems.Add(new IntermediateAppDto
            {
                Application = app,
                ClientId = await applicationManager.GetClientIdAsync(app, cancellationToken) ?? string.Empty,
                DisplayName = await applicationManager.GetDisplayNameAsync(app, cancellationToken),
                ApplicationType = await applicationManager.GetApplicationTypeAsync(app, cancellationToken),
                ClientType = await applicationManager.GetClientTypeAsync(app, cancellationToken),
                ConsentType = await applicationManager.GetConsentTypeAsync(app, cancellationToken),
                CreationTime = OpenApplicationProfile.ReadCreationTime(
                    await applicationManager.GetPropertiesAsync(app, cancellationToken))
            });
        }

        var query = intermediateItems.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(input.Keyword))
        {
            var keyword = input.Keyword.Trim();
            query = query.Where(item =>
                item.ClientId.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (item.DisplayName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(input.ApplicationType))
        {
            query = query.Where(item => item.ApplicationType == input.ApplicationType);
        }

        if (!string.IsNullOrWhiteSpace(input.ClientType))
        {
            query = query.Where(item => item.ClientType == input.ClientType);
        }

        var filteredItems = ApplySorting(query, input.Sorting).ToList();
        var totalCount = filteredItems.Count;
        var pagedItems = filteredItems
            .Skip(input.Offset)
            .Take(input.Limit)
            .ToList();

        var outputItems = new List<OpenApplicationOutputDto>();
        foreach (var item in pagedItems)
        {
            outputItems.Add(await MapToOutputAsync(item.Application, cancellationToken));
        }

        return new PagedResultDto<OpenApplicationOutputDto>(totalCount, outputItems);
    }

    public async Task<OpenApplicationOutputDto> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await FindRequiredAsync(id, cancellationToken);
        return await MapToOutputAsync(application, cancellationToken);
    }

    public async Task<OpenApplicationOutputDto> CreateAsync(
        CreateOpenApplicationInputDto input,
        CancellationToken cancellationToken = default)
    {
        var clientId = input.ClientId.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new BadRequestException("Client ID is required.")
#if (IncludeLocalization)
                .WithCode("OpenApp:ClientIdRequired")
#endif
                ;
        }

        if (await applicationManager.FindByClientIdAsync(clientId, cancellationToken) != null)
        {
            throw new BadRequestException($"Client ID already exists: {clientId}")
#if (IncludeLocalization)
                .WithCode("OpenApp:ClientIdTaken")
                .WithData("ClientId", clientId)
#endif
                ;
        }

        ValidateApplication(input.ApplicationType, input.ClientType, input.ConsentType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements, input.Permissions);

        // Confidential 客户端：自动生成 Secret
        string? generatedSecret = null;
        if (input.ClientType == OpenIddictConstants.ClientTypes.Confidential)
        {
            generatedSecret = GenerateClientSecret();
        }

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim(),
            ApplicationType = input.ApplicationType,
            ClientType = input.ClientType,
            ConsentType = input.ConsentType,
            ClientSecret = generatedSecret
        };

        ApplyCollections(
            descriptor,
            input.RedirectUris,
            input.PostLogoutRedirectUris,
            input.Permissions,
            input.Requirements);
        descriptor.Properties[OpenApplicationProfile.CreationTimePropertyName] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);

        try
        {
            var application = await applicationManager.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation("Open application created (ClientId: {ClientId})", clientId);

            var output = await MapToOutputAsync(application, cancellationToken);

            // 创建时返回生成的 Secret（仅此一次）
            if (generatedSecret != null)
            {
                return output with { ClientSecret = generatedSecret };
            }

            return output;
        }
        catch (OpenIddict.Abstractions.OpenIddictExceptions.ValidationException ex)
        {
            logger.LogWarning(ex, "OpenIddict validation failed (ClientId: {ClientId})", clientId);
            throw new BadRequestException($"Failed to create client: {ex.Message}")
#if (IncludeLocalization)
                .WithCode("OpenApp:CreateFailed")
                .WithData("Reason", ex.Message)
#endif
                ;
        }
    }

    public async Task<OpenApplicationOutputDto> UpdateAsync(
        string id,
        UpdateOpenApplicationInputDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateApplication(input.ApplicationType, input.ClientType, input.ConsentType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements, input.Permissions);

        var application = await FindRequiredAsync(id, cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);

        descriptor.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        descriptor.ApplicationType = input.ApplicationType;
        descriptor.ClientType = input.ClientType;
        descriptor.ConsentType = input.ConsentType;
        if (input.ClientType == OpenIddictConstants.ClientTypes.Public)
        {
            descriptor.ClientSecret = null;
        }

        ApplyCollections(
            descriptor,
            input.RedirectUris,
            input.PostLogoutRedirectUris,
            input.Permissions,
            input.Requirements);

        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        logger.LogInformation("Open application updated (ID: {Id})", id);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await FindRequiredAsync(id, cancellationToken);
        await applicationManager.DeleteAsync(application, cancellationToken);
        logger.LogInformation("Open application deleted (ID: {Id})", id);
    }

    public async Task<ResetOpenApplicationSecretOutputDto> ResetSecretAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await FindRequiredAsync(id, cancellationToken);
        var clientType = await applicationManager.GetClientTypeAsync(application, cancellationToken);
        if (clientType != OpenIddictConstants.ClientTypes.Confidential)
        {
            throw new BadRequestException("Only confidential clients can reset their secret.")
#if (IncludeLocalization)
                .WithCode("OpenApp:SecretResetConfidentialOnly")
#endif
                ;
        }

        var clientSecret = GenerateClientSecret();
        await applicationManager.UpdateAsync(application, clientSecret, cancellationToken);
        logger.LogInformation("Open application secret reset (ID: {Id})", id);
        return new ResetOpenApplicationSecretOutputDto { ClientSecret = clientSecret };
    }

    private async Task<object> FindRequiredAsync(string id, CancellationToken cancellationToken)
    {
        var application = await applicationManager.FindByIdAsync(id, cancellationToken);
        if (application == null)
        {
            throw new NotFoundException($"Open application not found: {id}")
#if (IncludeLocalization)
                .WithCode("OpenApp:NotFound")
                .WithData("Id", id)
#endif
                ;
        }

        return application;
    }

    /// <remarks><c>PopulateAsync</c> 一次填满除 <c>Id</c> 以外的全部字段，<c>Id</c> 经映射上下文传入。</remarks>
    private async Task<OpenApplicationOutputDto> MapToOutputAsync(object application, CancellationToken cancellationToken)
    {
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);
        var id = await applicationManager.GetIdAsync(application, cancellationToken);

        return objectMapper.Map<OpenIddictApplicationDescriptor, OpenApplicationOutputDto>(
            descriptor,
            new Dictionary<string, object> { [OpenApplicationProfile.IdKey] = id ?? string.Empty });
    }

    private static void ValidateApplication(
        string applicationType,
        string clientType,
        string consentType,
        IReadOnlyCollection<string> redirectUris,
        IReadOnlyCollection<string> postLogoutRedirectUris,
        IReadOnlyCollection<string> requirements,
        IReadOnlyCollection<string> permissions)
    {
        if (!ApplicationTypes.Contains(applicationType))
        {
            throw new BadRequestException($"Unsupported application type: {applicationType}")
#if (IncludeLocalization)
                .WithCode("OpenApp:ApplicationTypeUnsupported")
                .WithData("ApplicationType", applicationType)
#endif
                ;
        }

        if (!ClientTypes.Contains(clientType))
        {
            throw new BadRequestException($"Unsupported client type: {clientType}")
#if (IncludeLocalization)
                .WithCode("OpenApp:ClientTypeUnsupported")
                .WithData("ClientType", clientType)
#endif
                ;
        }

        if (!ConsentTypes.Contains(consentType))
        {
            throw new BadRequestException($"Unsupported consent type: {consentType}")
#if (IncludeLocalization)
                .WithCode("OpenApp:ConsentTypeUnsupported")
                .WithData("ConsentType", consentType)
#endif
                ;
        }

        // Secret 由后端自动生成，不从前端传入，无需校验

        if ((applicationType == OpenIddictConstants.ApplicationTypes.Native || clientType == OpenIddictConstants.ClientTypes.Public) &&
            !requirements.Contains(PkceRequirement))
        {
            throw new BadRequestException("PKCE must be enabled for native/public clients.")
#if (IncludeLocalization)
                .WithCode("OpenApp:PkceRequired")
#endif
                ;
        }

        foreach (var permission in permissions.Where(x =>
                     x.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.Ordinal)))
        {
            if (RegisteredScopePermissions.Contains(permission))
                continue;

            throw new BadRequestException($"Unsupported scope permission: {permission}")
#if (IncludeLocalization)
                .WithCode("OpenApp:ScopeUnsupported")
                .WithData("Scope", permission)
#endif
                ;
        }

        ValidateMachineOnlyScopes(clientType, permissions);

        foreach (var uri in redirectUris.Concat(postLogoutRedirectUris))
        {
            ValidateUri(uri);
        }
    }

    /// <summary>
    /// 开放应用列表的可排序字段
    /// </summary>
    /// <remarks>
    /// 这一处排的是内存集合（OpenIddict 的管理器没有可组合的 <c>IQueryable</c>），
    /// 但白名单的理由与另外两处相同：字段集必须由服务端定，非法字段要 400 而不是 500。
    /// 末尾固定追加 <c>ClientId</c>：它在本服务内唯一，作为稳定次序保证翻页不重不漏。
    /// </remarks>
    private static IEnumerable<IntermediateAppDto> ApplySorting(
        IEnumerable<IntermediateAppDto> items, string? sorting)
    {
        var (field, descending) = SortingRequest.Parse(sorting, "clientId");

        var ordered = field switch
        {
            "clientId" => SortingRequest.By(items, item => item.ClientId, descending),
            "displayName" => SortingRequest.By(items, item => item.DisplayName, descending),
            "creationTime" => SortingRequest.By(items, item => item.CreationTime, descending),
            _ => throw SortingRequest.UnknownField(field)
        };

        return ordered.ThenBy(item => item.ClientId, StringComparer.Ordinal);
    }

    /// <summary>
    /// 内部控制面 scope 的四条组合约束
    /// </summary>
    /// <remarks>
    /// 内部控制面 scope 只允许 confidential 客户端，并要求 <c>client_credentials</c>
    /// 与 <c>ept:token</c> 权限；缺少任一项都无法从令牌端点取得该 scope。
    /// 同一客户端不得启用用户授权流，避免混合机器与自然人的信任边界。
    /// <c>applicationType=service</c> 仅为分类信息，不作为安全断言。
    /// </remarks>
    private static void ValidateMachineOnlyScopes(
        string clientType,
        IReadOnlyCollection<string> permissions)
    {
        var machineScopes = permissions.Where(MachineOnlyScopePermissions.Contains).ToList();
        if (machineScopes.Count == 0)
        {
            return;
        }

        var scopeList = string.Join(", ", machineScopes);

        if (clientType != OpenIddictConstants.ClientTypes.Confidential)
        {
            throw new BadRequestException(
                $"Internal control-plane scopes ({scopeList}) require a confidential client.")
#if (IncludeLocalization)
                .WithCode("OpenApp:MachineScopeRequiresConfidential")
                .WithData("Scopes", scopeList)
#endif
                ;
        }

        if (!permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials))
        {
            throw new BadRequestException(
                $"Internal control-plane scopes ({scopeList}) require the client_credentials grant type.")
#if (IncludeLocalization)
                .WithCode("OpenApp:MachineScopeRequiresClientCredentials")
                .WithData("Scopes", scopeList)
#endif
                ;
        }

        if (!permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token))
        {
            throw new BadRequestException(
                $"Internal control-plane scopes ({scopeList}) require the token endpoint permission.")
#if (IncludeLocalization)
                .WithCode("OpenApp:MachineScopeRequiresTokenEndpoint")
                .WithData("Scopes", scopeList)
#endif
                ;
        }

        var humanGrants = permissions.Where(HumanGrantPermissions.Contains).ToList();
        if (humanGrants.Count > 0)
        {
            throw new BadRequestException(
                $"Internal control-plane scopes ({scopeList}) cannot be combined with user-facing grant " +
                $"types ({string.Join(", ", humanGrants)}).")
#if (IncludeLocalization)
                .WithCode("OpenApp:MachineScopeRejectsUserGrants")
                .WithData("Scopes", scopeList)
                .WithData("Grants", string.Join(", ", humanGrants))
#endif
                ;
        }
    }

    private static void ValidateUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new BadRequestException($"Invalid URI: {value}")
#if (IncludeLocalization)
                .WithCode("OpenApp:InvalidUri")
                .WithData("Uri", value)
#endif
                ;
        }
    }

    private static void ApplyCollections(
        OpenIddictApplicationDescriptor descriptor,
        IEnumerable<string> redirectUris,
        IEnumerable<string> postLogoutRedirectUris,
        IEnumerable<string> permissions,
        IEnumerable<string> requirements)
    {
        descriptor.RedirectUris.Clear();
        foreach (var uri in redirectUris.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            descriptor.RedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var uri in postLogoutRedirectUris.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri, UriKind.Absolute));
        }

        descriptor.Permissions.Clear();
        foreach (var permission in permissions.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            descriptor.Permissions.Add(permission);
        }

        descriptor.Requirements.Clear();
        foreach (var requirement in requirements.Select(x => x.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
        {
            descriptor.Requirements.Add(requirement);
        }
    }

    private static string GenerateClientSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private class IntermediateAppDto
    {
        public required object Application { get; init; }
        public required string ClientId { get; init; }
        public string? DisplayName { get; init; }
        public string? ApplicationType { get; init; }
        public string? ClientType { get; init; }
        public string? ConsentType { get; init; }
        public DateTimeOffset CreationTime { get; init; }
    }
}
#endif
