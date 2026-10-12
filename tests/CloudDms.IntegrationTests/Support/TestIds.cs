namespace CloudDms.IntegrationTests.Support;

public static class TestIds
{
    public static readonly Guid TenantA = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid DepartmentA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid DepartmentB = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid Owner = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Reader = Guid.Parse("30000000-0000-0000-0000-000000000002");
    public static readonly Guid Admin = Guid.Parse("30000000-0000-0000-0000-000000000003");
}
