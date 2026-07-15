#if (IncludeIdentity)
using System.Linq.Dynamic.Core;
using System.Security.Cryptography;
using System.Text.Json;
using CompanyName.ProjectName.Application.OpenApplications.Dtos;
using Leistd.Ddd.Application.AppService;
using Leistd.Ddd.Application.Contracts.Dtos;
using Leistd.Exception.Core;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;

namespace CompanyName.ProjectName.Application.OpenApplications.AppServices;

public class OpenApplicationAppService(
    IOpenIddictApplicationManager applicationManager,
    ILogger<OpenApplicationAppService> logger) : BaseAppService, IOpenApplicationAppService
{
    private const string CreationTimePropertyName = "creationTime";
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
                CreationTime = GetCreationTime(await applicationManager.GetPropertiesAsync(app, cancellationToken))
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

        var sorting = input.Sorting ?? "clientId asc";
        var filteredItems = query.AsQueryable().OrderBy(sorting).ToList();
        var totalCount = filteredItems.Count;
        var pagedItems = filteredItems
            .Skip(Math.Max(input.Offset, 0))
            .Take(Math.Max(input.Limit, 0))
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
                .WithLocalization("OpenApp:ClientIdRequired")
#endif
                ;
        }

        if (await applicationManager.FindByClientIdAsync(clientId, cancellationToken) != null)
        {
            throw new BadRequestException($"Client ID already exists: {clientId}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:ClientIdTaken")
                .WithData("ClientId", clientId)
#endif
                ;
        }

        ValidateApplication(input.ApplicationType, input.ClientType, input.ConsentType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements);

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
        descriptor.Properties[CreationTimePropertyName] = JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow);

        try
        {
            var application = await applicationManager.CreateAsync(descriptor, cancellationToken);
            logger.LogInformation("创建开放应用成功 (ClientId: {ClientId})", clientId);

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
            logger.LogWarning(ex, "OpenIddict 校验失败 (ClientId: {ClientId})", clientId);
            throw new BadRequestException($"Failed to create client: {ex.Message}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:CreateFailed")
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
        ValidateApplication(input.ApplicationType, input.ClientType, input.ConsentType, input.RedirectUris, input.PostLogoutRedirectUris, input.Requirements);

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
        logger.LogInformation("更新开放应用成功 (ID: {Id})", id);
        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await FindRequiredAsync(id, cancellationToken);
        await applicationManager.DeleteAsync(application, cancellationToken);
        logger.LogInformation("删除开放应用成功 (ID: {Id})", id);
    }

    public async Task<ResetOpenApplicationSecretOutputDto> ResetSecretAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await FindRequiredAsync(id, cancellationToken);
        var clientType = await applicationManager.GetClientTypeAsync(application, cancellationToken);
        if (clientType != OpenIddictConstants.ClientTypes.Confidential)
        {
            throw new BadRequestException("Only confidential clients can reset their secret.")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:SecretResetConfidentialOnly")
#endif
                ;
        }

        var clientSecret = GenerateClientSecret();
        await applicationManager.UpdateAsync(application, clientSecret, cancellationToken);
        logger.LogInformation("重置开放应用密钥成功 (ID: {Id})", id);
        return new ResetOpenApplicationSecretOutputDto { ClientSecret = clientSecret };
    }

    private async Task<object> FindRequiredAsync(string id, CancellationToken cancellationToken)
    {
        var application = await applicationManager.FindByIdAsync(id, cancellationToken);
        if (application == null)
        {
            throw new NotFoundException($"Open application not found: {id}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:NotFound")
                .WithData("Id", id)
#endif
                ;
        }

        return application;
    }

    private async Task<OpenApplicationOutputDto> MapToOutputAsync(object application, CancellationToken cancellationToken)
    {
        var id = await applicationManager.GetIdAsync(application, cancellationToken);
        var clientId = await applicationManager.GetClientIdAsync(application, cancellationToken);
        var applicationType = await applicationManager.GetApplicationTypeAsync(application, cancellationToken);
        var clientType = await applicationManager.GetClientTypeAsync(application, cancellationToken);
        var consentType = await applicationManager.GetConsentTypeAsync(application, cancellationToken);
        var redirectUris = await applicationManager.GetRedirectUrisAsync(application, cancellationToken);
        var postLogoutRedirectUris = await applicationManager.GetPostLogoutRedirectUrisAsync(application, cancellationToken);
        var permissions = await applicationManager.GetPermissionsAsync(application, cancellationToken);
        var requirements = await applicationManager.GetRequirementsAsync(application, cancellationToken);
        var settings = await applicationManager.GetSettingsAsync(application, cancellationToken);
        var properties = await applicationManager.GetPropertiesAsync(application, cancellationToken);
        var descriptor = new OpenIddictApplicationDescriptor();
        await applicationManager.PopulateAsync(descriptor, application, cancellationToken);

        return new OpenApplicationOutputDto
        {
            Id = id ?? string.Empty,
            ClientId = clientId ?? string.Empty,
            DisplayName = await applicationManager.GetDisplayNameAsync(application, cancellationToken),
            ApplicationType = applicationType ?? OpenIddictConstants.ApplicationTypes.Web,
            ClientType = clientType ?? OpenIddictConstants.ClientTypes.Public,
            ConsentType = consentType ?? OpenIddictConstants.ConsentTypes.Explicit,
            RedirectUris = redirectUris.Select(uri => uri.ToString()).ToList(),
            PostLogoutRedirectUris = postLogoutRedirectUris.Select(uri => uri.ToString()).ToList(),
            Permissions = permissions.ToList(),
            Requirements = requirements.ToList(),
            Settings = settings.ToDictionary(pair => pair.Key, pair => pair.Value),
            Properties = properties.ToDictionary(pair => pair.Key, pair => pair.Value),
            HasClientSecret = !string.IsNullOrEmpty(descriptor.ClientSecret),
            CreationTime = GetCreationTime(properties)
        };
    }

    private static void ValidateApplication(
        string applicationType,
        string clientType,
        string consentType,
        IReadOnlyCollection<string> redirectUris,
        IReadOnlyCollection<string> postLogoutRedirectUris,
        IReadOnlyCollection<string> requirements)
    {
        if (!ApplicationTypes.Contains(applicationType))
        {
            throw new BadRequestException($"Unsupported application type: {applicationType}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:ApplicationTypeUnsupported")
                .WithData("ApplicationType", applicationType)
#endif
                ;
        }

        if (!ClientTypes.Contains(clientType))
        {
            throw new BadRequestException($"Unsupported client type: {clientType}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:ClientTypeUnsupported")
                .WithData("ClientType", clientType)
#endif
                ;
        }

        if (!ConsentTypes.Contains(consentType))
        {
            throw new BadRequestException($"Unsupported consent type: {consentType}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:ConsentTypeUnsupported")
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
                .WithLocalization("OpenApp:PkceRequired")
#endif
                ;
        }

        foreach (var uri in redirectUris.Concat(postLogoutRedirectUris))
        {
            ValidateUri(uri);
        }
    }

    private static void ValidateUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new BadRequestException($"Invalid URI: {value}")
#if (IncludeLocalization)
                .WithLocalization("OpenApp:InvalidUri")
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

    private static DateTimeOffset GetCreationTime(IReadOnlyDictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue(CreationTimePropertyName, out var value))
        {
            return DateTimeOffset.MinValue;
        }

        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var dateTime))
        {
            return dateTime;
        }

        if (value.TryGetDateTimeOffset(out dateTime))
        {
            return dateTime;
        }

        return DateTimeOffset.MinValue;
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
