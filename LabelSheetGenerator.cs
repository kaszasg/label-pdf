using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace LabelPdf;

internal sealed record GenerationResult(
    int LabelCount,
    int PageCount,
    double FontSize,
    IReadOnlyList<string> OverflowingCaptions);

/// <summary>
/// Renders label captions onto A4 pages as a 2 x 5 grid of 10.5 x 6 cm labels,
/// each with a cutting frame a configurable distance (default 7 mm) inside the
/// label edges.
/// </summary>
internal static class LabelSheetGenerator
{
    private const string FontFamily = "Arial";
    private const int Columns = 2;
    private const int Rows = 5;
    private const int LabelsPerPage = Columns * Rows;

    // All layout metrics are in points (1 pt = 1/72 inch).
    private static readonly double CellWidth = XUnit.FromCentimeter(10.5).Point;
    private static readonly double CellHeight = XUnit.FromCentimeter(6).Point;
    private static readonly double TextPadding = XUnit.FromCentimeter(0.25).Point;
    private const double FrameLineWidth = 0.4;

    // Captions are measured at this size and scaled linearly (glyph metrics scale with the em size).
    private const double ReferenceSize = 100;
    private const double FitEpsilon = 0.01;

    private sealed record CaptionLayout(
        string Caption,
        double SingleLineWidth,
        string[]? TwoLineSplit,
        double TwoLineMaxWidth,
        double MaxFontSize);

    public static GenerationResult Generate(IReadOnlyList<string> rawCaptions, string outputPath, double? requestedFontSize, double frameInsetMm)
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        // The caller validates the range (1.5-25 mm); below 1.5 mm the frames of the top and
        // bottom rows would fall off the page because of the 1.5 mm grid bleed.
        var frameInset = XUnit.FromMillimeter(frameInsetMm).Point;
        var textAreaWidth = CellWidth - 2 * frameInset - 2 * TextPadding;
        var textAreaHeight = CellHeight - 2 * frameInset - 2 * TextPadding;

        var captions = rawCaptions.Select(c => c.ToUpperInvariant()).ToArray();

        var document = new PdfDocument();
        document.Info.Title = "Labels";
        document.Info.Creator = "labelpdf";

        var pageCount = (captions.Length + LabelsPerPage - 1) / LabelsPerPage;
        var pages = new List<XGraphics>(pageCount);
        double gridX = 0, gridY = 0;
        for (var p = 0; p < pageCount; p++)
        {
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Portrait;
            // The 2 x 10.5 cm grid matches the A4 width exactly; the 5 x 6 cm grid exceeds the
            // A4 height by 3 mm, so the grid is centered and the outer cell edges bleed 1.5 mm
            // off the top and bottom. The cutting frames stay fully on the page.
            gridX = (page.Width.Point - Columns * CellWidth) / 2;
            gridY = (page.Height.Point - Rows * CellHeight) / 2;
            pages.Add(XGraphics.FromPdfPage(page));
        }

        var referenceFont = new XFont(FontFamily, ReferenceSize, XFontStyleEx.Bold);
        var referenceLineSpacing = referenceFont.GetHeight();
        var measureGfx = pages[0];

        var layouts = captions.Select(c => MeasureCaption(measureGfx, referenceFont, referenceLineSpacing, c, textAreaWidth, textAreaHeight)).ToArray();
        // Floor to 0.01 pt so the reported size is exact; clamp so a pathologically long
        // caption can never drive the automatic size down to an invisible 0 pt.
        var fontSize = requestedFontSize ?? Math.Max(0.01, Math.Floor(layouts.Min(l => l.MaxFontSize) * 100) / 100);

        var font = new XFont(FontFamily, fontSize, XFontStyleEx.Bold);
        var lineSpacing = referenceLineSpacing * fontSize / ReferenceSize;
        // Captions are uppercase, so the visible glyph block spans from the baseline up to the
        // cap height; centering that block (instead of the full line box with its descender
        // space) puts the text optically in the middle of the frame.
        var capHeight = (double)font.Metrics.CapHeight / font.Metrics.UnitsPerEm * fontSize;
        var baselineFormat = new XStringFormat
        {
            Alignment = XStringAlignment.Center,
            LineAlignment = XLineAlignment.BaseLine,
        };
        var framePen = new XPen(XColors.Black, FrameLineWidth);
        var overflowing = new List<string>();

        for (var i = 0; i < captions.Length; i++)
        {
            var gfx = pages[i / LabelsPerPage];
            var row = i % LabelsPerPage / Columns;
            var col = i % LabelsPerPage % Columns;

            var frame = new XRect(
                gridX + col * CellWidth + frameInset,
                gridY + row * CellHeight + frameInset,
                CellWidth - 2 * frameInset,
                CellHeight - 2 * frameInset);
            gfx.DrawRectangle(framePen, frame);

            var layout = layouts[i];
            var scale = fontSize / ReferenceSize;
            string[] lines;
            double maxLineWidth;
            if (layout.SingleLineWidth * scale <= textAreaWidth + FitEpsilon || layout.TwoLineSplit is null)
            {
                lines = [layout.Caption];
                maxLineWidth = layout.SingleLineWidth * scale;
            }
            else
            {
                lines = layout.TwoLineSplit;
                maxLineWidth = layout.TwoLineMaxWidth * scale;
            }

            if (maxLineWidth > textAreaWidth + FitEpsilon || lines.Length * lineSpacing > textAreaHeight + FitEpsilon)
                overflowing.Add(layout.Caption);

            var blockHeight = (lines.Length - 1) * lineSpacing + capHeight;
            var firstBaseline = frame.Y + (frame.Height - blockHeight) / 2 + capHeight;
            for (var j = 0; j < lines.Length; j++)
            {
                var origin = new XPoint(frame.X + frame.Width / 2, firstBaseline + j * lineSpacing);
                gfx.DrawString(lines[j], font, XBrushes.Black, origin, baselineFormat);
            }
        }

        foreach (var gfx in pages)
            gfx.Dispose();
        document.Save(outputPath);

        return new GenerationResult(captions.Length, pageCount, fontSize, overflowing);
    }

    private static CaptionLayout MeasureCaption(XGraphics gfx, XFont referenceFont, double referenceLineSpacing, string caption, double textAreaWidth, double textAreaHeight)
    {
        var singleLineWidth = gfx.MeasureString(caption, referenceFont).Width;
        var singleLineSize = Math.Min(
            textAreaWidth / singleLineWidth * ReferenceSize,
            textAreaHeight / referenceLineSpacing * ReferenceSize);

        // Try every split point between words and keep the one with the narrowest widest line;
        // two lines win whenever they allow a larger font than the single-line layout.
        string[]? bestSplit = null;
        var bestSplitWidth = double.MaxValue;
        var words = caption.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var k = 1; k < words.Length; k++)
        {
            var first = string.Join(' ', words[..k]);
            var second = string.Join(' ', words[k..]);
            var width = Math.Max(
                gfx.MeasureString(first, referenceFont).Width,
                gfx.MeasureString(second, referenceFont).Width);
            if (width < bestSplitWidth)
            {
                bestSplitWidth = width;
                bestSplit = [first, second];
            }
        }

        var maxFontSize = singleLineSize;
        if (bestSplit is not null)
        {
            var twoLineSize = Math.Min(
                textAreaWidth / bestSplitWidth * ReferenceSize,
                textAreaHeight / (2 * referenceLineSpacing) * ReferenceSize);
            maxFontSize = Math.Max(singleLineSize, twoLineSize);
        }

        return new CaptionLayout(caption, singleLineWidth, bestSplit, bestSplitWidth, maxFontSize);
    }
}
