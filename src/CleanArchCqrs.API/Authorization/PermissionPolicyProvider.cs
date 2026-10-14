using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Authorization
{
    public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
        {
        }
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            var existing = await base.GetPolicyAsync(policyName);
            if (existing != null) return existing;

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                    ctx.User.IsInRole("SystemAdmin") ||
                    ctx.User.HasClaim(System.Security.Claims.ClaimTypes.Role, "SystemAdmin") ||
                    ctx.User.HasClaim("role", "SystemAdmin") ||
                    ctx.User.HasClaim("permission", policyName))
                .Build();
        }
    }
}
