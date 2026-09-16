using System.Runtime.InteropServices;
using System.Text;

namespace PdfChecker;

/// <summary>
/// Extracts the web links of a PDF page: both link annotations (clickable areas with a URI action) and
/// URLs that PDFium recognizes inside the page text.
/// </summary>
internal static class PdfLinkExtractor
{
    internal static List<string> GetLinks(IntPtr document, IntPtr page)
    {
        var links = new List<string>();

        AddAnnotationLinks(document, page, links);
        AddTextLinks(page, links);

        return links;
    }

    private static void AddAnnotationLinks(IntPtr document, IntPtr page, List<string> links)
    {
        int position = 0;
        while (PdfiumNative.FPDFLink_Enumerate(page, ref position, out IntPtr linkAnnotation))
        {
            IntPtr action = PdfiumNative.FPDFLink_GetAction(linkAnnotation);
            if (action == IntPtr.Zero)
            {
                continue;
            }

            uint length = PdfiumNative.FPDFAction_GetURIPath(document, action, IntPtr.Zero, 0);
            if (length <= 1)
            {
                continue;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)length);
            try
            {
                PdfiumNative.FPDFAction_GetURIPath(document, action, buffer, length);

                // The returned buffer contains a terminating zero byte.
                var bytes = new byte[length - 1];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                AddIfWebLink(links, Encoding.UTF8.GetString(bytes));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    private static void AddTextLinks(IntPtr page, List<string> links)
    {
        IntPtr textPage = PdfiumNative.FPDFText_LoadPage(page);
        if (textPage == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr pageLink = PdfiumNative.FPDFLink_LoadWebLinks(textPage);
            if (pageLink == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int count = PdfiumNative.FPDFLink_CountWebLinks(pageLink);
                for (int i = 0; i < count; i++)
                {
                    int length = PdfiumNative.FPDFLink_GetURL(pageLink, i, IntPtr.Zero, 0);
                    if (length <= 1)
                    {
                        continue;
                    }

                    IntPtr buffer = Marshal.AllocHGlobal(length * sizeof(char));
                    try
                    {
                        PdfiumNative.FPDFLink_GetURL(pageLink, i, buffer, length);

                        // The returned buffer contains a terminating zero character.
                        AddIfWebLink(links, Marshal.PtrToStringUni(buffer, length - 1) ?? string.Empty);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            finally
            {
                PdfiumNative.FPDFLink_CloseWebLinks(pageLink);
            }
        }
        finally
        {
            PdfiumNative.FPDFText_ClosePage(textPage);
        }
    }

    private static void AddIfWebLink(List<string> links, string url)
    {
        url = url.Trim();

        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!links.Contains(url, StringComparer.OrdinalIgnoreCase))
        {
            links.Add(url);
        }
    }
}
