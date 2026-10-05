namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IEmailService
{
    Task SendTenantAdminInvitationAsync(
        string toEmail,
        string recipientName,
        string tenantName,
        string token,
        CancellationToken cancellationToken = default);

    Task SendUserActivationEmailAsync(
        string toEmail,
        string recipientName,
        string token,
        CancellationToken cancellationToken = default);
}
