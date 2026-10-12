using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CleanArchCqrs.Application.Features.SystemAdmin.Commands.ExchangeOneDriveCode;

public record ExchangeOneDriveCodeCommand(
    string ClientId,
    string ClientSecret,
    string Code,
    string RedirectUri
) : IRequest<ExchangeOneDriveCodeResult>;

public record ExchangeOneDriveCodeResult(
    bool Success,
    string Message,
    long TotalCapacityBytes = 0,
    long UsedCapacityBytes = 0,
    long AvailableCapacityBytes = 0,
    long LatencyMs = 0
);

public class ExchangeOneDriveCodeCommandHandler : IRequestHandler<ExchangeOneDriveCodeCommand, ExchangeOneDriveCodeResult>
{
    private readonly IStorageServiceFactory _storageServiceFactory;
    private readonly IApplicationDbContext _context;
    private readonly IAesEncryptionService _aesEncryptionService;
    private static readonly HttpClient _httpClient = new();

    public ExchangeOneDriveCodeCommandHandler(
        IStorageServiceFactory storageServiceFactory,
        IApplicationDbContext context,
        IAesEncryptionService aesEncryptionService)
    {
        _storageServiceFactory = storageServiceFactory;
        _context = context;
        _aesEncryptionService = aesEncryptionService;
    }

    public async Task<ExchangeOneDriveCodeResult> Handle(ExchangeOneDriveCodeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1. Gửi Authorization Code lên Microsoft Identity Platform để đổi Access Token & Refresh Token
            var tokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
            var bodyParams = new Dictionary<string, string>
            {
                { "client_id", request.ClientId.Trim() },
                { "client_secret", request.ClientSecret.Trim() },
                { "grant_type", "authorization_code" },
                { "code", request.Code.Trim() },
                { "redirect_uri", request.RedirectUri.Trim() },
                { "scope", "offline_access Files.ReadWrite User.Read" }
            };

            var formContent = new FormUrlEncodedContent(bodyParams);
            using var response = await _httpClient.PostAsync(tokenEndpoint, formContent, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new ExchangeOneDriveCodeResult(
                    Success: false,
                    Message: $"Lỗi xác thực mã với Microsoft ({response.StatusCode}): {content}"
                );
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

            if (string.IsNullOrEmpty(refreshToken))
            {
                return new ExchangeOneDriveCodeResult(
                    Success: false,
                    Message: "Microsoft không trả về Refresh Token. Vui lòng đảm bảo đã cấp quyền 'offline_access'."
                );
            }

            // 2. Tạo config JSON chuẩn hoá
            var configObject = new
            {
                client_id = request.ClientId.Trim(),
                client_secret = request.ClientSecret.Trim(),
                refresh_token = refreshToken,
                tenant_id = "common"
            };
            var configJson = JsonSerializer.Serialize(configObject, new JsonSerializerOptions { WriteIndented = true });

            // 3. Test kết nối thực tế và lấy Quota
            var storageService = _storageServiceFactory.CreateServiceFromRawConfig(StorageProviderType.OneDrive, configJson);
            var quota = await storageService.GetProviderQuotaAsync(cancellationToken);
            sw.Stop();

            // 4. Lưu an toàn trực tiếp vào CSDL với mã hóa AES (tuyệt đối không gửi secret/token về trình duyệt)
            var encryptedConfig = _aesEncryptionService.Encrypt(configJson);
            var existingProvider = await _context.StorageProviders
                .FirstOrDefaultAsync(p => p.Type == StorageProviderType.OneDrive && p.Name == "Microsoft OneDrive", cancellationToken);

            if (existingProvider != null)
            {
                existingProvider.ConfigEncrypted = encryptedConfig;
                existingProvider.TotalCapacityBytes = quota.TotalCapacityBytes;
                existingProvider.UsedCapacityBytes = quota.UsedCapacityBytes;
                existingProvider.IsActive = true;
                existingProvider.LastSyncAt = DateTime.UtcNow;
            }
            else
            {
                var newProvider = new StorageProvider
                {
                    Id = Guid.NewGuid(),
                    Name = "Microsoft OneDrive",
                    Type = StorageProviderType.OneDrive,
                    ConfigEncrypted = encryptedConfig,
                    TotalCapacityBytes = quota.TotalCapacityBytes,
                    UsedCapacityBytes = quota.UsedCapacityBytes,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastSyncAt = DateTime.UtcNow
                };
                _context.StorageProviders.Add(newProvider);
            }

            await _context.SaveChangesAsync(cancellationToken);

            return new ExchangeOneDriveCodeResult(
                Success: true,
                Message: "Đăng nhập, kiểm tra và kết nối OneDrive thành công!",
                TotalCapacityBytes: quota.TotalCapacityBytes,
                UsedCapacityBytes: quota.UsedCapacityBytes,
                AvailableCapacityBytes: quota.AvailableCapacityBytes,
                LatencyMs: sw.ElapsedMilliseconds
            );
        }
        catch (Exception ex)
        {
            return new ExchangeOneDriveCodeResult(
                Success: false,
                Message: $"Lỗi xử lý kết nối OneDrive: {ex.Message}"
            );
        }
    }
}
