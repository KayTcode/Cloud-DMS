using CleanArchCqrs.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace CleanArchCqrs.Infrastructure.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendTenantAdminInvitationAsync(
        string toEmail,
        string recipientName,
        string tenantName,
        string token,
        CancellationToken cancellationToken = default)
    {
        var frontendUrl = _config["EmailSettings:FrontendBaseUrl"] ?? "http://localhost:5173";

        // Đóng gói đường dẫn
        var activationLink = $"{frontendUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(toEmail)}";

        // In ra log 
        _logger.LogInformation("===============================================================================");
        _logger.LogInformation("[TENANT ADMIN INVITATION] Recipient: {Email}, Tenant: {Tenant}", toEmail, tenantName);
        _logger.LogInformation("[ACTIVATION LINK]: {Link}", activationLink);
        _logger.LogInformation("===============================================================================");

        // Dao diện HTML gửi thư 
        var subject = $"[Cloud-DMS] Lời mời làm Quản trị viên (Tenant Admin) cho tổ chức {tenantName}";
        var bodyHtml = $@"
        <div style='font-family: Arial, -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, sans-serif; max-width: 600px; margin: 0 auto; padding: 28px; border: 1px solid #e2e8f0; border-radius: 12px; background-color: #ffffff;'>
            <div style='text-align: center; margin-bottom: 24px;'>
                <div style='display: inline-block; background-color: #eff6ff; padding: 12px; border-radius: 12px; margin-bottom: 12px;'>
                    <span style='font-size: 28px;'>🏢</span>
                </div>
                <h1 style='color: #1e40af; margin: 0; font-size: 24px; font-weight: 700;'>Cloud-DMS Enterprise</h1>
                <p style='color: #64748b; font-size: 14px; margin-top: 4px;'>Hệ thống Quản lý Tài liệu Đa Nền tảng Đám mây</p>
            </div>
            
            <hr style='border: none; border-top: 1px solid #f1f5f9; margin: 20px 0;' />
            
            <p style='font-size: 16px; color: #1e293b; line-height: 1.5;'>
                Kính gửi <strong>{recipientName}</strong>,
            </p>
            <p style='font-size: 14px; color: #475569; line-height: 1.6;'>
                Bạn vừa được Quản trị viên Hệ thống (System Admin) chỉ định làm <strong>Quản trị viên (Tenant Admin)</strong> cho tổ chức:
            </p>
            
            <div style='background-color: #f8fafc; border-left: 4px solid #3b82f6; padding: 14px 18px; border-radius: 4px; margin: 18px 0;'>
                <p style='margin: 0; font-size: 15px; font-weight: bold; color: #1e3a8a;'>{tenantName}</p>
                <p style='margin: 4px 0 0 0; font-size: 13px; color: #64748b;'>Vai trò: Quản trị viên Tổ chức (Tenant Administrator)</p>
            </div>

            <p style='font-size: 14px; color: #475569; line-height: 1.6;'>
                Để hoàn tất kích hoạt tài khoản và tự thiết lập mật khẩu đăng nhập, vui lòng nhấn vào nút xác nhận bên dưới:
            </p>
            
            <div style='text-align: center; margin: 32px 0;'>
                <a href='{activationLink}' style='background-color: #2563eb; color: #ffffff; padding: 14px 32px; text-decoration: none; border-radius: 8px; font-weight: 600; font-size: 15px; display: inline-block; box-shadow: 0 4px 6px -1px rgba(37, 99, 235, 0.2);'>
                    👉 Kích hoạt Tài khoản & Thiết lập Mật khẩu
                </a>
            </div>

            <p style='font-size: 12px; color: #94a3b8; line-height: 1.5;'>
                * Lưu ý: Liên kết này có thời hạn trong <strong>48 giờ</strong>. Nếu bạn không bấm được vào nút trên, hãy sao chép đường dẫn sau vào trình duyệt:<br/>
                <a href='{activationLink}' style='color: #2563eb; word-break: break-all;'>{activationLink}</a>
            </p>

            <hr style='border: none; border-top: 1px solid #f1f5f9; margin: 24px 0 16px 0;' />
            
            <p style='font-size: 11px; color: #94a3b8; text-align: center; margin: 0;'>
                Email này được gửi tự động từ hệ thống Cloud-DMS. Vui lòng không trả lời trực tiếp email này.<br/>
                © 2026 Cloud-DMS Team. All rights reserved.
            </p>
        </div>";

        await SendEmailInternalAsync(toEmail, recipientName, subject, bodyHtml, cancellationToken);
    }

    public async Task SendUserActivationEmailAsync(
        string toEmail,
        string recipientName,
        string token,
        CancellationToken cancellationToken = default)
    {
        var frontendUrl = _config["EmailSettings:FrontendBaseUrl"] ?? "http://localhost:5173";
        var activationLink = $"{frontendUrl.TrimEnd('/')}/accept-invitation?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(toEmail)}";

        var subject = "[Cloud-DMS] Kích hoạt tài khoản người dùng của bạn";
        var bodyHtml = $@"
        <div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto; padding: 24px; border: 1px solid #e2e8f0; border-radius: 8px;'>
            <h2>Xin chào {recipientName},</h2>
            <p>Tài khoản của bạn trên hệ thống Cloud-DMS đã được tạo thành công.</p>
            <p>Vui lòng nhấn vào liên kết bên dưới để kích hoạt tài khoản và tạo mật khẩu:</p>
            <p><a href='{activationLink}' style='background-color: #2563eb; color: #fff; padding: 10px 20px; text-decoration: none; border-radius: 6px; display: inline-block;'>Kích hoạt tài khoản</a></p>
            <p style='font-size: 12px; color: #64748b;'>Liên kết có hiệu lực trong 48 giờ.</p>
        </div>";

        await SendEmailInternalAsync(toEmail, recipientName, subject, bodyHtml, cancellationToken);
    }

    private async Task SendEmailInternalAsync(
        string toEmail,
        string recipientName,
        string subject,
        string bodyHtml,
        CancellationToken cancellationToken)
    {

        // Đọc các thông số cấu hình 
        var smtpHost = _config["EmailSettings:SmtpHost"];
        var senderEmail = _config["EmailSettings:SenderEmail"];
        var senderPassword = _config["EmailSettings:Password"];
        var senderName = _config["EmailSettings:SenderName"] ?? "Cloud-DMS System";
        var smtpPort = int.TryParse(_config["EmailSettings:SmtpPort"], out var port) ? port : 587;

        // Fallback: If not configured, don't crash, just log and return
        if (string.IsNullOrWhiteSpace(smtpHost) || string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPassword) || senderPassword.Contains("your-app-password"))
        {
            _logger.LogWarning("EmailSettings not fully configured in appsettings.json. Email simulation mode: Message recorded in logs without SMTP delivery.");
            return;
        }

        // Khởi tạo đối tượng Email & Thực hiện kết nối MailKit
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(senderName, senderEmail));
            message.To.Add(new MailboxAddress(recipientName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = bodyHtml };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(senderEmail, senderPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            _logger.LogInformation("Email successfully sent via SMTP to {ToEmail}", toEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deliver email via SMTP to {ToEmail}. (Activation link remains available in logs)", toEmail);
            // We do not rethrow to prevent breaking user creation flow if SMTP credentials are being tested or offline
        }
    }
}
