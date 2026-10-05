using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Users.DTOs;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Application.Users.Queries.ValidateInvitation;

public record ValidateInvitationQuery(string Token, string Email) : IRequest<InvitationValidationDto>;

public class ValidateInvitationQueryHandler : IRequestHandler<ValidateInvitationQuery, InvitationValidationDto>
{
    private readonly IApplicationDbContext _context;

    public ValidateInvitationQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<InvitationValidationDto> Handle(ValidateInvitationQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.Email))
        {
            return new InvitationValidationDto(false, "Mã kích hoạt hoặc email không được để trống.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var tokenRecord = await _context.EmailVerificationTokens
            .Include(t => t.User)
                .ThenInclude(u => u.Tenant)
            .FirstOrDefaultAsync(t => t.Token == request.Token.Trim() 
                                   && t.User.Email.ToLower() == normalizedEmail, cancellationToken);

        if (tokenRecord == null)
        {
            return new InvitationValidationDto(false, "Mã kích hoạt không tồn tại hoặc email không trùng khớp.");
        }

        if (tokenRecord.IsUsed)
        {
            return new InvitationValidationDto(false, "Mã kích hoạt này đã được sử dụng trước đó. Bạn có thể tiến hành đăng nhập.");
        }

        if (tokenRecord.ExpiresAt < DateTime.UtcNow)
        {
            return new InvitationValidationDto(false, "Mã kích hoạt đã hết hạn (quá thời hạn 48 giờ). Vui lòng liên hệ Quản trị viên để được cấp lại.");
        }

        return new InvitationValidationDto(
            IsValid: true,
            Message: "Mã kích hoạt hợp lệ.",
            Email: tokenRecord.User.Email,
            FullName: tokenRecord.User.FullName,
            TenantName: tokenRecord.User.Tenant?.Name ?? "Tổ chức");
    }
}
