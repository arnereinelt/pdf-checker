using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PdfChecker;

internal readonly record struct PageSkewResult(int Page, double? AngleDegrees, double? Dpi, double? Sharpness, double? InkCoverage, IReadOnlyList<string> Links)
{
    /// <summary>A page whose ink coverage is below the threshold is considered empty.</summary>
    public bool IsBlank => InkCoverage is not null && InkCoverage.Value < SkewDetector.BlankPageInkCoverage;

    /// <summary>Classification of the page orientation relative to a straight scan.</summary>
    public string Orientation => IsBlank
        ? "Blank page"
        : AngleDegrees is null
            ? "Not analyzable"
            : Math.Abs(AngleDegrees.Value) switch
            {
                <= SkewDetector.StraightThresholdDegrees => "OK",
                <= SkewDetector.SlightlySkewedThresholdDegrees => "Slightly crooked",
                _ => "Crooked"
            };

    public bool IsSkewed => AngleDegrees is not null && Math.Abs(AngleDegrees.Value) > SkewDetector.StraightThresholdDegrees;

    /// <summary>Classification of the Laplacian variance: low values mean a blurry scan.</summary>
    public string SharpnessRating => IsBlank
        ? "Blank page"
        : Sharpness switch
        {
            null => "Not analyzable",
            < SkewDetector.BlurryThreshold => "Blurry",
            < SkewDetector.AcceptableSharpnessThreshold => "Slightly blurry",
            _ => "Sharp"
        };
}

/// <summary>
/// Renders PDF pages with PDFium and estimates the scan skew angle via Canny + Hough transform.
/// </summary>
internal static class SkewDetector
{
    internal const double StraightThresholdDegrees = 0.3;

    internal const double SlightlySkewedThresholdDegrees = 1.0;

    /// <summary>Angles beyond this value are treated as unrelated structures, not as scan skew.</summary>
    private const double MaxDetectableSkewDegrees = 30.0;

    /// <summary>Laplacian variance below this value indicates a blurry page.</summary>
    internal const double BlurryThreshold = 100.0;

    /// <summary>Laplacian variance above this value indicates a sharp page.</summary>
    internal const double AcceptableSharpnessThreshold = 300.0;

    /// <summary>Share of non white pixels below which a page is considered blank (0.1 %).</summary>
    internal const double BlankPageInkCoverage = 0.001;

    /// <summary>Gray values below this level count as content (ink), everything above as paper.</summary>
    private const int InkGrayThreshold = 200;

    private const int RenderDpi = 150;

    private static readonly Lock InitLock = new();

    private static bool _initialized;

