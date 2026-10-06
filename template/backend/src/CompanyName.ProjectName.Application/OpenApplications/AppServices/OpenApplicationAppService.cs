#if (LocalIdentity)
#if (OpenIddictServer)
using CompanyName.ProjectName.Application.OpenApplications.Errors;
#endif
using Leistd.ExceptionHandling;
using System.Security.Cryptography;
using System.Text.Json;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using CompanyName.ProjectName.Application.Shared.Paging;
using CompanyName.ProjectName.Application.Auth.OAuth;
using CompanyName.ProjectName.Domain.Auth.Options;
using Leistd.Ddd.Application.AppServices;
using Leistd.Ddd.Application.Contracts.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using CompanyName.ProjectName.Application.OpenApplications.Mappings;
using Leistd.ObjectMapping.Abstractions;
using OpenIddict.Abstractions;
using Leistd.Timing;
using Leistd.Data.Paging;

namespace CompanyName.ProjectName.Application.OpenApplications.AppServices;

public class OpenApplicationAppService(
    IOpenIddictApplicationManager applicationManager,
    IOptions<OAuthOptions> oauthOptions,
    IObjectMapper objectMapper,
    IClock clock,
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
    /// 本服务能签发的 scope 与其中仅限机器的那些，都取自 scope 目录（见 <see cref="OAuthScopes"/>）。
    /// </summary>
    /// <remarks>
    /// <para>写入时按目录校验 scope 与 audience，并校验交换客户端的权限组合，
    /// 避免保存可登记但无法使用的客户端；不维护完整的官方权限词汇表。</para>
    /// <para>仅限机器的 scope（租户连接回源与迁移控制面）只能发给服务间调用的机器客户端：
    /// 授出去就没有回收窗口，创建时挡住比事后审计便宜；资源端策略另有一道。</para>
    /// </remarks>
    private IReadOnlyList<OAuthScope> ScopeCatalog => OAuthScopes.All(oauthOptions.Value);

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


    public async Task<PagedResult<OpenApplicationOutputDto>> GetPagedListAsync(
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
                CreationTime = OpenApplicationMappings.ReadCreationTime(
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

        return new PagedResult<OpenApplicationOutputDto>(totalCount, outputItems);
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
            throw new BusinessException(OpenAppErrorCodes.ClientIdRequired, "Client ID is required.")
                ;
        }

        if (await applicationManager.FindByClientIdAsync(clientId, cancellationToken) != null)
        {
            throw new BusinessException(OpenAppErrorCodes.ClientIdTaken, $"Client ID already exists: {clientId}")
                .WithData("ClientId", clientId);
        }

        ValidateApplication(input.ApplicationType, input.ClientType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements, input.Permissions);

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
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit,
            ClientSecret = generatedSecret
        };

        ApplyCollections(
            descriptor,
            input.RedirectUris,
            input.PostLogoutRedirectUris,
            input.Permissions,
            input.Requirements);
        descriptor.Properties[OpenApplicationMappings.CreationTimePropertyName] = JsonSerializer.SerializeToElement(clock.Now);
        descriptor.Settings[OpenApplicationSettings.SessionBound] = OpenApplicationSettings.Format(input.SessionBound!.Value);

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
        catch (OpenIddictExceptions.ValidationException ex)
        {
            logger.LogWarning(ex, "OpenIddict validation failed (ClientId: {ClientId})", clientId);
            throw new BusinessException(OpenAppErrorCodes.CreateFailed, "The open application could not be created.", ex);
        }
    }

    public async Task<OpenApplicationOutputDto> UpdateAsync(
        string id,
        UpdateOpenApplicationInputDto input,
        CancellationToken cancellationToken = default)
    {
        ValidateApplication(input.ApplicationType, input.ClientType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements, input.Permissions);

        var application = await FindRequiredAsync(id, cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);

        descriptor.DisplayName = string.IsNullOrWhiteSpace(input.DisplayName) ? null : input.DisplayName.Trim();
        descriptor.ApplicationType = input.ApplicationType;
        descriptor.ClientType = input.ClientType;
        descriptor.ConsentType = OpenIddictConstants.ConsentTypes.Implicit;
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
        // 只改模板自有的键，其余 Settings 原样保留
        descriptor.Settings[OpenApplicationSettings.SessionBound] = OpenApplicationSettings.Format(input.SessionBound!.Value);

        await applicationManager.UpdateAsync(application, descriptor, cancellationToken);
        logger.LogInformation("Open application updated (ID: {Id})", id);
        // 管理器已把描述符写回手里的实例，用它构造输出，不再按 Id 回查
        return await MapToOutputAsync(application, cancellationToken);
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
            throw new BusinessException(OpenAppErrorCodes.SecretResetConfidentialOnly, "Only confidential clients can reset their secret.")
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
            throw new BusinessException(OpenAppErrorCodes.NotFound, $"Open application not found: {id}")
                .WithData("Id", id);
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
            new Dictionary<string, object> { [OpenApplicationMappings.IdKey] = id ?? string.Empty });
    }

    /// <inheritdoc />
    public IReadOnlyList<OpenApplicationScopeOutputDto> GetScopes() =>
        ScopeCatalog
            .Select(scope => new OpenApplicationScopeOutputDto
            {
                Name = scope.Name,
                DisplayName = scope.DisplayName,
                Audience = !scope.MachineOnly && scope.Resources.Count == 1 ? scope.Resources[0] : null,
                MachineOnly = scope.MachineOnly
            })
            .ToList();

    private void ValidateApplication(
        string applicationType,
        string clientType,
        IReadOnlyCollection<string> redirectUris,
        IReadOnlyCollection<string> postLogoutRedirectUris,
        IReadOnlyCollection<string> requirements,
        IReadOnlyCollection<string> permissions)
    {
        if (!ApplicationTypes.Contains(applicationType))
        {
            throw new BusinessException(OpenAppErrorCodes.ApplicationTypeUnsupported, $"Unsupported application type: {applicationType}")
                .WithData("ApplicationType", applicationType);
        }

        if (!ClientTypes.Contains(clientType))
        {
            throw new BusinessException(OpenAppErrorCodes.ClientTypeUnsupported, $"Unsupported client type: {clientType}")
                .WithData("ClientType", clientType);
        }


        // Secret 由后端自动生成，不从前端传入，无需校验

        if ((applicationType == OpenIddictConstants.ApplicationTypes.Native || clientType == OpenIddictConstants.ClientTypes.Public) &&
            !requirements.Contains(PkceRequirement))
        {
            throw new BusinessException(OpenAppErrorCodes.PkceRequired, "PKCE must be enabled for native/public clients.")
                ;
        }

        foreach (var permission in permissions.Where(x =>
                     x.StartsWith(OpenIddictConstants.Permissions.Prefixes.Scope, StringComparison.Ordinal)))
        {
            var scopeName = permission[OpenIddictConstants.Permissions.Prefixes.Scope.Length..];
            if (ScopeCatalog.Any(scope => scope.Name == scopeName))
                continue;

            throw new BusinessException(OpenAppErrorCodes.ScopeUnsupported, $"Unsupported scope permission: {permission}")
                .WithData("Scope", permission);
        }

        ValidateMachineOnlyScopes(clientType, permissions);
        if (permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.TokenExchange) &&
            (clientType != OpenIddictConstants.ClientTypes.Confidential ||
             !permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token) ||
             permissions.Any(HumanGrantPermissions.Contains)))
            throw new BusinessException(OpenAppErrorCodes.ExchangeClientInvalid,
                "Token Exchange requires a confidential service client with token permission and no user-facing grants.");
        foreach (var permission in permissions.Where(permission => permission.StartsWith(OpenIddictConstants.Permissions.Prefixes.Audience, StringComparison.Ordinal)))
        {
            var audience = permission[OpenIddictConstants.Permissions.Prefixes.Audience.Length..];
            if (!ScopeCatalog.SelectMany(scope => scope.Resources).Contains(audience))
                throw new BusinessException(OpenAppErrorCodes.AudienceUnsupported, $"Unsupported audience: {audience}").WithData("Audience", audience);
        }

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
    private void ValidateMachineOnlyScopes(
        string clientType,
        IReadOnlyCollection<string> permissions)
    {
        var machineScopes = ScopeCatalog
            .Where(scope => scope.MachineOnly)
            .Select(scope => OpenIddictConstants.Permissions.Prefixes.Scope + scope.Name)
            .Where(permissions.Contains)
            .ToList();
        if (machineScopes.Count == 0)
        {
            return;
        }

        var scopeList = string.Join(", ", machineScopes);

        if (clientType != OpenIddictConstants.ClientTypes.Confidential)
        {
            throw new BusinessException(OpenAppErrorCodes.MachineScopeRequiresConfidential,
                    $"Machine-only scopes ({scopeList}) require a confidential client.")
                .WithData("Scopes", scopeList);
        }

        if (!permissions.Contains(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials))
        {
            throw new BusinessException(OpenAppErrorCodes.MachineScopeRequiresClientCredentials,
                    $"Machine-only scopes ({scopeList}) require the client_credentials grant type.")
                .WithData("Scopes", scopeList);
        }

        if (!permissions.Contains(OpenIddictConstants.Permissions.Endpoints.Token))
        {
            throw new BusinessException(OpenAppErrorCodes.MachineScopeRequiresTokenEndpoint,
                    $"Machine-only scopes ({scopeList}) require the token endpoint permission.")
                .WithData("Scopes", scopeList);
        }

        var humanGrants = permissions.Where(HumanGrantPermissions.Contains).ToList();
        if (humanGrants.Count > 0)
        {
            throw new BusinessException(OpenAppErrorCodes.MachineScopeRejectsUserGrants,
                $"Machine-only scopes ({scopeList}) cannot be combined with user-facing grant " +
                $"types ({string.Join(", ", humanGrants)}).")
                .WithData("Scopes", scopeList)
                .WithData("Grants", string.Join(", ", humanGrants));
        }
    }

    private static void ValidateUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new BusinessException(OpenAppErrorCodes.InvalidUri, $"Invalid URI: {value}")
                .WithData("Uri", value);
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
        public DateTimeOffset CreationTime { get; init; }
    }
}
#endif
