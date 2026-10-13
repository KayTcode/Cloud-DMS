using System.Security.Cryptography;

namespace CleanArchCqrs.Infrastructure.Search.Extraction;

// Scanner consumes the entire immutable source once; hash/capture see exactly the scanned bytes.
internal sealed class ScanCaptureStream(Stream source, Stream capture, long limit) : Stream
{
    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    public long BytesRead { get; private set; }
    public bool ReachedEnd { get; private set; }
    public string FinishHash() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await source.ReadAsync(buffer, ct);
        if (read == 0) { ReachedEnd = true; return 0; }
        BytesRead = checked(BytesRead + read);
        if (BytesRead > limit) throw new IOException("SCAN_BYTE_LIMIT");
        hash.AppendData(buffer.Span[..read]);
        var remaining = DocumentIndexOptions.AnalysisBytes - capture.Length;
        if (remaining > 0) await capture.WriteAsync(buffer[..(int)Math.Min(remaining, read)], ct);
        return read;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
    protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
