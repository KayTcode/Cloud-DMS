using System.Security.Cryptography;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using Microsoft.AspNetCore.DataProtection;
namespace CleanArchCqrs.Infrastructure.Preview;
public sealed class FileTextCursorProtector(IDataProtectionProvider provider) : IFileTextCursorProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("CloudDMS.FileText.Cursor.v1");
    public string Protect(FileTextCursor cursor) => protector.Protect(JsonSerializer.Serialize(cursor));
    public FileTextCursor Unprotect(string value)
    {
        if (value.Length > 4096) throw new InvalidSearchRequestException();
        try { return JsonSerializer.Deserialize<FileTextCursor>(protector.Unprotect(value)) ?? throw new InvalidSearchRequestException(); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { throw new InvalidSearchRequestException(); }
    }
}
