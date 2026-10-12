namespace CleanArchCqrs.Application.Common.Interfaces;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Issuer { get; set; } = "https://accounts.google.com";
    public string[] AllowedDomains { get; set; } = Array.Empty<string>();
}
