using System.IO.Compression;
using System.Text;
using CleanArchCqrs.Application.Common.Models.Files;
using CleanArchCqrs.Application.Features.Files.Queries.SearchFiles;
using CleanArchCqrs.Infrastructure.Search;
using CleanArchCqrs.Infrastructure.Search.Extraction;
using Microsoft.Extensions.Options;
namespace CloudDms.IntegrationTests.Files;
public sealed class ExtractionRegressionTests
{
    private readonly FileTextExtractor extractor = new(Options.Create(new DocumentIndexOptions()));
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("notes.csv")]
    [InlineData("notes.md")]
    public async Task Utf8VietnameseAndMarkupArePlainText(string name)
    {
        const string text = "Tài liệu tiếng Việt 日本語 <script>example</script>";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result = await extractor.ExtractAsync(stream, name, default);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal(text, Assert.Single(result.Sections).Text);
    }
    [Theory]
    [InlineData("image.svg", "<svg xmlns='http://www.w3.org/2000/svg'><script/></svg>", "UNSUPPORTED_TYPE")]
    [InlineData("page.html", "<html><script>alert(1)</script></html>", "UNSUPPORTED_TYPE")]
    [InlineData("binary.txt", "abc\0def", "UNSUPPORTED_BINARY")]
    public async Task ActiveOrBinaryTypesNeverPromoted(string name, string content, string reason)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = await extractor.ExtractAsync(stream, name, default);
        Assert.Equal("application/octet-stream", result.ContentType);
        Assert.Equal(reason, result.Reason); Assert.Empty(result.Sections);
    }
    [Fact]
    public async Task InvalidUtf8IsMetadataOnly()
    {
        using var stream = new MemoryStream([0xff, 0xfe, 0xff]);
        var result = await extractor.ExtractAsync(stream, "bad.txt", default);
        Assert.Empty(result.Sections);
    }
    private static MemoryStream Office(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var entry in entries.Prepend(("[Content_Types].xml", "<Types/>")))
            { using var writer = new StreamWriter(archive.CreateEntry(entry.Item1).Open(), new UTF8Encoding(false)); writer.Write(entry.Item2); }
        }
        stream.Position = 0; return stream;
    }
    [Fact]
    public async Task DocxExtractsParagraphsWithoutNetworkOrNativeParser()
    {
        using var stream = Office(("word/document.xml", "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Ngân sách</w:t></w:r></w:p></w:body></w:document>"));
        var result = await extractor.ExtractAsync(stream, "report.docx", default);
        Assert.Contains(result.Sections, s => s.Text.Contains("Ngân sách"));
    }
    [Fact]
    public async Task DocxExternalEntityFailsClosed()
    {
        using var stream = Office(("word/document.xml", "<!DOCTYPE x [<!ENTITY secret SYSTEM 'file:///etc/passwd'>]><w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:t>&secret;</w:t></w:p></w:body></w:document>"));
        var result = await extractor.ExtractAsync(stream, "report.docx", default);
        Assert.Empty(result.Sections); Assert.Equal("MALFORMED_XML", result.Reason);
    }
    [Fact]
    public async Task DuplicateZipEntriesFailClosed()
    {
        using var stream = Office(("word/document.xml", "<x/>"), ("word/document.xml", "<x/>"));
        var result = await extractor.ExtractAsync(stream, "report.docx", default);
        Assert.Empty(result.Sections); Assert.Equal("MALFORMED_OR_RESOURCE_LIMIT", result.Reason);
    }
    [Fact]
    public async Task PdfOnWindowsCannotFallbackToUnsandboxedParser()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var stream = new MemoryStream("%PDF-1.4 fake fixture"u8.ToArray());
        var result = await extractor.ExtractAsync(stream, "report.pdf", default);
        Assert.Empty(result.Sections); Assert.True(result.IsPartial);
    }
    [Fact]
    public void RrfCollapsesChunksBeforeRankAndWeightsBranchesEqually()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var b = Guid.Parse("00000000-0000-0000-0000-000000000002");
        SearchCandidate C(Guid id, string source, double score, int ordinal = 0) => new(id, 1, null, ordinal, score, source, null);
        var ranked = ReciprocalRankFusion.Rank([C(a,"keyword",100), C(a,"keyword",99,1), C(b,"keyword",90), C(b,"semantic",.9), C(a,"semantic",.8)]);
        Assert.Equal(2, ranked.Count); Assert.Equal(a, ranked[0].FileId);
        Assert.Equal(1.0/61 + 1.0/62, ranked[0].Score, 12);
        Assert.Equal(ranked[0].Score, ranked[1].Score); Assert.Equal(2, ranked[0].Evidence.Count);
    }
}
