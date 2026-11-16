#if (TenantWorkspace && LocalIdentity)
using System.ComponentModel.DataAnnotations;

namespace CompanyName.ProjectName.Application.Tenants.Dtos;

public sealed record GrantTenantStaffInputDto
{
    [Required(ErrorMessage = "{0} is required.")]
    [Display(Name = "Platform staff")]
    public Guid? HomeUserId { get; init; }

    [Required(ErrorMessage = "{0} is required.")]
    [MinLength(1, ErrorMessage = "{0} must contain at least {1} item(s).")]
    [Display(Name = "Roles")]
    public List<Guid> RoleIds { get; init; } = null!;
}
#endif
