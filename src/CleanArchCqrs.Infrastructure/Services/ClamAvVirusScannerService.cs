using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Services;

public sealed class ClamAvVirusScannerService(IConfiguration configuration,
    IHostEnvironment environment, ILogger<ClamAvVirusScannerService> logger) : IVirusScannerService
{
    public async Task<VirusScanResult> ScanStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // Keep constructor compatibility for existing callers; document content is never logged.
        _ = environment;
        _ = logger;
        if (!configuration.GetValue<bool>("ClamAV:Enabled")) return VirusScanResult.Failed("SCAN_DISABLED");
        // Deployment must verify StreamMaxLength/MaxFileSize/MaxScanSize and AlertExceedsMax.
        // ClamAV can otherwise skip coverage while returning OK. No capability is inferred from OK.
        if (!configuration.GetValue<bool>("SearchIndex:ClamAvCoverageConfirmed"))
            return VirusScanResult.Failed("SCAN_COVERAGE_UNVERIFIED");
        var maxBytes = configuration.GetValue<long?>("SearchIndex:MaxScanBytes") ?? 25L * 1024 * 1024;
        if (maxBytes <= 0 || maxBytes > 2L * 1024 * 1024 * 1024 - 1)
            return VirusScanResult.Failed("SCAN_COVERAGE_UNSUPPORTED");
        var initialPosition = stream.CanSeek ? stream.Position : 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue<int?>("ClamAV:TimeoutSeconds") ?? 3600, 1, 86400)));
        var ct = deadline.Token;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(configuration["ClamAV:Host"] ?? "127.0.0.1", configuration.GetValue<int?>("ClamAV:Port") ?? 3310, ct);
            await using var network = client.GetStream();
            await network.WriteAsync("zINSTREAM\0"u8.ToArray(), ct);
            var buffer = new byte[8192];
            var size = new byte[4];
            int read;
            long totalBytes = 0;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                totalBytes += read;
                if (totalBytes > maxBytes) return VirusScanResult.Failed("SCAN_COVERAGE_UNSUPPORTED");
                BinaryPrimitives.WriteInt32BigEndian(size, read);
                await network.WriteAsync(size, ct);
                await network.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            await network.WriteAsync(new byte[4], ct);
            await network.FlushAsync(ct);
            var response = new List<byte>();
            while (response.Count < 4096)
            {
                read = await network.ReadAsync(buffer.AsMemory(0, 1), ct);
                if (read == 0 || buffer[0] == 0 || buffer[0] == '\n') break;
                response.Add(buffer[0]);
            }
            var text = Encoding.ASCII.GetString(response.ToArray()).TrimEnd('\r');
            if (text == "stream: OK") return VirusScanResult.Clean();
            if (text.Contains("Heuristics.Limits.Exceeded", StringComparison.Ordinal))
                return VirusScanResult.Failed("SCAN_RESOURCE_LIMIT");
            if (text.StartsWith("stream: ", StringComparison.Ordinal) && text.EndsWith(" FOUND", StringComparison.Ordinal))
                return VirusScanResult.Infected("MALWARE_DETECTED");
            return VirusScanResult.Failed("SCAN_PROTOCOL_OR_LIMIT");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return VirusScanResult.Failed("SCAN_TIMEOUT"); }
        catch (OperationCanceledException) { throw; }
        catch (IOException) { return VirusScanResult.Failed("SCAN_IO_OR_LIMIT"); }
        catch (SocketException) { return VirusScanResult.Failed("SCAN_UNAVAILABLE"); }
        finally { if (stream.CanSeek) stream.Position = initialPosition; }
    }
}