    internal static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            PdfiumNative.FPDF_InitLibrary();
            _initialized = true;
        }
    }

    internal static List<PageSkewResult> AnalyzeDocument(string pdfFilePath)
    {
        EnsureInitialized();

        var results = new List<PageSkewResult>();

        var document = PdfiumNative.FPDF_LoadDocument(pdfFilePath, null);
        if (document == IntPtr.Zero)
        {
            throw new InvalidOperationException($"PDF could not be opened: {pdfFilePath}");
        }

        try
        {
            var pageCount = PdfiumNative.FPDF_GetPageCount(document);
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageHandle = PdfiumNative.FPDF_LoadPage(document, pageIndex);
                if (pageHandle == IntPtr.Zero)
                {
                    results.Add(new PageSkewResult(pageIndex + 1, null, null, null, null, []));
                    continue;
                }

                try
                {
                    double? dpi = GetScanDpi(pageHandle);
                    List<string> links = PdfLinkExtractor.GetLinks(document, pageHandle);

                    using Mat? page = RenderPage(pageHandle);

                    // A page that cannot be rendered or that provides no usable structures is reported without an angle.
                    double? inkCoverage = page is null ? null : MeasureInkCoverage(page);
                    double? angle = page is null ? null : EstimateSkewAngle(page);
                    double? sharpness = page is null ? null : EstimateSharpness(page);
                    results.Add(new PageSkewResult(pageIndex + 1, angle, dpi, sharpness, inkCoverage, links));
                }
                finally
                {
                    PdfiumNative.FPDF_ClosePage(pageHandle);
                }
            }
        }
        finally
        {
            PdfiumNative.FPDF_CloseDocument(document);
        }

        return results;
    }

    /// <summary>
    /// Resolution of the scanned image on the page. The largest image object determines the value, because that is
    /// the scan itself; smaller images are typically logos or stamps. Pages without image objects (pure vector or
    /// digital text pages) have no scan resolution.
    /// </summary>
    private static double? GetScanDpi(IntPtr page)
    {
        double? dpi = null;
        var largestPixelCount = 0L;

        int objectCount = PdfiumNative.FPDFPage_CountObjects(page);
        for (int i = 0; i < objectCount; i++)
        {
            var pageObject = PdfiumNative.FPDFPage_GetObject(page, i);
            if (pageObject == IntPtr.Zero || PdfiumNative.FPDFPageObj_GetType(pageObject) != PdfiumNative.PageObjectTypeImage)
            {
                continue;
            }

            if (!PdfiumNative.FPDFImageObj_GetImageMetadata(pageObject, page, out FpdfImageObjMetadata metadata))
            {
                continue;
            }

            var pixelCount = (long)metadata.Width * metadata.Height;
            if (pixelCount <= largestPixelCount || metadata.HorizontalDpi <= 0 || metadata.VerticalDpi <= 0)
            {
                continue;
            }

            largestPixelCount = pixelCount;
            dpi = (metadata.HorizontalDpi + metadata.VerticalDpi) / 2.0;
        }

        return dpi;
    }

    private static Mat? RenderPage(IntPtr page)
    {
        var scale = RenderDpi / 72.0;
        var width = (int)Math.Round(PdfiumNative.FPDF_GetPageWidthF(page) * scale);
        var height = (int)Math.Round(PdfiumNative.FPDF_GetPageHeightF(page) * scale);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var stride = width * 4;
        var buffer = Marshal.AllocHGlobal(stride * height);

        try
        {
            var bitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.FPDFBitmapBgra, buffer, stride);
            if (bitmap == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0, PdfiumNative.RenderFlagAnnotations);

                using var view = Mat.FromPixelData(height, width, MatType.CV_8UC4, buffer, stride);
                return view.Clone();
            }
            finally
            {
                PdfiumNative.FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Share of pixels that are darker than <see cref="InkGrayThreshold"/>, i.e. the amount of content on the page.
    /// Used to recognize blank (empty) scan pages.
    /// </summary>
    private static double MeasureInkCoverage(Mat pageImage)
    {
        using var gray = new Mat();
        Cv2.CvtColor(pageImage, gray, ColorConversionCodes.BGRA2GRAY);

        using var ink = new Mat();
        Cv2.Threshold(gray, ink, InkGrayThreshold, 255, ThresholdTypes.BinaryInv);

        double totalPixels = (double)gray.Rows * gray.Cols;
        return totalPixels <= 0 ? 0 : Cv2.CountNonZero(ink) / totalPixels;
    }

    /// <summary>
    /// Sharpness measure: variance of the Laplacian of the grayscale page. Blurry scans contain few strong
    /// intensity changes and therefore produce a low variance.
    /// </summary>
    private static double EstimateSharpness(Mat pageImage)
    {
        using var gray = new Mat();
        Cv2.CvtColor(pageImage, gray, ColorConversionCodes.BGRA2GRAY);

        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

        Cv2.MeanStdDev(laplacian, out Scalar _, out Scalar stdDev);
        return stdDev.Val0 * stdDev.Val0;
    }

    /// <summary>
    /// Grayscale -> Otsu binarization -> horizontal closing (text lines / bar groups) -> median angle of the
    /// resulting minimum area rectangles.
    /// </summary>
    private static double? EstimateSkewAngle(Mat pageImage)
    {
        using var gray = new Mat();
        Cv2.CvtColor(pageImage, gray, ColorConversionCodes.BGRA2GRAY);

        // Foreground (text) becomes white.
        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

        // Merge characters of a text line (or bars of a barcode) into single blobs.
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(25, 3));
        using var closed = new Mat();
        Cv2.MorphologyEx(binary, closed, MorphTypes.Close, kernel);

        Cv2.FindContours(closed, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var weightedAngles = new List<(double Angle, double Weight)>();
        foreach (Point[] contour in contours)
        {
            RotatedRect rect = Cv2.MinAreaRect(contour);

            double longSide = Math.Max(rect.Size.Width, rect.Size.Height);
            double shortSide = Math.Min(rect.Size.Width, rect.Size.Height);

            // Ignore noise and blobs that are not clearly elongated (no reliable direction).
            if (longSide < 60 || shortSide < 3 || longSide / Math.Max(shortSide, 1) < 4)
            {
                continue;
            }

            double angle = rect.Angle;
            if (rect.Size.Width < rect.Size.Height)
            {
                angle += 90;
            }

            while (angle > 45)
            {
                angle -= 90;
            }

            while (angle < -45)
            {
                angle += 90;
            }

            if (Math.Abs(angle) <= MaxDetectableSkewDegrees)
            {
                weightedAngles.Add((angle, longSide));
            }
        }

        if (weightedAngles.Count == 0)
        {
            return null;
        }

        return WeightedMedian(weightedAngles);
    }

    private static double WeightedMedian(List<(double Angle, double Weight)> values)
    {
        values.Sort((a, b) => a.Angle.CompareTo(b.Angle));

        var half = values.Sum(v => v.Weight) / 2.0;
        var running = 0.0;

        foreach ((double angle, double weight) in values)
        {
            running += weight;
            if (running >= half)
            {
                return angle;
            }
        }

        return values[^1].Angle;
    }
}
