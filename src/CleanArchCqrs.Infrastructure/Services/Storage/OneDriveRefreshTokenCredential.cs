using Azure.Core;
using System.Text.Json;

namespace CleanArchCqrs.Infrastructure.Services.Storage;

public class OneDriveRefreshTokenCredential : TokenCredential
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly HttpClient _httpClient;
    private string _refreshToken;
    private AccessToken _cachedToken;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private readonly string? _redirectUri;

    public OneDriveRefreshTokenCredential(
        string clientId, 
        string clientSecret, 
        string refreshToken, 
        string? tenantId = null, 
        string? redirectUri = null,
        HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        
        var cleanedRt = refreshToken?.Trim() ?? string.Empty;
        if (cleanedRt.Contains('%'))
        {
            try { cleanedRt = Uri.UnescapeDataString(cleanedRt); } catch { }
        }
        _refreshToken = cleanedRt;
        _tenantId = string.IsNullOrWhiteSpace(tenantId) ? "common" : tenantId;
        _redirectUri = redirectUri;
        _httpClient = httpClient ?? new HttpClient();
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_cachedToken.Token) && DateTimeOffset.UtcNow < _cachedToken.ExpiresOn.AddMinutes(-5))
        {
            return _cachedToken;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrEmpty(_cachedToken.Token) && DateTimeOffset.UtcNow < _cachedToken.ExpiresOn.AddMinutes(-5))
            {
                return _cachedToken;
            }

            bool isMsa = _refreshToken.StartsWith("M.", StringComparison.OrdinalIgnoreCase) 
                      || _refreshToken.Contains("MsaArtifacts", StringComparison.OrdinalIgnoreCase);

            var candidates = new List<(string Tenant, string Scope)>();
            if (isMsa || string.Equals(_tenantId, "common", StringComparison.OrdinalIgnoreCase) || string.Equals(_tenantId, "consumers", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(("consumers", "https://graph.microsoft.com/Files.ReadWrite"));
                candidates.Add(("common", "https://graph.microsoft.com/Files.ReadWrite"));
                candidates.Add(("consumers", "https://graph.microsoft.com/.default"));
                candidates.Add(("common", "https://graph.microsoft.com/.default"));
            }
            else
            {
                candidates.Add((_tenantId.Trim(), "https://graph.microsoft.com/.default"));
                candidates.Add(("common", "https://graph.microsoft.com/Files.ReadWrite"));
                candidates.Add(("consumers", "https://graph.microsoft.com/Files.ReadWrite"));
                candidates.Add(("common", "https://graph.microsoft.com/.default"));
            }

            HttpResponseMessage? lastResponse = null;
            string lastContent = string.Empty;

            foreach (var (tenant, scope) in candidates)
            {
                var tokenEndpoint = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
                var bodyParams = new Dictionary<string, string>
                {
                    { "client_id", _clientId.Trim() },
                    { "grant_type", "refresh_token" },
                    { "refresh_token", _refreshToken.Trim() },
                    { "scope", scope }
                };

                if (!string.IsNullOrWhiteSpace(_clientSecret))
                {
                    bodyParams["client_secret"] = _clientSecret.Trim();
                }

                if (!string.IsNullOrWhiteSpace(_redirectUri))
                {
                    bodyParams["redirect_uri"] = _redirectUri.Trim();
                }

                var formContent = new FormUrlEncodedContent(bodyParams);
                lastResponse?.Dispose();
                lastResponse = await _httpClient.PostAsync(tokenEndpoint, formContent, cancellationToken);
                lastContent = await lastResponse.Content.ReadAsStringAsync(cancellationToken);

                if (lastResponse.IsSuccessStatusCode)
                {
                    break;
                }
            }

            if (lastResponse == null || !lastResponse.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Lỗi khi làm mới OneDrive Token ({lastResponse?.StatusCode}): {lastContent}");
            }

            using var doc = JsonDocument.Parse(lastContent);
            var root = doc.RootElement;
            var accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();

            if (root.TryGetProperty("refresh_token", out var newRefreshToken))
            {
                var newRt = newRefreshToken.GetString();
                if (!string.IsNullOrEmpty(newRt))
                {
                    _refreshToken = newRt;
                }
            }

            _cachedToken = new AccessToken(accessToken, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
            return _cachedToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
