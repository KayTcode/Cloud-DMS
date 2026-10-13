using CleanArchCqrs.Domain.Entities;
namespace CloudDms.UnitTests.Files;
public sealed class SearchReadContractTests
{
    [Fact]
    public void DocumentAndAuthorizationVersionsAreIndependent()
    {
        var file = new FileEntry();
        var property = typeof(FileEntry).GetProperty("DocumentVersion");
        Assert.NotNull(property);
        Assert.Equal(1L, property.GetValue(file));
        file.AuthorizationVersion = 9;
        Assert.Equal(1L, property.GetValue(file));
        Assert.Null(typeof(FileEntry).GetProperty("ActiveSearchGenerationId")!.GetValue(file));
    }
    [Theory]
    [InlineData("FileContentVersion")]
    [InlineData("FileSearchGeneration")]
    [InlineData("FileTextChunk")]
    [InlineData("FilePreviewArtifact")]
    [InlineData("DocumentOutboxMessage")]
    [InlineData("FileUploadSession")]
    [InlineData("Team")]
    [InlineData("TeamMember")]
    [InlineData("ResourceGrant")]
    public void NewDocumentAndAclEntitiesCarryTenant(string name)
    {
        var type = typeof(FileEntry).Assembly.GetType($"CleanArchCqrs.Domain.Entities.{name}");
        Assert.NotNull(type);
        Assert.Equal(typeof(Guid), type.GetProperty("TenantId")!.PropertyType);
        Assert.NotEqual(typeof(OutboxMessage), type);
    }
}
