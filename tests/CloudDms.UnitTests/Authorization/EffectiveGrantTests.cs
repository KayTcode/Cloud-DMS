using CleanArchCqrs.Application.Common.Authorization;
using CleanArchCqrs.Domain.Enums;
namespace CloudDms.UnitTests.Authorization;
public class EffectiveGrantTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T00:00:00Z");
    private static AccessEvaluationInput Input(params ShareGrant[] grants)
    {
        var id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        return new(new(id, id, null, true, 1, new HashSet<string>(), new HashSet<string> { "file.read", "file.update", "file.share", "file.delete" }),
            new(id, id, true, Now.AddHours(1), 1, true), new(id, id, null, Guid.NewGuid(), FileLifecycle.Active, 1), grants,
            new[] { new ScopeAccessSnapshot("global", 1, false, true) }, true, Now, "g", new("file", id));
    }
    [Fact]
    public void ExpiredEditorDoesNotMaskLaterViewer()
    {
        var input = Input(new ShareGrant(Guid.Parse("00000000-0000-0000-0000-000000000001"), true, true, false, Now),
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), true, false, false, Now.AddHours(1)));
        var evaluator = new AccessPolicyEvaluator();
        Assert.Equal(AuthorizationDecision.Allow, evaluator.Evaluate(input, "file.read").Decision);
        Assert.Equal(AuthorizationDecision.Deny, evaluator.Evaluate(input, "file.update").Decision);
    }
    [Fact]
    public void GrantedOwnerCanManageSharing()
    {
        var input = Input(new ShareGrant(Guid.Parse("00000000-0000-0000-0000-000000000001"), true, true, true, null, CanShare: true));
        Assert.Equal(AuthorizationDecision.Allow, new AccessPolicyEvaluator().Evaluate(input, "file.share").Decision);
    }
    [Theory]
    [InlineData("file.restore")]
    [InlineData("file.purge")]
    public void BinnedOwnerCanPerformLifecycleMutationWithoutContentRead(string action)
    {
        var input = Input(); input = input with { Resource = input.Resource! with { OwnerId = input.User!.UserId, Lifecycle = FileLifecycle.Binned } };
        Assert.Equal(AuthorizationDecision.Allow, new AccessPolicyEvaluator().Evaluate(input, action).Decision);
        Assert.Equal(AuthorizationDecision.Deny, new AccessPolicyEvaluator().Evaluate(input, "file.read").Decision);
    }
}
