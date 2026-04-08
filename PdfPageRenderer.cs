using System;
using System.Drawing;
using PdfiumViewer;

namespace PdfOverlayCompare
{
    /// <summary>
    /// Wraps PdfiumViewer to render individual PDF pages as Bitmap images.
    /// </summary>
    public class PdfPageRenderer : IDisposable
    {
        private PdfDocument _doc;
        private string _filePath;

        public int PageCount { get; private set; }

        public PdfPageRenderer(string filePath)
        {
            _filePath = filePath;
            _doc = PdfDocument.Load(filePath);
            PageCount = _doc.PageCount;
        }

        /// <summary>
        /// Gets the page size in points (1/72 inch).
        /// </summary>
        public SizeF GetPageSizePoints(int pageIndex)
        {
            var size = _doc.PageSizes[pageIndex];
            return new SizeF((float)size.Width, (float)size.Height);
        }

        /// <summary>
        /// Renders the specified page at the given DPI.
        /// </summary>
        public Bitmap RenderPage(int pageIndex, float dpi)
        {
            var size = _doc.PageSizes[pageIndex];
            int w = (int)(size.Width * dpi / 72.0);
            int h = (int)(size.Height * dpi / 72.0);
            var img = _doc.Render(pageIndex, w, h, dpi, dpi, PdfRenderFlags.ForPrinting);
            return new Bitmap(img);
        }

        /// <summary>
        /// Renders the specified page at a given pixel size (stretches to fit).
        /// </summary>
        public Bitmap RenderPageToSize(int pageIndex, int targetWidth, int targetHeight)
        {
            float dpiX = targetWidth * 72f / (float)_doc.PageSizes[pageIndex].Width;
            float dpiY = targetHeight * 72f / (float)_doc.PageSizes[pageIndex].Height;
            float dpi = Math.Min(dpiX, dpiY);
            var img = _doc.Render(pageIndex, targetWidth, targetHeight, dpi, dpi, PdfRenderFlags.ForPrinting);
            return new Bitmap(img);
        }

        public void Dispose()
        {
            _doc?.Dispose();
            _doc = null;
        }
    }
}
