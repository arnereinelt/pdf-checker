using System.Runtime.InteropServices;

namespace PdfChecker;

/// <summary>
/// Native PDFium interop (bblanchon.PDFium) limited to the functions needed for page rendering.
/// </summary>
internal static partial class PdfiumNative
{
    private const string LibraryName = "pdfium";

    [LibraryImport(LibraryName)]
    internal static partial void FPDF_InitLibrary();

    [LibraryImport(LibraryName)]
    internal static partial void FPDF_DestroyLibrary();

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr FPDF_LoadDocument(string filePath, string? password);

    [LibraryImport(LibraryName)]
    internal static partial void FPDF_CloseDocument(IntPtr document);

    [LibraryImport(LibraryName)]
    internal static partial int FPDF_GetPageCount(IntPtr document);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [LibraryImport(LibraryName)]
    internal static partial void FPDF_ClosePage(IntPtr page);

    [LibraryImport(LibraryName)]
    internal static partial float FPDF_GetPageWidthF(IntPtr page);

    [LibraryImport(LibraryName)]
    internal static partial float FPDF_GetPageHeightF(IntPtr page);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [LibraryImport(LibraryName)]
    internal static partial void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [LibraryImport(LibraryName)]
    internal static partial void FPDFBitmap_Destroy(IntPtr bitmap);

    [LibraryImport(LibraryName)]
    internal static partial void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [LibraryImport(LibraryName)]
    internal static partial int FPDFPage_CountObjects(IntPtr page);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDFText_LoadPage(IntPtr page);

    [LibraryImport(LibraryName)]
    internal static partial void FPDFText_ClosePage(IntPtr textPage);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDFLink_LoadWebLinks(IntPtr textPage);

    [LibraryImport(LibraryName)]
    internal static partial void FPDFLink_CloseWebLinks(IntPtr pageLink);

    [LibraryImport(LibraryName)]
    internal static partial int FPDFLink_CountWebLinks(IntPtr pageLink);

    /// <summary>Writes the URL as UTF-16 and returns the number of characters including the terminating zero.</summary>
    [LibraryImport(LibraryName)]
    internal static partial int FPDFLink_GetURL(IntPtr pageLink, int linkIndex, IntPtr buffer, int bufferLength);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FPDFLink_Enumerate(IntPtr page, ref int startPosition, out IntPtr linkAnnotation);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDFLink_GetAction(IntPtr linkAnnotation);

    /// <summary>Writes the URI as UTF-8 and returns the number of bytes including the terminating zero.</summary>
    [LibraryImport(LibraryName)]
    internal static partial uint FPDFAction_GetURIPath(IntPtr document, IntPtr action, IntPtr buffer, uint bufferLength);

    [LibraryImport(LibraryName)]
    internal static partial IntPtr FPDFPage_GetObject(IntPtr page, int index);

    [LibraryImport(LibraryName)]
    internal static partial int FPDFPageObj_GetType(IntPtr pageObject);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool FPDFImageObj_GetImageMetadata(IntPtr imageObject, IntPtr page, out FpdfImageObjMetadata metadata);

    /// <summary>FPDF_PAGEOBJ_IMAGE.</summary>
    internal const int PageObjectTypeImage = 3;

    /// <summary>BGRA, 4 bytes per pixel.</summary>
    internal const int FPDFBitmapBgra = 4;

    /// <summary>FPDF_ANNOT: render annotations as well.</summary>
    internal const int RenderFlagAnnotations = 0x01;
}

/// <summary>FPDF_IMAGEOBJ_METADATA.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FpdfImageObjMetadata
{
    /// <summary>Pixel width of the embedded image.</summary>
    internal uint Width;

    /// <summary>Pixel height of the embedded image.</summary>
    internal uint Height;

    /// <summary>Horizontal resolution in DPI as placed on the page.</summary>
    internal float HorizontalDpi;

    /// <summary>Vertical resolution in DPI as placed on the page.</summary>
    internal float VerticalDpi;

    internal uint BitsPerPixel;

    internal int Colorspace;

    internal int MarkedContentId;
}
