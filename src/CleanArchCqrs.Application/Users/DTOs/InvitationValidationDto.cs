namespace CleanArchCqrs.Application.Users.DTOs;

public record InvitationValidationDto(
    bool IsValid,
    string Message,
    string? Email = null,
    string? FullName = null,
    string? TenantName = null,
    string? Role = null,
    string? DepartmentName = null,
    string? TokenType = null
);
