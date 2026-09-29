using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Authorization
{
    public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
    {
        public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
        {
        }
        public override Task<AuthorizationPolicy?> GetPolicyAsync(
        string policyName)
        {
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("permission", policyName)
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
    }
}
