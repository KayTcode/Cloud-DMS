using CleanArchCqrs.Application.Common.Authorization;

namespace CloudDms.UnitTests.Authorization;

public sealed class PolicyContractTests
{
    [Fact]
    public void ContentAndMetadataAreDifferentActions()
    {
        Assert.NotEqual(PermissionNames.FileRead, PermissionNames.FileMetadataRead);
        Assert.NotEqual(AuthorizationDecision.Allow, AuthorizationDecision.Unavailable);
    }
}
