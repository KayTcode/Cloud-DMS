namespace CleanArchCqrs.Application.Users.DTOs;

public sealed record LoginResponseDto(
    string AccessToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    Guid? TenantId,
    List<string> Permissions
);

