using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

// Provider configuration is encrypted by the existing storage-provider flow.
public sealed class S3StorageService : IStorageService, IMultipartObjectStorage, IIntentObjectStorage
{
    private readonly AmazonS3Client _client;
    private readonly S3StorageSettings _settings;
    private int _disposed;

    public S3StorageService(string rawConfigJson)
    {
        _settings = JsonSerializer.Deserialize<S3StorageSettings>(rawConfigJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("Invalid S3 provider configuration.");
        if (!Uri.TryCreate(_settings.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("https" or "http")
            || endpoint.Scheme == "http" && !endpoint.IsLoopback
            || endpoint.AbsolutePath != "/" || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment) || !string.IsNullOrEmpty(endpoint.UserInfo)
            || string.IsNullOrWhiteSpace(_settings.Bucket)
            || string.IsNullOrWhiteSpace(_settings.Region)
            || string.IsNullOrWhiteSpace(_settings.AccessKey)
            || string.IsNullOrWhiteSpace(_settings.SecretKey)
            || _settings.CapacityBytes <= 0)
            throw new ArgumentException("Invalid S3 provider configuration.");

        _client = new AmazonS3Client(new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = endpoint.GetLeftPart(UriPartial.Authority),
                AuthenticationRegion = _settings.Region,
                ForcePathStyle = true
            });
    }

