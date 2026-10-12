using CleanArchCqrs.Domain.Entities;

namespace CloudDms.UnitTests.Authorization;

public sealed class AuthorizationPersistenceModelTests
{
    [Fact]
    public void FileOwnershipRemainsAttachedToOriginalDepartmentWhenOwnerMoves()
    {
        var originalDepartmentId = Guid.NewGuid();
        var user = new User(null, originalDepartmentId, "person@example.test", "hash", "Test", "User", null);
        var file = new FileEntry
        {
            OwnerId = user.Id,
            OwningDepartmentId = originalDepartmentId
        };

        user.ChangeDepartment(Guid.NewGuid());

        Assert.Equal(originalDepartmentId, file.OwningDepartmentId);
    }

    [Fact]
    public void DepartmentQuotaCannotBeNegative()
    {
        var department = new Department();

        Assert.Throws<ArgumentOutOfRangeException>(() => department.QuotaBytes = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => department.UsedBytes = -1);
    }

    [Fact]
    public void UserQuotaCannotBeNegative()
    {
        var quota = new UserStorageQuota();

        Assert.Throws<ArgumentOutOfRangeException>(() => quota.QuotaBytes = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => quota.UsedBytes = -1);
    }
}
