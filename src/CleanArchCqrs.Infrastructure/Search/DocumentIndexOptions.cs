namespace CleanArchCqrs.Infrastructure.Search;

public sealed class DocumentIndexOptions
{
    public bool Enabled { get; set; } = true;
    public long MaxScanBytes { get; set; } = 25L * 1024 * 1024;
    public bool ClamAvCoverageConfirmed { get; set; }
    public string CoverageKey => $"{MaxScanBytes}:{ClamAvCoverageConfirmed}";
    public int ScanTimeoutSeconds { get; set; } = 3600;
    public int RetentionMinutes { get; set; } = 60;
    public string TokenizerEndpoint { get; set; } = "http://127.0.0.1:8001/";
    public string ModelVersion { get; set; } = EmbeddingOptions.PinnedModel;
    public string PdfTextExecutable { get; set; } = "pdftotext";
    public const long AnalysisBytes = 100L * 1024 * 1024;
    public const int MaxCharacters = 16 * 1024 * 1024;
    public const int MaxPages = 1000;
    public const int MaxChunks = 2000;
}
