namespace PdfChecker;

internal class Program
{
    private const string Analysis = @"analysis";

    private const string AnalysisExtension = ".csv";

    private const string PdfExtension = ".pdf";

    static int Main(string[] args)
    {
        if (!CommandLineParser.TryParse(args, out var options, out var exitCode))
        {
            return exitCode;
        }

        var inputDirectory = options!.InputDirectory;
        var outputDirectory = options.OutputDirectory;

        // Determine the effective reporting range from the matching files when no explicit range was given
        var (startTime, endTime) = DetermineEffectiveRange(inputDirectory, options.StartTime, options.EndTime);

        Console.WriteLine("Starting analysis...");
        Console.WriteLine($"Input Directory: {inputDirectory}");
        Console.WriteLine($"Output Directory: {outputDirectory}");
        Console.WriteLine($"Time range (last write time): {startTime:d} - {endTime:d}");

        // Prepare analysis file
        var analysisFileName = $"{Analysis}_{startTime:yyyyMMdd}_{endTime:yyyyMMdd}{AnalysisExtension}";
        var analysisFilePath = Path.Combine(outputDirectory, analysisFileName);

        // Perform the analysis
        var dailyCounts = AnalyzePdfFiles(inputDirectory, analysisFilePath, startTime, endTime);

        var dailyAnalysisFilePath = Path.Combine(outputDirectory, $"daily_analysis_{startTime:yyyyMMdd}_{endTime:yyyyMMdd}.csv");
        var chartFilePath = Path.Combine(outputDirectory, $"daily_analysis_{startTime:yyyyMMdd}_{endTime:yyyyMMdd}.png");
        var htmlFilePath = Path.Combine(outputDirectory, $"daily_analysis_{startTime:yyyyMMdd}_{endTime:yyyyMMdd}.html");

        GenerateDailyAnalysisAndChart(dailyCounts, dailyAnalysisFilePath, chartFilePath, htmlFilePath, startTime, endTime);

        return 0;
    }

