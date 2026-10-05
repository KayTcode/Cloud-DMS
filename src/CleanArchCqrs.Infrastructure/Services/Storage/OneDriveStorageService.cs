using Azure.Core;
using Azure.Identity;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession;
using Microsoft.Graph.Models;
using System.Text.Json;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class OneDriveStorageService : IStorageService
{
    private readonly GraphServiceClient _graphClient;
    private readonly string? _userEmail;
    private readonly bool _isUserDelegated;
    private string? _cachedDriveId;

    public OneDriveStorageService(string configJson)
    {
        try
        {
            using var doc = ParseJsonSafely(configJson);
            var root = doc.RootElement;

            // Kiểm tra các biến thể viết hoa/thường: client_id hoặc clientId
            string clientId = GetPropertyString(root, "client_id", "clientId") 
                ?? throw new ArgumentException("Thiếu thông tin 'client_id' hoặc 'clientId'.");

            string clientSecret = GetPropertyString(root, "client_secret", "clientSecret") 
                ?? throw new ArgumentException("Thiếu thông tin 'client_secret' hoặc 'clientSecret'.");

            string? tenantId = GetPropertyString(root, "tenant_id", "tenantId");

            // Trường hợp 1: Có refresh_token (OAuth 2.0 User Delegated Flow)
            if (root.TryGetProperty("refresh_token", out var rtProp) && !string.IsNullOrWhiteSpace(rtProp.GetString()))
            {
                var refreshToken = rtProp.GetString()!;
                string? redirectUri = GetPropertyString(root, "redirect_uri", "redirectUri");

                // Đối với token cá nhân MSA, tenant_id bắt buộc là "common" để tránh bị lỗi SPO license
                if (refreshToken.StartsWith("M.", StringComparison.OrdinalIgnoreCase) || refreshToken.Contains("MsaArtifacts", StringComparison.OrdinalIgnoreCase))
                {
                    tenantId = "common";
                }

                var credential = new OneDriveRefreshTokenCredential(clientId, clientSecret, refreshToken, tenantId ?? "common", redirectUri);
                _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
                _isUserDelegated = true;
            }
            // Trường hợp 2: Client Secret Credential (Daemon / Service Principal Flow)
            else
            {
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    throw new ArgumentException("Thiếu thông tin 'tenant_id' / 'tenantId' cho chế độ ClientSecretCredential.");
                }

                if (root.TryGetProperty("userEmail", out var emailProp))
                {
                    _userEmail = emailProp.GetString();
                }

                var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
                _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
                _isUserDelegated = false;
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Cấu hình OneDrive không hợp lệ: {ex.Message}", ex);
        }
    }

    private static JsonDocument ParseJsonSafely(string configJson)
    {
        try
        {
            return JsonDocument.Parse(configJson);
        }
        catch (JsonException)
        {
            try
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    configJson, 
                    @"""(?<key>[a-zA-Z0-9_-]+)""\s*:\s*""(?<val>[\s\S]*?)""(?=\s*[,}\]])");

                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    var k = m.Groups["key"].Value;
                    var v = m.Groups["val"].Value
                        .Replace("\r", "")
                        .Replace("\n", "")
                        .Trim();
                    dict[k] = v;
                }

                if (dict.Count > 0)
                {
                    var validJson = JsonSerializer.Serialize(dict);
                    return JsonDocument.Parse(validJson);
                }
            }
            catch
            {
                // Bỏ qua và ném ngoại lệ gốc bên dưới
            }

            throw;
        }
    }

    private static string? GetPropertyString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString();
            }
        }
        return null;
    }

    private async Task<string> GetDriveIdAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedDriveId))
        {
            return _cachedDriveId;
        }

        if (_isUserDelegated)
        {
            var myDrive = await _graphClient.Me.Drive.GetAsync(cancellationToken: cancellationToken);
            _cachedDriveId = myDrive?.Id ?? throw new InvalidOperationException("Không tìm thấy OneDrive của người dùng (Me.Drive).");
        }
        else if (!string.IsNullOrEmpty(_userEmail))
        {
            var userDrive = await _graphClient.Users[_userEmail].Drive.GetAsync(cancellationToken: cancellationToken);
            _cachedDriveId = userDrive?.Id ?? throw new InvalidOperationException($"Không tìm thấy OneDrive của tài khoản '{_userEmail}'.");
        }
        else
        {
            try
            {
                var drives = await _graphClient.Drives.GetAsync(cancellationToken: cancellationToken);
                _cachedDriveId = drives?.Value?.FirstOrDefault()?.Id;
            }
            catch (Exception ex) when (ex.Message.Contains("SPO license", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Tổ chức Azure của bạn không có gói bản quyền doanh nghiệp (SharePoint Online). Đối với tài khoản cá nhân, vui lòng bấm nút 'Đăng nhập Microsoft' ở trên để kết nối tự động qua OAuth2.");
            }

            if (string.IsNullOrEmpty(_cachedDriveId))
            {
                throw new InvalidOperationException("Không tìm thấy Drive mặc định trong tổ chức Azure Entra ID. Vui lòng bấm 'Đăng nhập Microsoft' để kết nối tài khoản cá nhân.");
            }
        }

        return _cachedDriveId;
    }

    public async Task<string> UploadFileAsync(
        Stream stream, 
        string fileName, 
        string contentType, 
        string? parentFolderId = null, 
        CancellationToken cancellationToken = default)
    {
        var driveId = await GetDriveIdAsync(cancellationToken);
        string folderId = string.IsNullOrEmpty(parentFolderId) || parentFolderId == "root" ? "root" : parentFolderId;

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        long streamLength = stream.Length;
        const long maxSimpleUploadBytes = 4 * 1024 * 1024; // 4MB

        // 1. Tải lên trực tiếp nếu file <= 4MB
        if (streamLength <= maxSimpleUploadBytes)
        {
            DriveItem? uploadedItem;
            if (folderId == "root")
            {
                uploadedItem = await _graphClient.Drives[driveId].Root
                    .ItemWithPath(fileName).Content
                    .PutAsync(stream, cancellationToken: cancellationToken);
            }
            else
            {
                uploadedItem = await _graphClient.Drives[driveId].Items[folderId]
                    .ItemWithPath(fileName).Content
                    .PutAsync(stream, cancellationToken: cancellationToken);
            }

            return uploadedItem?.Id ?? Guid.NewGuid().ToString();
        }

        // 2. Tải lên phân đoạn (Resumable Chunked Upload) nếu file > 4MB
        var uploadSessionRequestBody = new CreateUploadSessionPostRequestBody
        {
            Item = new DriveItemUploadableProperties
            {
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", "rename" }
                }
            }
        };

        UploadSession? uploadSession;
        if (folderId == "root")
        {
            uploadSession = await _graphClient.Drives[driveId].Root
                .ItemWithPath(fileName)
                .CreateUploadSession
                .PostAsync(uploadSessionRequestBody, cancellationToken: cancellationToken);
        }
        else
        {
            uploadSession = await _graphClient.Drives[driveId].Items[folderId]
                .ItemWithPath(fileName)
                .CreateUploadSession
                .PostAsync(uploadSessionRequestBody, cancellationToken: cancellationToken);
        }

        if (uploadSession?.UploadUrl == null)
        {
            throw new InvalidOperationException("Không thể tạo phiên tải lên (Upload Session) với Microsoft OneDrive.");
        }

        // Chunk size 3.2MB (bội số của 320 KiB theo chuẩn Microsoft Graph)
        int maxSliceSize = 320 * 1024 * 10;
        var fileUploadTask = new LargeFileUploadTask<DriveItem>(uploadSession, stream, maxSliceSize, _graphClient.RequestAdapter);
        var uploadResult = await fileUploadTask.UploadAsync(cancellationToken: cancellationToken);

        if (!uploadResult.UploadSucceeded)
        {
            throw new InvalidOperationException("Quá trình tải tệp tin phân đoạn lên OneDrive thất bại.");
        }

        return uploadResult.ItemResponse?.Id ?? Guid.NewGuid().ToString();
    }

    public async Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var driveId = await GetDriveIdAsync(cancellationToken);
        var stream = await _graphClient.Drives[driveId].Items[storageKey].Content
            .GetAsync(cancellationToken: cancellationToken);

        if (stream == null)
        {
            throw new FileNotFoundException("Không tìm thấy tệp tin trên Microsoft OneDrive.", storageKey);
        }

        var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var driveId = await GetDriveIdAsync(cancellationToken);
            await _graphClient.Drives[driveId].Items[storageKey]
                .DeleteAsync(cancellationToken: cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default)
    {
        Drive? drive;
        if (_isUserDelegated)
        {
            drive = await _graphClient.Me.Drive.GetAsync(cancellationToken: cancellationToken);
        }
        else
        {
            var driveId = await GetDriveIdAsync(cancellationToken);
            drive = await _graphClient.Drives[driveId].GetAsync(cancellationToken: cancellationToken);
        }

        long total = drive?.Quota?.Total ?? (1024L * 1024 * 1024 * 1024); // 1 TB mặc định
        long used = drive?.Quota?.Used ?? 0L;
        long available = drive?.Quota?.Remaining ?? Math.Max(0, total - used);

        return new StorageQuotaDto(total, used, available);
    }

    public async Task<string> CreateFolderAsync(string folderName, string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var driveId = await GetDriveIdAsync(cancellationToken);
        var folderItem = new DriveItem
        {
            Name = folderName,
            Folder = new Folder(),
            AdditionalData = new Dictionary<string, object>
            {
                { "@microsoft.graph.conflictBehavior", "rename" }
            }
        };

        var targetParentId = string.IsNullOrEmpty(parentFolderId) || parentFolderId == "root" ? "root" : parentFolderId;
        var created = await _graphClient.Drives[driveId].Items[targetParentId].Children
            .PostAsync(folderItem, cancellationToken: cancellationToken);

        return created?.Id ?? folderName;
    }
}
