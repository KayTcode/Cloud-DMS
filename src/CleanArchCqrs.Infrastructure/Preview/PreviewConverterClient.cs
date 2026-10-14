using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
namespace CleanArchCqrs.Infrastructure.Preview;

public sealed class PreviewConverterClient(HttpClient http, IOptions<PreviewOptions> options) : IDisposable
{
    public async Task<byte[]> ConvertAsync(Stream source, string mime, string hash, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(65));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(options.Value.Endpoint), "convert"));
        request.Content = new StreamContent(source);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        request.Headers.Add("X-Content-SHA256", hash);
        request.Headers.Add("X-Converter-Version", options.Value.ConverterVersion);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/pdf"
            || response.Content.Headers.ContentLength is > PreviewOptions.MaxOutputBytes
            || !response.Headers.TryGetValues("X-Converter-Version", out var versions)
            || versions.SingleOrDefault() != options.Value.ConverterVersion)
            throw new InvalidOperationException("PREVIEW_CONVERTER_UNAVAILABLE");
        await using var body = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, deadline.Token)) != 0)
        {
            if (output.Length + read > PreviewOptions.MaxOutputBytes) throw new InvalidDataException("PREVIEW_OUTPUT_LIMIT");
            await output.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
        }
        var bytes = output.ToArray();
        if (bytes.Length < 5 || !bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            throw new InvalidDataException("PREVIEW_OUTPUT_INVALID");
        return bytes;
    }
    public void Dispose() => http.Dispose();
}
