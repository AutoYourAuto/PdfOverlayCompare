using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PdfOverlayCompare
{
    /// <summary>
    /// Composites PDF A (reference) and PDF B (transformed) into a single
    /// overlay image with color-coded differences.
    /// Red   = content only in PDF A
    /// Blue  = content only in PDF B
    /// Gray  = identical content in both
    /// </summary>
    public static class OverlayEngine
    {
        /// <summary>
        /// Content detection threshold (0-255). Pixels darker than this are "content".
        /// </summary>
        public static int ContentThreshold { get; set; } = 200;

        /// <summary>
        /// Difference tolerance in pixels for morphological merging (handles sub-pixel shifts).
        /// </summary>
        public static int MergeTolerance { get; set; } = 3;

        /// <summary>
        /// Creates the overlay composite. pdfB is transformed via the affine matrix
        /// to align with pdfA's coordinate space.
        /// </summary>
        public static Bitmap CreateOverlay(Bitmap pdfA, Bitmap pdfB, AffineTransform transform)
        {
            int w = pdfA.Width;
            int h = pdfA.Height;

            // Transform PDF B into PDF A's space
            Bitmap pdfBWarped = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(pdfBWarped))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;

                if (transform != null && transform.IsValid)
                {
                    g.MultiplyTransform(transform.ToGdiMatrix());
                }

                g.DrawImage(pdfB, 0, 0, pdfB.Width, pdfB.Height);
            }

            // Extract grayscale arrays
            byte[] grayA = ToGrayscale(pdfA);
            byte[] grayB = ToGrayscale(pdfBWarped);

            // Build binary content masks
            bool[] contentA = new bool[w * h];
            bool[] contentB = new bool[w * h];
            for (int i = 0; i < w * h; i++)
            {
                contentA[i] = grayA[i] < ContentThreshold;
                contentB[i] = grayB[i] < ContentThreshold;
            }

            // Dilate both masks to create tolerance zones
            bool[] dilatedA = Dilate(contentA, w, h, MergeTolerance);
            bool[] dilatedB = Dilate(contentB, w, h, MergeTolerance);

            // Classify each pixel
            // onlyA: content in A but not near any B content
            // onlyB: content in B but not near any A content
            // both:  content in both (identical or very close)
            bool[] onlyARegion = new bool[w * h];
            bool[] onlyBRegion = new bool[w * h];

            for (int i = 0; i < w * h; i++)
            {
                if (contentA[i] && !dilatedB[i])
                    onlyARegion[i] = true;
                if (contentB[i] && !dilatedA[i])
                    onlyBRegion[i] = true;
            }

            // Build result bitmap
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bmpData = result.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            byte[] pixels = new byte[w * h * 4];

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int pi = i * 4; // BGRA

                    if (onlyARegion[i])
                    {
                        // Red - only in PDF A
                        pixels[pi + 0] = 40;   // B
                        pixels[pi + 1] = 40;   // G
                        pixels[pi + 2] = 210;  // R
                        pixels[pi + 3] = 255;  // A
                    }
                    else if (onlyBRegion[i])
                    {
                        // Blue - only in PDF B
                        pixels[pi + 0] = 210;  // B
                        pixels[pi + 1] = 80;   // G
                        pixels[pi + 2] = 20;   // R
                        pixels[pi + 3] = 255;
                    }
                    else if (contentA[i] && contentB[i])
                    {
                        // Both have content - dark gray
                        pixels[pi + 0] = 60;
                        pixels[pi + 1] = 60;
                        pixels[pi + 2] = 60;
                        pixels[pi + 3] = 255;
                    }
                    else if (contentA[i] || contentB[i])
                    {
                        // Content in one, within tolerance of other - medium gray
                        pixels[pi + 0] = 140;
                        pixels[pi + 1] = 140;
                        pixels[pi + 2] = 140;
                        pixels[pi + 3] = 255;
                    }
                    else
                    {
                        // Background - white
                        pixels[pi + 0] = 250;
                        pixels[pi + 1] = 250;
                        pixels[pi + 2] = 250;
                        pixels[pi + 3] = 255;
                    }
                }
            }

            Marshal.Copy(pixels, 0, bmpData.Scan0, pixels.Length);
            result.UnlockBits(bmpData);

            pdfBWarped.Dispose();
            return result;
        }

        /// <summary>
        /// Converts a Bitmap to a grayscale byte array.
        /// </summary>
        private static byte[] ToGrayscale(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            byte[] gray = new byte[w * h];

            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, w, h),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            byte[] pixels = new byte[w * h * 4];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            bmp.UnlockBits(data);

            for (int i = 0; i < w * h; i++)
            {
                int pi = i * 4;
                // Luminance: 0.299*R + 0.587*G + 0.114*B
                gray[i] = (byte)(0.299 * pixels[pi + 2] + 0.587 * pixels[pi + 1] + 0.114 * pixels[pi + 0]);
            }

            return gray;
        }

        /// <summary>
        /// Simple box dilation of a boolean mask.
        /// </summary>
        private static bool[] Dilate(bool[] mask, int w, int h, int radius)
        {
            bool[] result = new bool[w * h];
            // Use integral image approach for speed
            // First pass: horizontal
            bool[] temp = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                int count = 0;
                // sliding window
                for (int x = 0; x < w; x++)
                {
                    // Add right edge
                    if (x + radius < w && mask[y * w + x + radius])
                        count++;
                    // Check center
                    if (mask[y * w + x])
                        count++;

                    if (count > 0)
                        temp[y * w + x] = true;
                }
            }

            // Simpler but correct approach: direct box check
            Array.Clear(result, 0, result.Length);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (mask[y * w + x])
                    {
                        // Set all pixels within radius
                        int yMin = Math.Max(0, y - radius);
                        int yMax = Math.Min(h - 1, y + radius);
                        int xMin = Math.Max(0, x - radius);
                        int xMax = Math.Min(w - 1, x + radius);
                        for (int yy = yMin; yy <= yMax; yy++)
                        {
                            for (int xx = xMin; xx <= xMax; xx++)
                            {
                                result[yy * w + xx] = true;
                            }
                        }
                    }
                }
            }

            return result;
        }
    }
}