    public async Task<string> UploadFileAsync(Stream stream, string fileName, string contentType,
        string? parentFolderId = null, CancellationToken cancellationToken = default)
    {
        var key = $"objects/{Guid.NewGuid():N}";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = contentType
        }, cancellationToken);
        return key;
    }

    public async Task<string> UploadForIntentAsync(Stream stream, Guid intentId, string contentType,
        string? parentFolderId, CancellationToken cancellationToken)
    {
        var key = $"objects/legacy/{intentId:N}";
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _settings.Bucket, Key = key,
            InputStream = stream, ContentType = contentType
        }, cancellationToken);
        return key;
    }

    public async Task<IReadOnlyList<string>> FindIntentObjectsAsync(Guid intentId,
        string? parentFolderId, CancellationToken cancellationToken)
    {
        var key = $"objects/legacy/{intentId:N}";
        return await InspectAsync(key, cancellationToken) is null ? Array.Empty<string>() : new[] { key };
    }

    public async Task<string> BeginAsync(string key, string contentType, CancellationToken cancellationToken)
    {
        var response = await _client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = _settings.Bucket, Key = key, ContentType = contentType
        }, cancellationToken);
        return response.UploadId;
    }

    public async Task<Uri> PresignPartAsync(string key, string uploadId, int partNumber,
        TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var url = await _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket, Key = key, Verb = HttpVerb.PUT,
            UploadId = uploadId, PartNumber = partNumber,
            Expires = DateTime.UtcNow.Add(lifetime)
        });
        return new Uri(url);
    }

    public async Task CompleteAsync(string key, string uploadId, IReadOnlyList<MultipartPart> parts,
        CancellationToken cancellationToken)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _settings.Bucket, Key = key, UploadId = uploadId,
            PartETags = parts.Select(p => new PartETag(p.Number, p.ETag)).ToList()
        };
        await _client.CompleteMultipartUploadAsync(request, cancellationToken);
    }

    public async Task AbortAsync(string key, string uploadId, CancellationToken cancellationToken) =>
        await _client.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
        {
            BucketName = _settings.Bucket, Key = key, UploadId = uploadId
        }, cancellationToken);

    public async Task<MultipartObjectInfo?> InspectAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.GetObjectMetadataAsync(_settings.Bucket, key, cancellationToken);
            return new(result.ContentLength, result.Headers.ContentType ?? "application/octet-stream");
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<IncompleteMultipartUpload>> ListIncompleteAsync(
        DateTimeOffset startedBefore, CancellationToken cancellationToken)
    {
        var result = new List<IncompleteMultipartUpload>();
        string? keyMarker = null;
        string? uploadIdMarker = null;
        do
        {
            var response = await _client.ListMultipartUploadsAsync(new ListMultipartUploadsRequest
            {
                BucketName = _settings.Bucket, KeyMarker = keyMarker,
                UploadIdMarker = uploadIdMarker, MaxUploads = 1000
            }, cancellationToken);
            result.AddRange(response.MultipartUploads
                .Where(x => x.Initiated.HasValue
                    && x.Initiated.Value.ToUniversalTime() < startedBefore.UtcDateTime
                    && IsManagedMultipartKey(x.Key))
                .Select(x => new IncompleteMultipartUpload(x.Key, x.UploadId,
                    new DateTimeOffset(x.Initiated!.Value.ToUniversalTime()))));
            if (response.IsTruncated != true) break;
            keyMarker = response.NextKeyMarker;
            uploadIdMarker = response.NextUploadIdMarker;
        } while (!string.IsNullOrEmpty(keyMarker));
        return result;
    }

    private static bool IsManagedMultipartKey(string key)
    {
        var parts = key.Split('/');
        return parts.Length == 5 && parts[0] == "objects"
            && Guid.TryParseExact(parts[1], "N", out _)
            && Guid.TryParseExact(parts[2], "N", out _)
            && long.TryParse(parts[3], out var version) && version > 0
            && Guid.TryParseExact(parts[4], "N", out _);
    }

    public async Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        GetObjectResponse? response = null;
        try
        {
            response = await _client.GetObjectAsync(_settings.Bucket, storageKey, cancellationToken);
            return new OwnedS3ResponseStream(response, this);
        }
        catch
        {
            try { response?.Dispose(); }
            finally { Dispose(); }
            throw;
        }
    }

    public async Task<bool> DeleteFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        await _client.DeleteObjectAsync(_settings.Bucket, storageKey, cancellationToken);
        return true;
    }

    public async Task<StorageQuotaDto> GetProviderQuotaAsync(CancellationToken cancellationToken = default)
    {
        await _client.GetBucketLocationAsync(new GetBucketLocationRequest { BucketName = _settings.Bucket }, cancellationToken);
        // S3 does not report bucket quota. Capacity is a required operator policy;
        // persisted provider usage remains the authoritative counter.
        return new StorageQuotaDto(_settings.CapacityBytes, 0, _settings.CapacityBytes);
    }

    public Task<string> CreateFolderAsync(string folderName, string? parentFolderId = null,
        CancellationToken cancellationToken = default) => Task.FromResult("objects");

    public async Task<CleanArchCqrs.Application.Common.Models.Files.ObjectReadAccess> CreateReadUrlAsync(string key, string fileName, string contentType,
        string purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.GetObjectMetadataAsync(_settings.Bucket, key, cancellationToken);
        var disposition = purpose == "download" ? "attachment" : "inline";
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = SafeDisposition(disposition, fileName),
                ContentType = contentType,
                CacheControl = "no-store"
            }
        };
        return new(new Uri(await _client.GetPreSignedURLAsync(request)), expiresAt, true);
    }

    private static string SafeDisposition(string disposition, string value)
    {
        var name = value.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        name = new string(name.Where(c => !char.IsControl(c) && c is not '"' and not '\\').ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "file";
        name = name[..Math.Min(name.Length, 180)];
        var ascii = new string(name.Select(c => c is >= ' ' and <= '~' ? c : '_').ToArray());
        return $"{disposition}; filename=\"{ascii}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _client.Dispose();
    }

    private sealed class OwnedS3ResponseStream(GetObjectResponse response, S3StorageService owner) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        private int _disposed;
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try { response.Dispose(); }
                finally { owner.Dispose(); }
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try { await _inner.DisposeAsync(); }
                finally
                {
                    try { response.Dispose(); }
                    finally { owner.Dispose(); }
                }
            }
            await base.DisposeAsync();
        }
    }

    private sealed class S3StorageSettings
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Bucket { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string AccessKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
        public long CapacityBytes { get; set; }
    }
}
