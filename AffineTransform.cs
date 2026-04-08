using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PdfOverlayCompare
{
    /// <summary>
    /// Computes a 2D affine transform from three point pairs.
    /// Maps points from source (PDF B / Site) coordinate space into
    /// the target (PDF A / Box) coordinate space, handling scale,
    /// rotation, translation, and skew.
    /// </summary>
    public class AffineTransform
    {
        // The 2x3 affine matrix coefficients:
        //   x' = A*x + B*y + C
        //   y' = D*x + E*y + F
        private double _a, _b, _c, _d, _e, _f;
        private bool _isValid;

        public bool IsValid => _isValid;

        /// <summary>
        /// The computed scale factor (geometric mean of X and Y scale).
        /// </summary>
        public double Scale { get; private set; }

        /// <summary>
        /// The computed rotation in degrees.
        /// </summary>
        public double RotationDegrees { get; private set; }

        /// <summary>
        /// Translation offset in X.
        /// </summary>
        public double OffsetX => _c;

        /// <summary>
        /// Translation offset in Y.
        /// </summary>
        public double OffsetY => _f;

        /// <summary>
        /// Computes the affine transform from three source points to three target points.
        /// src[i] maps to dst[i].
        /// </summary>
        public bool Compute(PointF[] src, PointF[] dst)
        {
            _isValid = false;

            if (src == null || dst == null || src.Length < 3 || dst.Length < 3)
                return false;

            // Solve the system:
            //   dst.X = A*src.X + B*src.Y + C
            //   dst.Y = D*src.X + E*src.Y + F
            //
            // For three points, this gives two 3x3 linear systems.
            double x0 = src[0].X, y0 = src[0].Y;
            double x1 = src[1].X, y1 = src[1].Y;
            double x2 = src[2].X, y2 = src[2].Y;

            double u0 = dst[0].X, v0 = dst[0].Y;
            double u1 = dst[1].X, v1 = dst[1].Y;
            double u2 = dst[2].X, v2 = dst[2].Y;

            // Determinant of the source matrix
            double det = x0 * (y1 - y2) - x1 * (y0 - y2) + x2 * (y0 - y1);
            if (Math.Abs(det) < 1e-10)
                return false; // Points are collinear

            double invDet = 1.0 / det;

            // Solve for A, B, C (maps to X')
            _a = (u0 * (y1 - y2) - u1 * (y0 - y2) + u2 * (y0 - y1)) * invDet;
            _b = (x0 * (u1 - u2) - x1 * (u0 - u2) + x2 * (u0 - u1)) * invDet;
            _c = (x0 * (y1 * u2 - y2 * u1) - x1 * (y0 * u2 - y2 * u0) + x2 * (y0 * u1 - y1 * u0)) * invDet;

            // Solve for D, E, F (maps to Y')
            _d = (v0 * (y1 - y2) - v1 * (y0 - y2) + v2 * (y0 - y1)) * invDet;
            _e = (x0 * (v1 - v2) - x1 * (v0 - v2) + x2 * (v0 - v1)) * invDet;
            _f = (x0 * (y1 * v2 - y2 * v1) - x1 * (y0 * v2 - y2 * v0) + x2 * (y0 * v1 - y1 * v0)) * invDet;

            // Compute scale and rotation for diagnostics
            double scaleX = Math.Sqrt(_a * _a + _d * _d);
            double scaleY = Math.Sqrt(_b * _b + _e * _e);
            Scale = Math.Sqrt(scaleX * scaleY); // geometric mean
            RotationDegrees = Math.Atan2(_d, _a) * 180.0 / Math.PI;

            _isValid = true;
            return true;
        }

        /// <summary>
        /// Transforms a point from source space to target space.
        /// </summary>
        public PointF TransformPoint(PointF p)
        {
            if (!_isValid)
                return p;

            float x = (float)(_a * p.X + _b * p.Y + _c);
            float y = (float)(_d * p.X + _e * p.Y + _f);
            return new PointF(x, y);
        }

        /// <summary>
        /// Returns a GDI+ Matrix for use with Graphics.Transform.
        /// </summary>
        public Matrix ToGdiMatrix()
        {
            if (!_isValid)
                return new Matrix();

            return new Matrix(
                (float)_a, (float)_d,
                (float)_b, (float)_e,
                (float)_c, (float)_f);
        }

        /// <summary>
        /// Returns a human-readable summary of the transform.
        /// </summary>
        public string GetSummary()
        {
            if (!_isValid)
                return "Transform not computed";

            return string.Format(
                "Scale: {0:F4}x  Rotation: {1:F2}°  Offset: ({2:F1}, {3:F1})",
                Scale, RotationDegrees, OffsetX, OffsetY);
        }
    }
}
