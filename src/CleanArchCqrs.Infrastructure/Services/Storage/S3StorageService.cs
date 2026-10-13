using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

// Provider configuration is encrypted by the existing storage-provider flow.
public sealed class S3StorageService : IStorageService, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly S3StorageSettings _settings;

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

    public async Task<Stream> DownloadFileAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var response = await _client.GetObjectAsync(_settings.Bucket, storageKey, cancellationToken);
        return new OwnedS3ResponseStream(response, _client);
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

    public async Task<Uri> CreateReadUrlAsync(string key, string fileName, string contentType,
        string purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _client.GetObjectMetadataAsync(_settings.Bucket, key, cancellationToken);
        var disposition = purpose == "download" ? "attachment" : "inline";
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _settings.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = SafeDisposition(disposition, fileName),
                ContentType = contentType,
                CacheControl = "no-store"
            }
        };
        return new Uri(await _client.GetPreSignedURLAsync(request));
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

    public void Dispose() => _client.Dispose();

    private sealed class OwnedS3ResponseStream(GetObjectResponse response, AmazonS3Client client) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
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
            if (disposing) { response.Dispose(); client.Dispose(); }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await _inner.DisposeAsync();
            response.Dispose();
            client.Dispose();
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