    /// <summary>
    /// Determines the reporting range. Missing bounds are derived from the last write times of the matching PDF files
    /// so that the daily report never spans an unreasonably large period.
    /// </summary>
    private static (DateTime StartTime, DateTime EndTime) DetermineEffectiveRange(string inputDirectory, DateTime? startTime, DateTime? endTime)
    {
        if (startTime.HasValue && endTime.HasValue)
        {
            return (startTime.Value.Date, endTime.Value.Date);
        }

        DateTime? minDate = null;
        DateTime? maxDate = null;

        foreach (var filePath in Directory.EnumerateFiles(inputDirectory, $"*{PdfExtension}", SearchOption.AllDirectories))
        {
            DateTime day;
            try
            {
                day = File.GetLastWriteTime(filePath).Date;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not read the last write time of {filePath}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            if (startTime.HasValue && day < startTime.Value.Date)
            {
                continue;
            }

            if (endTime.HasValue && day > endTime.Value.Date)
            {
                continue;
            }

            if (minDate is null || day < minDate)
            {
                minDate = day;
            }

            if (maxDate is null || day > maxDate)
            {
                maxDate = day;
            }
        }

        var effectiveStart = startTime?.Date ?? minDate ?? DateTime.Today;
        var effectiveEnd = endTime?.Date ?? maxDate ?? effectiveStart;

        if (effectiveEnd < effectiveStart)
        {
            effectiveEnd = effectiveStart;
        }

        return (effectiveStart, effectiveEnd);
    }

    private static Dictionary<DateTime, DailyCounts> AnalyzePdfFiles(string inputDirectory, string analysisFilePath, DateTime startTime, DateTime endTime)
    {
        var pdfResults = new List<(string File, DateTime Date, int Page, string Orientation, double? Deviation, double? Dpi, double? Sharpness, string SharpnessRating, bool IsBlank, int LinkCount, int BrokenLinkCount, int ProtectedLinkCount)>();
        var linkResults = new List<(string File, int Page, string Url, string LinkStatus)>();
        var dailyCounts = new Dictionary<DateTime, DailyCounts>();

        // Collect all PDF files last modified within the given time range (both bounds inclusive)
        var rangeStart = startTime.Date;
        var rangeEndExclusive = endTime.Date.AddDays(1);

        foreach (var filePath in Directory.EnumerateFiles(inputDirectory, $"*{PdfExtension}", SearchOption.AllDirectories))
        {
            var lastWriteTime = File.GetLastWriteTime(filePath);
            if (lastWriteTime < rangeStart || lastWriteTime >= rangeEndExclusive)
            {
                continue;
            }

            var day = lastWriteTime.Date;
            var fileName = Path.GetFileName(filePath);

            List<PageSkewResult> pages;
            try
            {
                pages = SkewDetector.AnalyzeDocument(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing {fileName}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            bool fileIsSkewed = false;
            bool fileHasBrokenLinks = false;
            bool fileHasProtectedLinks = false;
            foreach (PageSkewResult page in pages)
            {
                fileIsSkewed |= page.IsSkewed;

                double? deviation = page.AngleDegrees.HasValue ? Math.Abs(page.AngleDegrees.Value) : null;

                // Verify that every web link of the page can still be reached
                var brokenLinkCount = 0;
                var protectedLinkCount = 0;
                foreach (var url in page.Links)
                {
                    LinkCheckResult link = LinkChecker.Check(url);
                    linkResults.Add((fileName, page.Page, link.Url, link.Status));

                    if (link.IsBroken)
                    {
                        brokenLinkCount++;
                    }
                    else if (link.IsProtected)
                    {
                        protectedLinkCount++;
                    }
                }

                fileHasBrokenLinks |= brokenLinkCount > 0;
                fileHasProtectedLinks |= protectedLinkCount > 0;

                pdfResults.Add((fileName, day, page.Page, page.Orientation, deviation, page.Dpi, page.Sharpness, page.SharpnessRating, page.IsBlank, page.Links.Count, brokenLinkCount, protectedLinkCount));

                Console.WriteLine($"File: {fileName}");
                Console.WriteLine($"Page: {page.Page}");
                Console.WriteLine($"Orientation: {page.Orientation}");
                Console.WriteLine($"Blank Page: {(page.IsBlank ? "yes" : "no")}");
                Console.WriteLine($"Deviation: {(deviation.HasValue ? $"{deviation.Value:0.0}°" : "-")}");
                Console.WriteLine($"Resolution: {(page.Dpi.HasValue ? $"{page.Dpi.Value:0} DPI" : "-")}");
                Console.WriteLine($"Sharpness: {(page.Sharpness.HasValue ? $"{page.Sharpness.Value:0} ({page.SharpnessRating})" : "-")}");
                Console.WriteLine($"Links: {page.Links.Count} (broken: {brokenLinkCount}, protected: {protectedLinkCount})");
            }

            if (fileIsSkewed || fileHasBrokenLinks || fileHasProtectedLinks)
            {
                var current = dailyCounts.TryGetValue(day, out DailyCounts? existing) ? existing : new DailyCounts(0, 0, 0);
                dailyCounts[day] = current with
                {
                    SkewedFiles = current.SkewedFiles + (fileIsSkewed ? 1 : 0),
                    FilesWithBrokenLinks = current.FilesWithBrokenLinks + (fileHasBrokenLinks ? 1 : 0),
                    FilesWithProtectedLinks = current.FilesWithProtectedLinks + (fileHasProtectedLinks ? 1 : 0)
                };
            }
        }

        // Write results to CSV
        try
        {
            using var writer = new StreamWriter(analysisFilePath);

            writer.WriteLine("File;Date;Page;Orientation;BlankPage;Deviation;Dpi;Sharpness;SharpnessRating;Links;BrokenLinks;ProtectedLinks");
            foreach (var result in pdfResults.OrderBy(r => r.File).ThenBy(r => r.Date).ThenBy(r => r.Page))
            {
                writer.WriteLine($"{result.File};{result.Date:yyyy-MM-dd};{result.Page};{result.Orientation};{(result.IsBlank ? "yes" : "no")};{(result.Deviation.HasValue ? result.Deviation.Value.ToString("0.0") : string.Empty)};{(result.Dpi.HasValue ? result.Dpi.Value.ToString("0") : string.Empty)};{(result.Sharpness.HasValue ? result.Sharpness.Value.ToString("0") : string.Empty)};{result.SharpnessRating};{result.LinkCount};{result.BrokenLinkCount};{result.ProtectedLinkCount}");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error: The analysis file could not be written: {analysisFilePath}");
            Console.WriteLine("Please close the file if it is open in another program (e.g. Excel) and run the analysis again.");
            Console.WriteLine($"Reason: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Error: No write access to the analysis file: {analysisFilePath}");
            Console.WriteLine($"Reason: {ex.Message}");
        }

        WriteLinkReport(linkResults, analysisFilePath);

        return dailyCounts;
    }

    private static void WriteLinkReport(List<(string File, int Page, string Url, string LinkStatus)> linkResults, string analysisFilePath)
    {
        var directory = Path.GetDirectoryName(analysisFilePath) ?? string.Empty;
        var linkFilePath = Path.Combine(directory, $"links_{Path.GetFileNameWithoutExtension(analysisFilePath)}{AnalysisExtension}");

        try
        {
            using var writer = new StreamWriter(linkFilePath);

            writer.WriteLine("File;Page;Url;LinkStatus");
            foreach (var link in linkResults.OrderBy(l => l.File).ThenBy(l => l.Page))
            {
                writer.WriteLine($"{link.File};{link.Page};{link.Url};{link.LinkStatus}");
            }

            Console.WriteLine($"Link report written to: {linkFilePath}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error: The link report could not be written: {linkFilePath}");
            Console.WriteLine("Please close the file if it is open in another program (e.g. Excel) and run the analysis again.");
            Console.WriteLine($"Reason: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Error: No write access to the link report: {linkFilePath}");
            Console.WriteLine($"Reason: {ex.Message}");
        }
    }

    private static void GenerateDailyAnalysisAndChart(Dictionary<DateTime, DailyCounts> dailyCounts, string dailyAnalysisFilePath, string chartFilePath, string htmlFilePath, DateTime startTime, DateTime endTime)
    {
        Console.WriteLine();
        Console.WriteLine("Generating daily analysis and chart...");

        try
        {
            // Fill in missing dates with zero counts
            var completeDailyCounts = new SortedDictionary<DateTime, DailyCounts>();
            for (DateTime date = startTime.Date; date <= endTime.Date; date = date.AddDays(1))
            {
                completeDailyCounts[date] = dailyCounts.TryGetValue(date, out DailyCounts? counts) ? counts : new DailyCounts(0, 0, 0);
            }

            // Write daily analysis CSV
            using (var writer = new StreamWriter(dailyAnalysisFilePath))
            {
                writer.WriteLine("Date;CrookedFiles;FilesWithBrokenLinks;FilesWithProtectedLinks");
                foreach (var entry in completeDailyCounts)
                {
                    writer.WriteLine($"{entry.Key:dd.MM.yyyy};{entry.Value.SkewedFiles};{entry.Value.FilesWithBrokenLinks};{entry.Value.FilesWithProtectedLinks}");
                }
            }
            Console.WriteLine($"Daily analysis written to: {dailyAnalysisFilePath}");

            // Generate chart using ScottPlot
            var dates = completeDailyCounts.Keys.ToArray();
            var skewedCounts = completeDailyCounts.Values.Select(v => (double)v.SkewedFiles).ToArray();
            var brokenLinkCounts = completeDailyCounts.Values.Select(v => (double)v.FilesWithBrokenLinks).ToArray();
            var protectedLinkCounts = completeDailyCounts.Values.Select(v => (double)v.FilesWithProtectedLinks).ToArray();
            var dateNumbers = dates.Select(d => d.ToOADate()).ToArray();

            var plot = new ScottPlot.Plot();
            var skewedScatter = plot.Add.Scatter(dateNumbers, skewedCounts);
            skewedScatter.LineWidth = 2;
            skewedScatter.MarkerSize = 8;
            skewedScatter.Color = ScottPlot.Colors.Blue;
            skewedScatter.LegendText = "Crooked Files";

            var brokenLinkScatter = plot.Add.Scatter(dateNumbers, brokenLinkCounts);
            brokenLinkScatter.LineWidth = 2;
            brokenLinkScatter.MarkerSize = 8;
            brokenLinkScatter.Color = ScottPlot.Colors.Red;
            brokenLinkScatter.LegendText = "Files with Broken Links";

            var protectedLinkScatter = plot.Add.Scatter(dateNumbers, protectedLinkCounts);
            protectedLinkScatter.LineWidth = 2;
            protectedLinkScatter.MarkerSize = 8;
            protectedLinkScatter.Color = ScottPlot.Colors.Orange;
            protectedLinkScatter.LegendText = "Files with Protected Links";

            plot.ShowLegend();
            plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.DateTimeAutomatic();

            // File counts are whole numbers, so fractional ticks must not appear on the left axis
            plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic { IntegerTicksOnly = true };
            plot.Axes.Bottom.Label.Text = "Date";
            plot.Axes.Left.Label.Text = "Files";
            plot.Title("Accumulated Values");

            plot.SavePng(chartFilePath, 800, 400);
            Console.WriteLine($"Chart saved to: {chartFilePath}");

            // Generate HTML with chart
            GenerateHtmlReport(completeDailyCounts, htmlFilePath);
            Console.WriteLine($"HTML report saved to: {htmlFilePath}");

            Console.WriteLine();
            Console.WriteLine($"Daily analysis complete. Total dates with occurrences: {dailyCounts.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating chart: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void GenerateHtmlReport(SortedDictionary<DateTime, DailyCounts> dailyCounts, string htmlFilePath)
    {
        var dates = dailyCounts.Keys.Select(d => d.ToString("dd.MM.yyyy")).ToArray();
        var skewedCounts = dailyCounts.Values.Select(v => v.SkewedFiles).ToArray();
        var brokenLinkCounts = dailyCounts.Values.Select(v => v.FilesWithBrokenLinks).ToArray();
        var protectedLinkCounts = dailyCounts.Values.Select(v => v.FilesWithProtectedLinks).ToArray();

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Daily Analysis Report</title>
    <script src='https://cdn.jsdelivr.net/npm/chart.js'></script>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }}
        .container {{ max-width: 1000px; margin: 0 auto; background-color: white; padding: 20px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        h1 {{ color: #333; }}
        canvas {{ max-width: 100%; }}
        table {{ width: 100%; border-collapse: collapse; margin-top: 20px; }}
        th, td {{ padding: 10px; text-align: left; border-bottom: 1px solid #ddd; }}
        th {{ background-color: #4CAF50; color: white; }}
    </style>
</head>
<body>
    <div class='container'>
        <h1>Daily Analysis Report</h1>
        <canvas id='myChart'></canvas>
        <h2>Data Table</h2>
        <table>
            <tr><th>Date</th><th>Crooked Files</th><th>Files with Broken Links</th><th>Files with Protected Links</th></tr>
{string.Join("\n", dailyCounts.Select(kvp => $"            <tr><td>{kvp.Key:dd.MM.yyyy}</td><td>{kvp.Value.SkewedFiles}</td><td>{kvp.Value.FilesWithBrokenLinks}</td><td>{kvp.Value.FilesWithProtectedLinks}</td></tr>"))}
        </table>
    </div>
    <script>
        const ctx = document.getElementById('myChart').getContext('2d');
        const myChart = new Chart(ctx, {{
            type: 'line',
            data: {{
                labels: {System.Text.Json.JsonSerializer.Serialize(dates)},
                datasets: [{{
                    label: 'Crooked Files',
                    data: {System.Text.Json.JsonSerializer.Serialize(skewedCounts)},
                    borderColor: 'rgb(75, 192, 192)',
                    backgroundColor: 'rgba(75, 192, 192, 0.2)',
                    tension: 0.1
                }}, {{
                    label: 'Files with Broken Links',
                    data: {System.Text.Json.JsonSerializer.Serialize(brokenLinkCounts)},
                    borderColor: 'rgb(220, 53, 69)',
                    backgroundColor: 'rgba(220, 53, 69, 0.2)',
                    tension: 0.1
                }}, {{
                    label: 'Files with Protected Links',
                    data: {System.Text.Json.JsonSerializer.Serialize(protectedLinkCounts)},
                    borderColor: 'rgb(255, 159, 64)',
                    backgroundColor: 'rgba(255, 159, 64, 0.2)',
                    tension: 0.1
                }}]
            }},
            options: {{
                responsive: true,
                plugins: {{
                    title: {{
                        display: true,
                        text: 'Accumulated Values'
                    }}
                }},
                scales: {{
                    y: {{
                        beginAtZero: true,
                        ticks: {{
                            precision: 0,
                            stepSize: 1
                        }},
                        title: {{
                            display: true,
                            text: 'Files'
                        }}
                    }},
                    x: {{
                        title: {{
                            display: true,
                            text: 'Date'
                        }}
                    }}
                }}
            }}
        }});
    </script>
</body>
</html>";

        File.WriteAllText(htmlFilePath, html);
    }
}
