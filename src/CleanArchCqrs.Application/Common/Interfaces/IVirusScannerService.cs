namespace CleanArchCqrs.Application.Common.Interfaces;

public record VirusScanResult(bool IsInfected, string? ThreatName = null, string? Message = null)
{
    public static VirusScanResult Clean() => new(false, null, "File is clean. No threats detected.");
    public static VirusScanResult Infected(string threatName, string? message = null) => 
        new(true, threatName, message ?? $"Security threat detected: {threatName}");
}

public interface IVirusScannerService
{
    Task<VirusScanResult> ScanStreamAsync(Stream stream, CancellationToken cancellationToken = default);
}
