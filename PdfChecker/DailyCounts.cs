namespace PdfChecker;

/// <summary>
/// Aggregated analysis result of a single day.
/// </summary>
/// <param name="SkewedFiles">Number of files of that day containing at least one skewed page.</param>
/// <param name="FilesWithBrokenLinks">Number of files of that day containing at least one broken web link.</param>
/// <param name="FilesWithProtectedLinks">Number of files of that day containing at least one protected web link.</param>
internal sealed record DailyCounts(int SkewedFiles, int FilesWithBrokenLinks, int FilesWithProtectedLinks);
