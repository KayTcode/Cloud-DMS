using System.Security.Cryptography;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace CleanArchCqrs.Infrastructure.Search;

public sealed class SearchSessionStore : ISearchSessionStore
{
    private readonly RedisSecurityClient redis;
    private readonly IDataProtector cursorProtector;
    private readonly IDataProtector cacheProtector;

    public SearchSessionStore(RedisSecurityClient redis, IDataProtectionProvider protection)
    {
        this.redis = redis;
        cursorProtector = protection.CreateProtector("CloudDMS.Search.Cursor.v1");
        cacheProtector = protection.CreateProtector("CloudDMS.Search.Session.v1");
    }

    public async Task SaveAsync(SearchSession session, CancellationToken ct)
    {
        var ttl = session.ExpiresAt - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero || ttl > SearchPolicy.Lifetime || session.Candidates.Count > SearchPolicy.CandidateBudget)
            throw new SearchUnavailableException();
        var value = cacheProtector.Protect(JsonSerializer.Serialize(session));
        if (value.Length > 1024 * 1024) throw new SearchUnavailableException();
        await redis.SetSearchSessionAsync(session.Id, value, ttl, ct);
    }

    public async Task<SearchSession?> ReadAsync(Guid id, CancellationToken ct)
    {
        var value = await redis.GetSearchSessionAsync(id, ct);
        if (value is null) return null;
        if (value.Length > 1024 * 1024) throw new SearchUnavailableException();
        try
        {
            var result = JsonSerializer.Deserialize<SearchSession>(cacheProtector.Unprotect(value));
            if (result is null || result.Id != id || result.Candidates.Count > SearchPolicy.CandidateBudget)
                throw new SearchUnavailableException();
            return result.ExpiresAt <= DateTimeOffset.UtcNow ? null : result;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { throw new SearchUnavailableException(); }
    }

    public string Protect(ProtectedSearchCursor cursor) => cursorProtector.Protect(JsonSerializer.Serialize(cursor));
    public ProtectedSearchCursor Unprotect(string cursor)
    {
        if (cursor.Length > 4096) throw new InvalidSearchRequestException();
        try
        {
            return JsonSerializer.Deserialize<ProtectedSearchCursor>(cursorProtector.Unprotect(cursor))
                ?? throw new InvalidSearchRequestException();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { throw new InvalidSearchRequestException(); }
    }
}
