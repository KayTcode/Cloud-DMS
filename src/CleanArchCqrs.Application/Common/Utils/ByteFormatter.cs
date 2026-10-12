namespace CleanArchCqrs.Application.Common.Utils;

/// <summary>
/// Utility class for formatting byte measurements into human-readable strings.
/// </summary>
public static class ByteFormatter
{
    private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Format(long bytes)
    {
        if (bytes < 0) return "-" + Format(-bytes);
        if (bytes == 0) return "0 B";

        int mag = (int)Math.Log(bytes, 1024);
        mag = Math.Min(mag, SizeSuffixes.Length - 1);
        decimal adjustedSize = (decimal)bytes / (1L << (mag * 10));

        return $"{adjustedSize:0.##} {SizeSuffixes[mag]}";
    }

    public static string FormatBytes(long bytes) => Format(bytes);
}
