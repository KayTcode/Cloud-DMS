using CleanArchCqrs.Application.Common.Interfaces;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using System.Text.Json;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class GoogleDriveStorageService : IStorageService
{
    private readonly DriveService _driveService;

    public GoogleDriveStorageService(string credentialJson)
    {
        _driveService = InitializeDriveService(credentialJson);
    }

    private static DriveService InitializeDriveService(string configJson)
    {
        IConfigurableHttpClientInitializer credential;

        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "service_account")
            {
                // Service Account credentials.json
                credential = GoogleCredential.FromJson(configJson)
                    .CreateScoped(DriveService.Scope.Drive);
            }
            else if (root.TryGetProperty("refresh_token", out var rtProp))
            {
                // OAuth 2.0 User Credential with Refresh Token
                var clientId = root.GetProperty("client_id").GetString()!;
                var clientSecret = root.GetProperty("client_secret").GetString()!;
                var refreshToken = rtProp.GetString()!;

                var tokenResponse = new TokenResponse { RefreshToken = refreshToken };
                var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = clientId,
                        ClientSecret = clientSecret
                    },
                    Scopes = new[] { DriveService.Scope.Drive }
                });

                credential = new UserCredential(flow, "user", tokenResponse);
            }
            else
            {
                // Fallback attempt standard GoogleCredential
                credential = GoogleCredential.FromJson(configJson)
                    .CreateScoped(DriveService.Scope.Drive);
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid Google Drive credentials format: {ex.Message}", ex);
        }

        return new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CloudDMS-MultiCloudPool"
        });
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType, string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var fileMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = fileName,
            Parents = !string.IsNullOrEmpty(parentFolderId) ? new List<string> { parentFolderId } : null
        };

        var request = _driveService.Files.Create(fileMetadata, stream, contentType);
        request.Fields = "id, name, size";
        var progress = await request.UploadAsync(cancellationToken);

        if (progress.Status == Google.Apis.Upload.UploadStatus.Failed)
        {
            throw progress.Exception ?? new InvalidOperationException("Failed to upload file to Google Drive.");
        }

        return request.ResponseBody.Id;
    }

    public async Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var outputStream = new MemoryStream();
        await _driveService.Files.Get(storageKey).DownloadAsync(outputStream, cancellationToken);
        outputStream.Position = 0;
        return outputStream;
    }

    public async Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _driveService.Files.Delete(storageKey).ExecuteAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default)
    {
        var request = _driveService.About.Get();
        request.Fields = "storageQuota";
        var about = await request.ExecuteAsync(cancellationToken);

        long rawLimit = about.StorageQuota?.Limit ?? 0L;
        // Service Account trên Google trả về Limit = 0. Tự động fallback về 15 GB tiêu chuẩn của Google Drive
        long total = rawLimit > 0 ? rawLimit : (15L * 1024 * 1024 * 1024);
        long used = about.StorageQuota?.Usage ?? 0L;
        long available = Math.Max(0, total - used);

        return new StorageQuotaDto(total, used, available);
    }

    public async Task<string> CreateFolderAsync(string folderName, string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var folderMetadata = new Google.Apis.Drive.v3.Data.File
        {
            Name = folderName,
            MimeType = "application/vnd.google-apps.folder",
            Parents = !string.IsNullOrEmpty(parentFolderId) ? new List<string> { parentFolderId } : null
        };

        var request = _driveService.Files.Create(folderMetadata);
        request.Fields = "id";
        var folder = await request.ExecuteAsync(cancellationToken);

        return folder.Id;
    }
}
