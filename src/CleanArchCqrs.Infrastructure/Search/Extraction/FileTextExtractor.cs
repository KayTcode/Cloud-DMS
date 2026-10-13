using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Search.Extraction;

public sealed class FileTextExtractor(IOptions<DocumentIndexOptions> options) : IFileTextExtractor
{
    public async Task<TextExtractionResult> ExtractAsync(Stream content, string fileName, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(120));
        ct = deadline.Token;
        if (!content.CanSeek || content.Length > DocumentIndexOptions.AnalysisBytes)
            return new("application/octet-stream", [], true, "ANALYSIS_BYTE_LIMIT");
        byte[] magic = new byte[8];
        var count = await content.ReadAsync(magic, ct);
        content.Position = 0;
        try
        {
            if (count >= 5 && magic.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                return await PdfAsync(content, ct);
            if (count >= 4 && magic[0] == 'P' && magic[1] == 'K' && magic[2] == 3 && magic[3] == 4)
                return await OfficeAsync(content, ct);
            if (Path.GetExtension(fileName).ToLowerInvariant() is ".txt" or ".csv" or ".md" or ".markdown")
            {
                using var reader = new StreamReader(content, new UTF8Encoding(false, true), true, 8192, true);
                var text = await ReadBoundedTextAsync(reader, ct);
                if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                    return new("application/octet-stream", [], false, "UNSUPPORTED_BINARY");
                // Always serve verified text as text/plain; never promote markup from an extension.
                return new("text/plain", [new(text, "text")], false,
                    string.IsNullOrWhiteSpace(text) ? "NO_EXTRACTABLE_TEXT" : null);
            }
            return new("application/octet-stream", [], false, "UNSUPPORTED_TYPE");
        }
        catch (DecoderFallbackException) { return new("application/octet-stream", [], false, "UNSUPPORTED_ENCODING"); }
        catch (InvalidDataException) { return new("application/octet-stream", [], true, "MALFORMED_OR_RESOURCE_LIMIT"); }
        catch (XmlException) { return new("application/octet-stream", [], true, "MALFORMED_XML"); }
    }

    private static async Task<string> ReadBoundedTextAsync(TextReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var chars = new char[8192];
        int read;
        while ((read = await reader.ReadAsync(chars, ct)) != 0)
        {
            if (result.Length + read > DocumentIndexOptions.MaxCharacters) throw new InvalidDataException();
            result.Append(chars, 0, read);
        }
        return result.ToString();
    }

