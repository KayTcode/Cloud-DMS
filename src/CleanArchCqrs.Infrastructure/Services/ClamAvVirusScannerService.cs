using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Services;

/// <summary>
/// ClamAV Virus Scanner Service using TCP INSTREAM protocol.
/// Safely connects to ClamAV daemon (default 127.0.0.1:3310).
/// Enforces fail-closed in Production / Non-Development environments.
/// </summary>
public class ClamAvVirusScannerService : IVirusScannerService
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _enabled;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ClamAvVirusScannerService> _logger;

    public ClamAvVirusScannerService(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ClamAvVirusScannerService> logger)
    {
        _environment = environment;
        _logger = logger;
        _host = configuration["ClamAV:Host"] ?? "127.0.0.1";
        _port = int.TryParse(configuration["ClamAV:Port"], out var port) ? port : 3310;
        _enabled = bool.TryParse(configuration["ClamAV:Enabled"], out var enabled) && enabled;
    }

    public async Task<VirusScanResult> ScanStreamAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        if (!_enabled)
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogError("ClamAV scanning is disabled via configuration in Non-Development environment. Rejecting upload (Fail-Closed).");
                return VirusScanResult.Infected("SCAN_DISABLED", "Dịch vụ quét mã độc đang bị tắt trên hệ thống máy chủ. Upload bị từ chối vì lý do an toàn bảo mật.");
            }

            _logger.LogDebug("ClamAV scanning is disabled via configuration in Development. Passing as clean.");
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
                if (!_environment.IsDevelopment())
                {
                    _logger.LogError("ClamAV daemon at {Host}:{Port} connection timed out in Non-Development. Fail-closed enforced.", _host, _port);
                    return VirusScanResult.Infected("SCAN_TIMEOUT", "Hệ thống quét virus phản hồi quá hạn (timeout). Tệp tin bị chặn để đảm bảo an toàn hệ thống.");
                }

                _logger.LogWarning("ClamAV daemon at {Host}:{Port} connection timed out in Development. Falling back to Safe Pass.", _host, _port);
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
            if (!_environment.IsDevelopment())
            {
                _logger.LogError(ex, "ClamAV daemon is not reachable at {Host}:{Port} in Non-Development. Fail-closed enforced.", _host, _port);
                return VirusScanResult.Infected("SCAN_UNAVAILABLE", "Dịch vụ kiểm tra mã độc ClamAV không phản hồi. Tệp tin không được phép tải lên môi trường Production.");
            }

            _logger.LogWarning(ex, "ClamAV daemon is not reachable at {Host}:{Port}. Skipping ClamAV scan in development.", _host, _port);
            return VirusScanResult.Clean();
        }
        catch (Exception ex)
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogError(ex, "Error occurred during ClamAV virus scanning in Non-Development. Fail-closed enforced.");
                return VirusScanResult.Infected("SCAN_ERROR", "Xảy ra lỗi trong quá trình quét mã độc tệp tin. Upload bị hủy vì lý do an toàn bảo mật.");
            }

            _logger.LogError(ex, "Error occurred during ClamAV virus scanning in Development.");
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
