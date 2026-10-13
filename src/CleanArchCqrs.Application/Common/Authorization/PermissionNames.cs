namespace CleanArchCqrs.Application.Common.Authorization;

public static class PermissionNames
{
    public const string FileRestore = "file.restore";
    public const string FilePurge = "file.purge";
    public const string FileRead = "file.read";
    public const string FileUpdate = "file.update";
    public const string FileDelete = "file.delete";
    public const string FileShare = "file.share";
    public const string FileMetadataRead = "file.metadata.read";
    public const string BinPurge = "bin.purge";
    public const string UserManage = "user.manage";
    public const string DepartmentManage = "department.manage";
    public const string DepartmentQuotaAllocate = "quota.department.allocate";
    public const string UserQuotaAllocate = "quota.user.allocate";
    public const string AuditRead = "audit.read";
    public const string SystemManage = "system.manage";
}
