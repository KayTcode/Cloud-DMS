namespace CleanArchCqrs.Application.Features.Files.DTOs;

public class MultipleFileUploadResultDto
{
    public List<FileEntryDto> Uploaded { get; set; } = new();
    public List<FileUploadFailureDto> Failed { get; set; } = new();
    public bool AllSucceeded => Failed.Count == 0;
    public int TotalFiles => Uploaded.Count + Failed.Count;
}

public class FileUploadFailureDto
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Error { get; set; } = string.Empty;
}
