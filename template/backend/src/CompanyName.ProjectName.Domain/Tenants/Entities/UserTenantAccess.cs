#if (TenantWorkspace && LocalIdentity)
using Leistd.Ddd.Domain.Entities;

namespace CompanyName.ProjectName.Domain.Tenants.Entities;

/// <summary>宿主全局的租户访问索引，不参与租户过滤。</summary>
public sealed class UserTenantAccess : Entity<Guid>, IAggregateRoot<Guid>
{
    public Guid HomeUserId { get; private set; }
    public Guid TenantId { get; private set; }
    public DateTime CreationTime { get; private set; }

    private UserTenantAccess() { }

    public UserTenantAccess(Guid homeUserId, Guid tenantId, DateTime creationTime)
    {
        Id = Guid.CreateVersion7();
        HomeUserId = homeUserId;
        TenantId = tenantId;
        CreationTime = creationTime;
    }
}
#endif