    private static async Task<TextExtractionResult> OfficeAsync(Stream content, CancellationToken ct)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, true);
        if (zip.Entries.Count > 10000) throw new InvalidDataException();
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            expanded = checked(expanded + entry.Length);
            if (!names.Add(entry.FullName) || expanded > DocumentIndexOptions.AnalysisBytes
                || entry.Length > DocumentIndexOptions.MaxCharacters
                || entry.Length > Math.Max(1, entry.CompressedLength) * 100)
                throw new InvalidDataException();
        }
        if (zip.GetEntry("[Content_Types].xml") is null) return new("application/octet-stream", [], false, "UNSUPPORTED_ZIP");
        var sections = new List<TextSection>();
        int totalChars = 0;
        var partial = false;
        var sectionLimit = false;
        async Task<XDocument> Xml(ZipArchiveEntry e)
        {
            using var stream = e.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = DocumentIndexOptions.MaxCharacters, MaxCharactersFromEntities = 0
            });
            return await XDocument.LoadAsync(reader, LoadOptions.None, ct);
        }
        void Add(string text, string location)
        {
            if (sections.Count >= DocumentIndexOptions.MaxPages) { partial = true; sectionLimit = true; return; }
            totalChars = checked(totalChars + text.Length);
            if (totalChars > DocumentIndexOptions.MaxCharacters) throw new InvalidDataException();
            if (!string.IsNullOrWhiteSpace(text)) sections.Add(new(text, location));
        }
        bool SectionBudgetAvailable()
        {
            ct.ThrowIfCancellationRequested();
            if (sections.Count < DocumentIndexOptions.MaxPages) return true;
            partial = true;
            sectionLimit = true;
            return false;
        }
        void AppendBounded(StringBuilder builder, string value, int limit)
        {
            ct.ThrowIfCancellationRequested();
            if (value.Length > limit - builder.Length) throw new InvalidDataException();
            builder.Append(value);
        }
        string mime;
        var types = await Xml(zip.GetEntry("[Content_Types].xml")!);
        if (types.Descendants().Attributes("ContentType").Any(a => a.Value.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)))
            return new("application/octet-stream", [], false, "UNSUPPORTED_MACRO_PACKAGE");
        if (zip.GetEntry("word/document.xml") is { } word)
        {
            mime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            var xml = await Xml(word);
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            int index = 0;
            foreach (var paragraph in xml.Descendants(w + "p"))
            {
                if (!SectionBudgetAvailable()) break;
                Add(string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value)), $"paragraph:{++index}");
            }
        }
        else if (zip.GetEntry("xl/workbook.xml") is not null)
        {
            mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var shared = new List<string>();
            var sharedChars = 0;
            if (zip.GetEntry("xl/sharedStrings.xml") is { } strings)
            {
                foreach (var item in (await Xml(strings)).Descendants(s + "si"))
                {
                    ct.ThrowIfCancellationRequested();
                    if (shared.Count >= 100000) throw new InvalidDataException();
                    var value = new StringBuilder();
                    foreach (var part in item.Descendants(s + "t"))
                        AppendBounded(value, part.Value, DocumentIndexOptions.MaxCharacters - sharedChars);
                    sharedChars += value.Length;
                    shared.Add(value.ToString());
                }
            }
            var sheets = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal)
                && e.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
            partial = sheets.Length > DocumentIndexOptions.MaxPages;
            foreach (var sheet in sheets.Take(DocumentIndexOptions.MaxPages))
            {
                if (!SectionBudgetAvailable()) break;
                var xml = await Xml(sheet);
                foreach (var row in xml.Descendants(s + "row"))
                {
                    if (!SectionBudgetAvailable()) break;
                    // Shared-string references can amplify a tiny worksheet into gigabytes. Charge each
                    // cell before append, including separators, and never materialize an unbounded row.
                    var rowText = new StringBuilder();
                    var rowLimit = Math.Min(1024 * 1024, DocumentIndexOptions.MaxCharacters - totalChars);
                    var firstCell = true;
                    foreach (var cell in row.Elements(s + "c"))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!firstCell) AppendBounded(rowText, "\t", rowLimit);
                        firstCell = false;
                        var value = cell.Element(s + "v")?.Value;
                        if ((string?)cell.Attribute("t") == "s")
                        {
                            if (int.TryParse(value, out var i) && i >= 0 && i < shared.Count)
                                AppendBounded(rowText, shared[i], rowLimit);
                        }
                        else if (value is not null) AppendBounded(rowText, value, rowLimit);
                        else foreach (var part in cell.Descendants(s + "t"))
                            AppendBounded(rowText, part.Value, rowLimit);
                    }
                    Add(rowText.ToString(), $"{sheet.FullName}:row:{(string?)row.Attribute("r")}");
                }
            }
        }
        else if (zip.GetEntry("ppt/presentation.xml") is not null)
        {
            mime = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var slides = zip.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)
                && e.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal).ToArray();
            partial = slides.Length > DocumentIndexOptions.MaxPages;
            foreach (var slide in slides.Take(DocumentIndexOptions.MaxPages))
            {
                if (!SectionBudgetAvailable()) break;
                Add(string.Join("\n", (await Xml(slide)).Descendants(a + "t").Select(t => t.Value)), slide.FullName);
            }
        }
        else return new("application/octet-stream", [], false, "UNSUPPORTED_ZIP");
        return new(mime, sections, partial, sectionLimit ? "SECTION_LIMIT" : partial ? "PAGE_LIMIT" : sections.Count == 0 ? "NO_EXTRACTABLE_TEXT" : null);
    }

    private async Task<TextExtractionResult> PdfAsync(Stream content, CancellationToken ct)
    {
        // Poppler runs as a cancellable child process. No shell or document-controlled arguments.
        var path = Path.Combine(Path.GetTempPath(), $"dms-{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(output, ct);
            var start = new ProcessStartInfo(options.Value.PdfTextExecutable)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8 };
            foreach (var arg in new[] { "-enc", "UTF-8", "-layout", "-f", "1", "-l", "1001", path, "-" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("PDF_EXTRACTOR_UNAVAILABLE");
            using var kill = ct.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var textTask = ReadBoundedTextAsync(process.StandardOutput, ct);
            var errorTask = ReadBoundedTextAsync(process.StandardError, ct);
            try
            {
                while (!process.HasExited)
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.WorkingSet64 > 512L * 1024 * 1024 || textTask.IsFaulted || errorTask.IsFaulted)
                        throw new InvalidDataException();
                    await Task.Delay(100, ct);
                }
                var text = await textTask;
                await errorTask; // Drain only; parser stderr may contain document data and is never logged.
                if (process.ExitCode != 0) throw new InvalidDataException();
                var pages = text.Split('\f');
                var sections = pages.Take(DocumentIndexOptions.MaxPages).Select((p, i) => new TextSection(p, $"page:{i + 1}"))
                    .Where(s => !string.IsNullOrWhiteSpace(s.Text)).ToArray();
                var partial = pages.Length > DocumentIndexOptions.MaxPages + 1
                    || pages.Skip(DocumentIndexOptions.MaxPages).Any(p => !string.IsNullOrWhiteSpace(p));
                return new("application/pdf", sections, partial, partial ? "PAGE_LIMIT" : sections.Length == 0 ? "NO_TEXT_NO_OCR" : null);
            }
            finally
            {
                if (!process.HasExited) process.Kill(true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        finally { File.Delete(path); }
    }
}
