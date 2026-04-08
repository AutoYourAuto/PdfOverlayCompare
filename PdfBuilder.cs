using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace PdfOverlayCompare
{
    /// <summary>
    /// Creates a simple PDF from a list of JPEG image files.
    /// Uses raw PDF writing — no external PDF library required.
    /// Each image becomes one page, sized to match the image
    /// dimensions at the specified DPI.
    /// </summary>
    public static class PdfBuilder
    {
        /// <summary>
        /// Creates a PDF file where each image is a full page.
        /// </summary>
        /// <param name="imagePaths">Paths to JPEG image files.</param>
        /// <param name="outputPath">Output PDF file path.</param>
        /// <param name="dpi">The DPI the images were rendered at (used to compute page size in points).</param>
        public static void CreatePdfFromImages(List<string> imagePaths, string outputPath, float dpi)
        {
            // PDF structure:
            //   Header
            //   Objects: Catalog, Pages, (Page + XObject per image)
            //   XRef table
            //   Trailer

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new PdfWriter(fs))
            {
                // Object 1: Catalog
                int catalogObj = writer.AddObject("<< /Type /Catalog /Pages 2 0 R >>");

                // Object 2: Pages (placeholder — we'll rewrite after we know all page refs)
                int pagesObj = writer.ReserveObject();

                // Build pages
                var pageObjIds = new List<int>();

                for (int i = 0; i < imagePaths.Count; i++)
                {
                    byte[] jpegData = File.ReadAllBytes(imagePaths[i]);

                    // Get image dimensions
                    int imgWidth, imgHeight;
                    using (var img = Image.FromFile(imagePaths[i]))
                    {
                        imgWidth = img.Width;
                        imgHeight = img.Height;
                    }

                    // Page size in PDF points (1 point = 1/72 inch)
                    float pageWidth = imgWidth * 72f / dpi;
                    float pageHeight = imgHeight * 72f / dpi;

                    // Object: Image XObject (stream)
                    int imageObj = writer.AddStreamObject(
                        string.Format(
                            "<< /Type /XObject /Subtype /Image /Width {0} /Height {1} " +
                            "/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {{0}} >>",
                            imgWidth, imgHeight),
                        jpegData);

                    // Object: Page content stream (draws the image)
                    string contentStr = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "q\n{0:F2} 0 0 {1:F2} 0 0 cm\n/Img Do\nQ\n",
                        pageWidth, pageHeight);
                    byte[] contentData = Encoding.ASCII.GetBytes(contentStr);

                    int contentObj = writer.AddStreamObject(
                        string.Format("<< /Length {{0}} >>"),
                        contentData);

                    // Object: Resources
                    int resourcesObj = writer.AddObject(
                        string.Format(
                            "<< /XObject << /Img {0} 0 R >> >>",
                            imageObj));

                    // Object: Page
                    int pageObj = writer.AddObject(
                        string.Format(
                            System.Globalization.CultureInfo.InvariantCulture,
                            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {0:F2} {1:F2}] " +
                            "/Contents {2} 0 R /Resources {3} 0 R >>",
                            pageWidth, pageHeight, contentObj, resourcesObj));

                    pageObjIds.Add(pageObj);
                }

                // Now write the Pages object with all kids
                var kidRefs = new StringBuilder();
                foreach (int id in pageObjIds)
                {
                    if (kidRefs.Length > 0) kidRefs.Append(" ");
                    kidRefs.AppendFormat("{0} 0 R", id);
                }

                writer.WriteReservedObject(pagesObj,
                    string.Format(
                        "<< /Type /Pages /Kids [{0}] /Count {1} >>",
                        kidRefs, pageObjIds.Count));

                // Write xref and trailer
                writer.Finish(catalogObj);
            }
        }
    }

    /// <summary>
    /// Low-level PDF writer that tracks object offsets for the xref table.
    /// </summary>
    internal class PdfWriter : IDisposable
    {
        private Stream _stream;
        private List<long> _objectOffsets = new List<long>(); // offset of each object (1-indexed)
        private int _nextObjId = 1;

        public PdfWriter(Stream stream)
        {
            _stream = stream;
            // Write PDF header
            WriteRaw("%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");
        }

        /// <summary>
        /// Adds a simple (non-stream) object. Returns the object ID.
        /// </summary>
        public int AddObject(string content)
        {
            int id = _nextObjId++;
            EnsureOffsetSlot(id);
            _objectOffsets[id - 1] = _stream.Position;
            WriteRaw(string.Format("{0} 0 obj\n{1}\nendobj\n", id, content));
            return id;
        }

        /// <summary>
        /// Reserves an object ID without writing it yet (for forward references).
        /// </summary>
        public int ReserveObject()
        {
            int id = _nextObjId++;
            EnsureOffsetSlot(id);
            return id;
        }

        /// <summary>
        /// Writes a previously reserved object.
        /// </summary>
        public void WriteReservedObject(int id, string content)
        {
            EnsureOffsetSlot(id);
            _objectOffsets[id - 1] = _stream.Position;
            WriteRaw(string.Format("{0} 0 obj\n{1}\nendobj\n", id, content));
        }

        /// <summary>
        /// Adds a stream object. The dictTemplate should contain {0} as a placeholder
        /// for the stream length. Returns the object ID.
        /// </summary>
        public int AddStreamObject(string dictTemplate, byte[] streamData)
        {
            int id = _nextObjId++;
            EnsureOffsetSlot(id);
            _objectOffsets[id - 1] = _stream.Position;

            string dict = string.Format(dictTemplate, streamData.Length);
            WriteRaw(string.Format("{0} 0 obj\n{1}\nstream\n", id, dict));
            _stream.Write(streamData, 0, streamData.Length);
            WriteRaw("\nendstream\nendobj\n");

            return id;
        }

        /// <summary>
        /// Writes the xref table and trailer, finalizing the PDF.
        /// </summary>
        public void Finish(int catalogObjId)
        {
            long xrefOffset = _stream.Position;
            int count = _objectOffsets.Count + 1; // includes object 0

            WriteRaw(string.Format("xref\n0 {0}\n", count));
            WriteRaw("0000000000 65535 f \n"); // object 0 (free)

            foreach (long offset in _objectOffsets)
            {
                WriteRaw(string.Format("{0:D10} 00000 n \n", offset));
            }

            WriteRaw("trailer\n");
            WriteRaw(string.Format(
                "<< /Size {0} /Root {1} 0 R >>\n",
                count, catalogObjId));
            WriteRaw(string.Format("startxref\n{0}\n%%EOF\n", xrefOffset));
        }

        private void EnsureOffsetSlot(int id)
        {
            while (_objectOffsets.Count < id)
                _objectOffsets.Add(0);
        }

        private void WriteRaw(string text)
        {
            byte[] data = Encoding.GetEncoding("ISO-8859-1").GetBytes(text);
            _stream.Write(data, 0, data.Length);
        }

        public void Dispose()
        {
            // Don't dispose the stream — the caller owns it
        }
    }
}
