namespace CleanArchCqrs.Application.Features.DepartmentAdmin.DTOs;

public class DepartmentSetupInfoDto
{
    public long AssignedQuotaBytes { get; set; }
    public double AssignedQuotaGB { get; set; }
    public string AdminName { get; set; } = string.Empty;
    public string? TenantName { get; set; }
    public bool HasDepartment { get; set; }
    public Guid? DepartmentId { get; set; }
}
