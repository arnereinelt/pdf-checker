using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Globalization;

namespace PdfChecker;

/// <summary>
/// Builds and evaluates the command line interface of the application.
/// </summary>
internal static class CommandLineParser
{
    private const string DateFormatHint = "date in the current culture format, e.g. 01.01.2024 for German or 01/01/2024 for English";

    /// <summary>
    /// Parses the command line arguments.
    /// </summary>
    /// <param name="args">Raw command line arguments.</param>
    /// <param name="options">The validated options, or <c>null</c> when the program should terminate.</param>
    /// <param name="exitCode">The exit code to return when <paramref name="options"/> is <c>null</c>.</param>
    /// <returns><c>true</c> when the analysis can be started.</returns>
    public static bool TryParse(string[] args, out CommandLineOptions? options, out int exitCode)
    {
        options = null;

        // The help output should always be English, while the date values are parsed using the current culture.
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        var inputOption = new Option<DirectoryInfo>("--input", "-i")
        {
            Description = "Directory that is searched recursively for PDF files.",
            Required = true
        };
        inputOption.Validators.Add(result =>
        {
            var directory = result.GetValueOrDefault<DirectoryInfo>();
            if (directory is not null && !directory.Exists)
            {
                result.AddError($"Input directory does not exist: {directory.FullName}");
            }
        });

        var outputOption = new Option<DirectoryInfo>("--output", "-o")
        {
            Description = "Directory the analysis reports are written to. It is created if it does not exist.",
            Required = true
        };

        var startOption = new Option<DateTime?>("--start", "-s")
        {
            Description = $"Optional inclusive start of the last write time range ({DateFormatHint}).",
            CustomParser = result => ParseDate(result)
        };

        var endOption = new Option<DateTime?>("--end", "-e")
        {
            Description = $"Optional inclusive end of the last write time range ({DateFormatHint}).",
            CustomParser = result => ParseDate(result)
        };

        var rootCommand = new RootCommand(
            "PdfChecker analyzes scanned PDF documents for skew, resolution, sharpness, blank pages and broken web links, " +
            "and aggregates the number of analyzed pages per day.")
        {
            inputOption,
            outputOption,
            startOption,
            endOption
        };

        if (args.Length == 0)
        {
            rootCommand.Parse("--help").Invoke();
            exitCode = 1;
            return false;
        }

        var parseResult = rootCommand.Parse(args);

        var helpOption = rootCommand.Options.OfType<HelpOption>().FirstOrDefault();
        var helpRequested = helpOption is not null && parseResult.GetResult(helpOption) is not null;

        if (helpRequested || parseResult.Errors.Count > 0)
        {
            exitCode = parseResult.Invoke();
            return false;
        }

        var inputDirectory = parseResult.GetValue(inputOption)!;
        var outputDirectory = parseResult.GetValue(outputOption)!;
        var startTime = parseResult.GetValue(startOption);
        var endTime = parseResult.GetValue(endOption);

        if (startTime.HasValue && endTime.HasValue && startTime.Value.Date > endTime.Value.Date)
        {
            Console.Error.WriteLine("Error: --start must not be later than --end.");
            exitCode = 1;
            return false;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory.FullName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Could not create output directory: {outputDirectory.FullName}");
            Console.Error.WriteLine($"Reason: {ex.GetType().Name}: {ex.Message}");
            exitCode = 1;
            return false;
        }

        options = new CommandLineOptions(
            inputDirectory.FullName,
            outputDirectory.FullName,
            startTime,
            endTime);

        exitCode = 0;
        return true;
    }

    private static DateTime? ParseDate(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var text = result.Tokens[0].Value;
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var value))
        {
            return value.Date;
        }

        result.AddError($"'{text}' is not a valid {DateFormatHint}.");
        return null;
    }
}
