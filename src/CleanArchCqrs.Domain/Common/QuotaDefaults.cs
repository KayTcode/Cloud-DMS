namespace CleanArchCqrs.Domain.Common;

/// <summary>
/// Centralized constants for storage quota limits across the application.
/// </summary>
public static class QuotaDefaults
{
    public const long OneGigabyte = 1L * 1024 * 1024 * 1024;
    public const long FiveGigabytes = 5L * 1024 * 1024 * 1024;
    public const long TenGigabytes = 10L * 1024 * 1024 * 1024;
    public const long FiftyGigabytes = 50L * 1024 * 1024 * 1024;
    public const long FiveHundredGigabytes = 500L * 1024 * 1024 * 1024;
    public const long OneTerabyte = 1024L * 1024 * 1024 * 1024;
}
