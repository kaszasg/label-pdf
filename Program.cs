using System.Globalization;
using System.Text;

namespace LabelPdf;

internal static class Program
{
    private const string DefaultInput = "input.txt";
    private const string DefaultOutput = "output.pdf";
    private const double DefaultFrameInsetMm = 7;

    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (Exception)
        {
            // Some hosts do not allow changing the console encoding; accented
            // characters may then degrade to the console's legacy code page.
        }

        string inputPath = DefaultInput;
        string outputPath = DefaultOutput;
        double? fontSize = null;
        double frameInsetMm = DefaultFrameInsetMm;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help" or "-?" or "/?":
                    PrintUsage();
                    return 0;
                case "-i" or "--input":
                    if (!TryGetOptionValue(args, ref i, out inputPath))
                        return UsageError($"Option '{args[i]}' requires a file path value.");
                    break;
                case "-o" or "--output":
                    if (!TryGetOptionValue(args, ref i, out outputPath))
                        return UsageError($"Option '{args[i]}' requires a file path value.");
                    break;
                case "-f" or "--font-size":
                    if (!TryGetOptionValue(args, ref i, out var sizeText))
                        return UsageError($"Option '{args[i]}' requires a numeric value in points.");
                    if (!double.TryParse(sizeText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                        || !double.IsFinite(size) || size <= 0 || size > 500)
                        return UsageError($"Invalid font size '{sizeText}'. Expected a number greater than 0 and at most 500 (points).");
                    fontSize = size;
                    break;
                case "-m" or "--frame-inset":
                    if (!TryGetOptionValue(args, ref i, out var insetText))
                        return UsageError($"Option '{args[i]}' requires a numeric value in millimeters.");
                    if (!double.TryParse(insetText.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var inset)
                        || !double.IsFinite(inset) || inset < 1.5 || inset > 25)
                        return UsageError($"Invalid frame inset '{insetText}'. Expected a number between 1.5 and 25 (millimeters).");
                    frameInsetMm = inset;
                    break;
                default:
                    return UsageError($"Unknown argument '{args[i]}'.");
            }
        }

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: input file not found: {inputPath}");
            return 2;
        }

        string[] captions;
        try
        {
            captions = File.ReadAllLines(inputPath, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: cannot read input file '{inputPath}': {ex.Message}");
            return 2;
        }

        if (captions.Length == 0)
        {
            Console.Error.WriteLine($"Error: input file '{inputPath}' contains no labels (all lines are empty).");
            return 2;
        }

        try
        {
            var result = LabelSheetGenerator.Generate(captions, outputPath, fontSize, frameInsetMm);

            Console.WriteLine($"Created '{outputPath}': {result.LabelCount} label(s) on {result.PageCount} page(s), font size {result.FontSize.ToString("0.##", CultureInfo.InvariantCulture)} pt.");
            foreach (var caption in result.OverflowingCaptions)
                Console.Error.WriteLine($"Warning: caption does not fit at font size {result.FontSize.ToString("0.##", CultureInfo.InvariantCulture)} pt: {caption}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to create PDF '{outputPath}': {ex.Message}");
            return 2;
        }
    }

    private static bool TryGetOptionValue(string[] args, ref int index, out string value)
    {
        if (index + 1 < args.Length)
        {
            index++;
            value = args[index];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static int UsageError(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        Console.Error.WriteLine("Use --help for usage information.");
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            labelpdf - generates an A4 PDF sheet of 5x2 cut-out labels (10.5 x 6 cm each).

            Usage:
              labelpdf [options]

            Options:
              -i, --input <file>       Input text file, one label caption per line (default: input.txt)
              -o, --output <file>      Output PDF file (default: output.pdf)
              -f, --font-size <pts>    Fixed font size in points (default: automatic, largest that fits every caption)
              -m, --frame-inset <mm>   Distance of the cutting frame from the label edges in millimeters,
                                       1.5 to 25 (default: 7)
              -h, --help               Show this help

            Notes:
              - Captions are printed in uppercase, centered, all with the same font size.
              - A caption may be wrapped to two lines when that allows a larger font.
              - Each label has a thin cutting frame inside the label edges (7 mm by default); cut along the frames.
              - Empty lines in the input file are ignored; more than 10 captions continue on additional pages.
            """);
    }
}
