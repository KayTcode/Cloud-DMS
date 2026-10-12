using CleanArchCqrs.Domain.Enums;

namespace CloudDms.UnitTests.Architecture;

public sealed class LayerDependencyTests
{
    [Fact]
    public void DomainDoesNotReferenceOuterLayers()
    {
        var references = typeof(FileLifecycle).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is "CleanArchCqrs.Application"
                or "CleanArchCqrs.Infrastructure"
                or "CleanArchCqrs.API");
    }
}
