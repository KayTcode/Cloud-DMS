namespace CleanArchCqrs.Infrastructure.Preview;
public sealed class PreviewOptions
{
    public bool Enabled { get; set; } = false;
    public string Endpoint { get; set; } = "http://127.0.0.1:8002/";
    // Change this whenever the converter image/profile changes; source hash is also part of identity.
    public string ConverterVersion { get; set; } = "libreoffice-sandbox-v1";
    public const long MaxInputBytes = 100L * 1024 * 1024;
    public const int MaxOutputBytes = 32 * 1024 * 1024;
    public string CacheVersion(string hash) => $"{ConverterVersion}:{hash.ToLowerInvariant()}";
    public static bool IsOffice(string mime) => mime is
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" or
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or
        "application/vnd.openxmlformats-officedocument.presentationml.presentation";
}
