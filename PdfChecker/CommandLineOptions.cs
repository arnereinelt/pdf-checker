namespace PdfChecker;

/// <summary>
/// Validated command-line configuration of the application.
/// </summary>
/// <param name="InputDirectory">Existing directory that is searched recursively for PDF files.</param>
/// <param name="OutputDirectory">Directory the reports are written to.</param>
/// <param name="StartTime">Inclusive lower bound for the last write time, or <c>null</c> if not specified.</param>
/// <param name="EndTime">Inclusive upper bound for the last write time, or <c>null</c> if not specified.</param>
internal sealed record CommandLineOptions(
    string InputDirectory,
    string OutputDirectory,
    DateTime? StartTime,
    DateTime? EndTime);
