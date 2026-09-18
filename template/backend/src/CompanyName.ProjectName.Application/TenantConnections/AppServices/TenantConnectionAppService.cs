#if (LocalIdentity)
using System.Text.RegularExpressions;
using CompanyName.ProjectName.Application.TenantConnections.Dtos;
using CompanyName.ProjectName.Domain.Tenants.Connections;
using Leistd.Ddd.Application.AppService;
using Leistd.ExceptionHandling;
using Leistd.MultiTenancy.ConnectionStrings;

namespace CompanyName.ProjectName.Application.TenantConnections.AppServices;

/// <summary>
/// 租户连接的管理面与内部下发。
/// </summary>
/// <remarks>
/// <para>列表走 <see cref="ITenantConnectionDirectory"/>（实现读控制库），而不是框架的
/// <see cref="ITenantConnectionConfigurationStore"/>：后者刻意是"按名字问、按名字答，一次只出一条"，
/// 因为远端形态下把整租户的连接都发给某一个资源服务等于把别人的库口令也发过去了。</para>
/// <para>管理面<b>只回名字与版本</b>；明文连接串只经已认证的内部接口，按名字下发给需要连库的服务。</para>
/// </remarks>
public sealed class TenantConnectionAppService(
    ITenantConnectionDirectory directory,
    ITenantConnectionConfigurationStore store,
    ITenantConnectionConfigurationManager manager) : BaseAppService, ITenantConnectionAppService
{
    public async Task<IReadOnlyList<TenantConnectionOutputDto>> GetListAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entries = await directory.ListAsync(tenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant was not found.");

        // 空列表是合法结果，表示该租户不单独分库——不要把它当成 404
        return
        [
            .. entries.Select(entry => new TenantConnectionOutputDto
            {
                TenantId = tenantId,
                Name = entry.Name,
                Version = entry.Version
            })
        ];
    }

    public async Task<TenantRuntimeConnectionOutputDto> GetRuntimeAsync(
        Guid tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureValidName(name);

        var lookup = await store.FindAsync(tenantId, name, cancellationToken)
            ?? throw new NotFoundException("Tenant was not found.");

        return new TenantRuntimeConnectionOutputDto
        {
            TenantId = lookup.TenantId,
            HasAnyConnection = lookup.HasAnyConnection,
            Connection = lookup.Connection is { } connection
                ? new TenantConnectionDetailOutputDto
                {
                    Name = connection.Name,
                    ConnectionString = connection.ConnectionString,
                    Version = connection.Version
                }
                : null
        };
    }

    public async Task<IReadOnlyList<TenantMigrationConnectionOutputDto>> GetMigrationListAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        EnsureValidName(name);

        var connections = await store.GetListAsync(name, cancellationToken);
        return
        [
            .. connections.Select(x => new TenantMigrationConnectionOutputDto
            {
                TenantId = x.TenantId,
                Name = x.Name,
                ConnectionString = x.ConnectionString
            })
        ];
    }

    public async Task<TenantConnectionOutputDto> SetAsync(
        Guid tenantId,
        string name,
        UpsertTenantConnectionInputDto input,
        CancellationToken cancellationToken = default)
    {
        EnsureValidName(name);
        ConnectionStringGuard.EnsureParsable(input.ConnectionString);

        var configuration = await manager.SetAsync(
            tenantId,
            name,
            input.ConnectionString,
            input.ExpectedVersion,
            cancellationToken);

        return new TenantConnectionOutputDto
        {
            TenantId = configuration.TenantId,
            Name = configuration.Name,
            Version = configuration.Version
        };
    }

    public Task RemoveAsync(
        Guid tenantId,
        string name,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        EnsureValidName(name);

        return manager.RemoveAsync(tenantId, name, expectedVersion, cancellationToken);
    }

    // 连接名来自 URL 路径段，是外部输入。框架的 TenantConnectionNames.Normalize 只守编程契约，
    // 对非法值抛 ArgumentException——那会以 500 出去，而这是调用方自己就能改对的输入错误，应当是 400。
    // 判据复用框架的 NamePattern，不另抄一份正则，免得两处各自漂移。
    // 先归一化再匹配，与框架同序：模式里只有小写，拿原值直接匹配会把合法的 "CRM" 误拒。
    private static void EnsureValidName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && Regex.IsMatch(name.Trim().ToLowerInvariant(), TenantConnectionConfiguration.NamePattern))
        {
            return;
        }

        throw new BadRequestException(
            $"Connection name '{name}' is invalid. After lowercasing it must match " +
            $"{TenantConnectionConfiguration.NamePattern}.")
#if (IncludeLocalization)
            .WithCode("TenantConnection:NameInvalid")
            .WithData("Name", name)
            .WithData("Pattern", TenantConnectionConfiguration.NamePattern)
#endif
            ;
    }
}
#endif
