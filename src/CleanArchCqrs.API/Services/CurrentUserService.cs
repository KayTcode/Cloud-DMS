using System.Security.Claims;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace CleanArchCqrs.API.Services;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var idClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                          ?? User?.FindFirst("sub")?.Value 
                          ?? User?.FindFirst("id")?.Value
                          ?? User?.FindFirst("userId")?.Value
                          ?? User?.FindFirst("nameid")?.Value
                          ?? User?.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

            return Guid.TryParse(idClaim, out var guid) ? guid : null;
        }
    }

    public Guid? TenantId
    {
        get
        {
            var tenantClaim = User?.FindFirst("tenantId")?.Value 
                              ?? User?.FindFirst("TenantId")?.Value;

            return Guid.TryParse(tenantClaim, out var guid) ? guid : null;
        }
    }

    public string? Email => User?.FindFirst(ClaimTypes.Email)?.Value ?? User?.FindFirst("email")?.Value;

    public string? Role => User?.FindFirst(ClaimTypes.Role)?.Value ?? User?.FindFirst("role")?.Value;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;
}
