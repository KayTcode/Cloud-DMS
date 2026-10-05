using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Services;

/// <summary>
/// ClamAV Virus Scanner Service using TCP INSTREAM protocol.
/// Safely connects to ClamAV daemon (default 127.0.0.1:3310).
/// </summary>
public class ClamAvVirusScannerService : IVirusScannerService
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _enabled;
    private readonly ILogger<ClamAvVirusScannerService> _logger;

    public ClamAvVirusScannerService(IConfiguration configuration, ILogger<ClamAvVirusScannerService> logger)
    {
        _logger = logger;
        _host = configuration["ClamAV:Host"] ?? "127.0.0.1";
        _port = int.TryParse(configuration["ClamAV:Port"], out var port) ? port : 3310;
        _enabled = bool.TryParse(configuration["ClamAV:Enabled"], out var enabled) && enabled;
    }

    public async Task<VirusScanResult> ScanStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            _logger.LogDebug("ClamAV scanning is disabled via configuration. Passing as clean.");
            return VirusScanResult.Clean();
        }

        long initialPosition = stream.CanSeek ? stream.Position : 0;

        try
        {
            using var tcpClient = new TcpClient();
            var connectTask = tcpClient.ConnectAsync(_host, _port);
            
            // Connect timeout 3s
            if (await Task.WhenAny(connectTask, Task.Delay(3000, cancellationToken)) != connectTask)
            {
                _logger.LogWarning("ClamAV daemon at {Host}:{Port} connection timed out. Falling back to Safe Pass.", _host, _port);
                return VirusScanResult.Clean();
            }

            using var networkStream = tcpClient.GetStream();

            // 1. Send INSTREAM command: "zINSTREAM\0"
            var instreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await networkStream.WriteAsync(instreamCommand, cancellationToken);

            // 2. Send chunks: [4 bytes size in network byte order (Big Endian)] + [chunk data]
            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                var lengthBytes = new byte[4];
                BinaryPrimitives.WriteInt32BigEndian(lengthBytes, bytesRead);

                await networkStream.WriteAsync(lengthBytes, cancellationToken);
                await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            }

            // 3. Send 4 zero bytes to terminate stream chunking
            await networkStream.WriteAsync(new byte[4], cancellationToken);
            await networkStream.FlushAsync(cancellationToken);

            // 4. Read response
            using var reader = new StreamReader(networkStream, Encoding.ASCII);
            var response = await reader.ReadToEndAsync(cancellationToken);
            response = response.Trim('\0', '\r', '\n');

            _logger.LogInformation("ClamAV scan completed. Raw response: {Response}", response);

            if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
            {
                return VirusScanResult.Clean();
            }

            if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
            {
                // Format is usually "stream: <VirusName> FOUND"
                var parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var virusName = parts.Length >= 2 ? parts[1] : "Unknown_Malware";
                _logger.LogWarning("ClamAV detected infected file! Virus: {VirusName}", virusName);
                return VirusScanResult.Infected(virusName, $"Phát hiện mã độc: {virusName}");
            }

            return VirusScanResult.Clean();
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "ClamAV daemon is not reachable at {Host}:{Port}. Skipping ClamAV scan in development.", _host, _port);
            return VirusScanResult.Clean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during ClamAV virus scanning.");
            return VirusScanResult.Clean();
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }
}
