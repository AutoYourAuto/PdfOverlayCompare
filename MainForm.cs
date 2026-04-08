using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PdfOverlayCompare
{
    public class MainForm : Form
    {
        // PDF renderers
        private PdfPageRenderer _rendererA;
        private PdfPageRenderer _rendererB;

        // Rendered page images at working DPI
        private Bitmap _imageA;
        private Bitmap _imageB;
        private Bitmap _overlayImage;

        // Current page indices
        private int _pageIndexA;
        private int _pageIndexB;

        // Render DPI
        private float _renderDpi = 200f;

        // Controls
        private MenuStrip _menuStrip;
        private ToolStrip _toolStrip;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        private SplitContainer _mainSplit;     // Left (A+B) vs Right (overlay)
        private SplitContainer _leftSplit;     // PDF A (top) vs PDF B (bottom)
        private PdfViewPanel _panelA;
        private PdfViewPanel _panelB;
        private PdfViewPanel _panelOverlay;
        private NumericUpDown _nudPageA;
        private NumericUpDown _nudPageB;
        private Label _lblPageCountA;
        private Label _lblPageCountB;
        private NumericUpDown _nudDpi;
        private NumericUpDown _nudTolerance;
        private Button _btnPickPoints;
        private Button _btnClearPoints;
        private Button _btnCompare;
        private Button _btnExport;
        private Label _lblTransformInfo;
        private CheckBox _chkSyncPages;

        // Affine transform
        private AffineTransform _transform = new AffineTransform();

        public MainForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "PDF Overlay Compare";
            Size = new Size(1600, 950);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = SystemIcons.Application;

            // ---- Menu ----
            _menuStrip = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add("Open PDF &A (Reference)...", null, (s, e) => OpenPdfA());
            fileMenu.DropDownItems.Add("Open PDF &B (Compare)...", null, (s, e) => OpenPdfB());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("&Export Overlay as PNG...", null, (s, e) => ExportOverlay());
            fileMenu.DropDownItems.Add("Export All Pages as &Images...", null, (s, e) => ExportAllPages());
            fileMenu.DropDownItems.Add("Export All Pages as &PDF...", null, (s, e) => ExportAllPagesPdf());
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());
            _menuStrip.Items.Add(fileMenu);

            var viewMenu = new ToolStripMenuItem("&View");
            viewMenu.DropDownItems.Add("Zoom to &Fit All", null, (s, e) => ZoomAllToFit());
            viewMenu.DropDownItems.Add("&Reset Points", null, (s, e) => ClearAllPoints());
            _menuStrip.Items.Add(viewMenu);

            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add("&About", null, (s, e) =>
                MessageBox.Show(
                    "PDF Overlay Compare\n\n" +
                    "1. Open two PDF files (A = reference, B = compare)\n" +
                    "2. Enable Pick Points, click 3 matching points on each PDF\n" +
                    "3. Click Compare (F5) to generate the overlay\n\n" +
                    "Red = only in PDF A\nBlue = only in PDF B\nGray = identical\n\n" +
                    "Navigation:\n" +
                    "  Pan: Right-click drag or middle-click drag\n" +
                    "  Zoom: Mouse wheel\n\n" +
                    "Point Picking:\n" +
                    "  Click to place (auto-snaps to corners)\n" +
                    "  Arrow keys: nudge selected point (1 px)\n" +
                    "  Shift+Arrow: fine nudge (0.1 px)\n" +
                    "  Tab: cycle through points",
                    "About", MessageBoxButtons.OK, MessageBoxIcon.Information));
            _menuStrip.Items.Add(helpMenu);
            // MenuStrip will be added AFTER ToolStrip so it docks above it

            // ---- Toolbar ----
            _toolStrip = new ToolStrip();
            _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
            _toolStrip.Items.Add(new ToolStripButton("Open A", null, (s, e) => OpenPdfA()) { ToolTipText = "Open reference PDF" });
            _toolStrip.Items.Add(new ToolStripButton("Open B", null, (s, e) => OpenPdfB()) { ToolTipText = "Open comparison PDF" });
            _toolStrip.Items.Add(new ToolStripSeparator());

            _toolStrip.Items.Add(new ToolStripLabel("Page A:"));
            var nudPageAHost = new ToolStripControlHost(_nudPageA = new NumericUpDown
            {
                Minimum = 1, Maximum = 1, Value = 1, Width = 55,
                Font = new Font("Segoe UI", 9)
            });
            _nudPageA.ValueChanged += (s, e) => LoadPageA((int)_nudPageA.Value - 1);
            _toolStrip.Items.Add(nudPageAHost);
            _toolStrip.Items.Add(new ToolStripControlHost(_lblPageCountA = new Label
            {
                Text = "/ 0", AutoSize = true, Font = new Font("Segoe UI", 9)
            }));

            _toolStrip.Items.Add(new ToolStripLabel("  Page B:"));
            var nudPageBHost = new ToolStripControlHost(_nudPageB = new NumericUpDown
            {
                Minimum = 1, Maximum = 1, Value = 1, Width = 55,
                Font = new Font("Segoe UI", 9)
            });
            _nudPageB.ValueChanged += (s, e) => LoadPageB((int)_nudPageB.Value - 1);
            _toolStrip.Items.Add(nudPageBHost);
            _toolStrip.Items.Add(new ToolStripControlHost(_lblPageCountB = new Label
            {
                Text = "/ 0", AutoSize = true, Font = new Font("Segoe UI", 9)
            }));

            _toolStrip.Items.Add(new ToolStripControlHost(_chkSyncPages = new CheckBox
            {
                Text = "Sync", Checked = true, AutoSize = true,
                Font = new Font("Segoe UI", 9)
            }));

            _toolStrip.Items.Add(new ToolStripSeparator());

            _toolStrip.Items.Add(new ToolStripLabel("DPI:"));
            _toolStrip.Items.Add(new ToolStripControlHost(_nudDpi = new NumericUpDown
            {
                Minimum = 72, Maximum = 600, Value = 200, Increment = 50, Width = 55,
                Font = new Font("Segoe UI", 9)
            }));

            _toolStrip.Items.Add(new ToolStripLabel("  Tolerance:"));
            _toolStrip.Items.Add(new ToolStripControlHost(_nudTolerance = new NumericUpDown
            {
                Minimum = 0, Maximum = 20, Value = 3, Width = 45,
                Font = new Font("Segoe UI", 9)
            }));

            _toolStrip.Items.Add(new ToolStripSeparator());

            var btnPick = new ToolStripButton("Pick Points")
            {
                CheckOnClick = true,
                ToolTipText = "Enable point picking mode (left-click to place points)"
            };
            btnPick.CheckedChanged += (s, e) =>
            {
                bool picking = btnPick.Checked;
                _panelA.PickingEnabled = picking;
                _panelB.PickingEnabled = picking;
                btnPick.Text = picking ? "Picking ON" : "Pick Points";
            };
            _toolStrip.Items.Add(btnPick);

            var btnSnap = new ToolStripButton("Snap: ON")
            {
                CheckOnClick = true,
                Checked = true,
                ToolTipText = "Auto-snap to nearest corner when picking points"
            };
            btnSnap.CheckedChanged += (s, e) =>
            {
                _panelA.SnapEnabled = btnSnap.Checked;
                _panelB.SnapEnabled = btnSnap.Checked;
                btnSnap.Text = btnSnap.Checked ? "Snap: ON" : "Snap: OFF";
            };
            _toolStrip.Items.Add(btnSnap);

            _toolStrip.Items.Add(new ToolStripButton("Clear Pts", null, (s, e) => ClearAllPoints())
            {
                ToolTipText = "Clear all picked calibration points"
            });

            _toolStrip.Items.Add(new ToolStripSeparator());

            var btnCompare = new ToolStripButton("Compare", null, (s, e) => RunCompare())
            {
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ToolTipText = "Generate overlay comparison (F5)"
            };
            _toolStrip.Items.Add(btnCompare);

            _toolStrip.Items.Add(new ToolStripButton("Export", null, (s, e) => ExportOverlay())
            {
                ToolTipText = "Export current overlay as PNG"
            });

            _toolStrip.Items.Add(new ToolStripButton("Export PDF", null, (s, e) => ExportAllPagesPdf())
            {
                ToolTipText = "Export all pages as a single PDF file"
            });

            Controls.Add(_toolStrip);
            Controls.Add(_menuStrip);
            MainMenuStrip = _menuStrip;

            // ---- Status Bar ----
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Ready. Open two PDF files to begin.")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _statusStrip.Items.Add(_statusLabel);

            _lblTransformInfo = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                BackColor = Color.FromArgb(245, 245, 245),
                ForeColor = Color.FromArgb(80, 80, 80),
                Font = new Font("Consolas", 9),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "  Transform: Not computed  |  Pick 3 matching points on each PDF, then click Compare"
            };
            Controls.Add(_lblTransformInfo);
            Controls.Add(_statusStrip);

            // ---- Layout: SplitContainers ----
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(200, 200, 200)
            };

            _leftSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(200, 200, 200)
            };

            _panelA = new PdfViewPanel
            {
                Dock = DockStyle.Fill,
                Label = "PDF A (Reference)",
                BorderStyle = BorderStyle.FixedSingle
            };
            _panelA.PointsChanged += (s, e) => UpdateTransformStatus();

            _panelB = new PdfViewPanel
            {
                Dock = DockStyle.Fill,
                Label = "PDF B (Compare)",
                BorderStyle = BorderStyle.FixedSingle
            };
            _panelB.PointsChanged += (s, e) => UpdateTransformStatus();

            _panelOverlay = new PdfViewPanel
            {
                Dock = DockStyle.Fill,
                Label = "Overlay Result",
                BorderStyle = BorderStyle.FixedSingle
            };

            _leftSplit.Panel1.Controls.Add(_panelA);
            _leftSplit.Panel2.Controls.Add(_panelB);
            _mainSplit.Panel1.Controls.Add(_leftSplit);
            _mainSplit.Panel2.Controls.Add(_panelOverlay);

            Controls.Add(_mainSplit);

            // WinForms docking: last added docks first.
            // Add bottom-docked items, then top-docked, then fill.
            // MenuStrip and ToolStrip auto-dock Top via their own logic.
            // We just need to ensure the fill content is added.
            _mainSplit.BringToFront();

            // Keyboard shortcuts
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;

            // Set equal 50/50 splits after the form has laid out
            Shown += (s, e) =>
            {
                // Main split: left panels vs overlay panel, 50/50
                _mainSplit.SplitterDistance = _mainSplit.Width / 2;
                // Left split: PDF A vs PDF B, equal height
                _leftSplit.SplitterDistance = _leftSplit.Height / 2;
                // Re-fit all panels after layout settles
                _panelA.ZoomToFit();
                _panelB.ZoomToFit();
                _panelOverlay.ZoomToFit();
            };
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                RunCompare();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F && e.Control)
            {
                ZoomAllToFit();
                e.Handled = true;
            }
        }

        #region File Operations

        private void OpenPdfA()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|All Files|*.*",
                Title = "Open PDF A (Reference)"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _rendererA?.Dispose();
                        _rendererA = new PdfPageRenderer(dlg.FileName);
                        _nudPageA.Maximum = _rendererA.PageCount;
                        _nudPageA.Value = 1;
                        _lblPageCountA.Text = "/ " + _rendererA.PageCount;
                        _panelA.Label = "PDF A: " + dlg.FileName;
                        LoadPageA(0);
                        _statusLabel.Text = "Loaded PDF A: " + dlg.FileName;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error loading PDF A:\n" + ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void OpenPdfB()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "PDF Files|*.pdf|All Files|*.*",
                Title = "Open PDF B (Compare)"
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _rendererB?.Dispose();
                        _rendererB = new PdfPageRenderer(dlg.FileName);
                        _nudPageB.Maximum = _rendererB.PageCount;
                        _nudPageB.Value = 1;
                        _lblPageCountB.Text = "/ " + _rendererB.PageCount;
                        _panelB.Label = "PDF B: " + dlg.FileName;
                        LoadPageB(0);
                        _statusLabel.Text = "Loaded PDF B: " + dlg.FileName;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Error loading PDF B:\n" + ex.Message,
                            "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void LoadPageA(int index)
        {
            if (_rendererA == null || index < 0 || index >= _rendererA.PageCount)
                return;

            _pageIndexA = index;
            _imageA?.Dispose();
            _renderDpi = (float)_nudDpi.Value;
            _imageA = _rendererA.RenderPage(index, _renderDpi);
            _panelA.PageImage = _imageA;

            if (_chkSyncPages.Checked && _rendererB != null)
            {
                if (index < _rendererB.PageCount)
                {
                    _nudPageB.Value = index + 1; // triggers LoadPageB
                }
            }

            UpdateTransformStatus();
        }

        private void LoadPageB(int index)
        {
            if (_rendererB == null || index < 0 || index >= _rendererB.PageCount)
                return;

            _pageIndexB = index;
            _imageB?.Dispose();
            _renderDpi = (float)_nudDpi.Value;
            _imageB = _rendererB.RenderPage(index, _renderDpi);
            _panelB.PageImage = _imageB;

            UpdateTransformStatus();
        }

        #endregion

        #region Transform & Compare

        private void UpdateTransformStatus()
        {
            int nA = _panelA.PickedPoints.Count;
            int nB = _panelB.PickedPoints.Count;

            string status = string.Format("  Points: A={0}/3, B={1}/3", nA, nB);

            if (nA >= 3 && nB >= 3)
            {
                // Auto-compute transform
                bool ok = _transform.Compute(
                    _panelB.PickedPoints.ToArray(),
                    _panelA.PickedPoints.ToArray());

                if (ok)
                    status += "  |  " + _transform.GetSummary();
                else
                    status += "  |  Transform failed (points may be collinear)";
            }
            else
            {
                status += "  |  Need 3 points on each PDF to compute transform";
            }

            _lblTransformInfo.Text = status;
        }

        private void RunCompare()
        {
            if (_imageA == null || _imageB == null)
            {
                MessageBox.Show("Please load both PDF A and PDF B first.",
                    "Missing PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int nA = _panelA.PickedPoints.Count;
            int nB = _panelB.PickedPoints.Count;

            AffineTransform xform = null;

            if (nA >= 3 && nB >= 3)
            {
                xform = new AffineTransform();
                bool ok = xform.Compute(
                    _panelB.PickedPoints.ToArray(),
                    _panelA.PickedPoints.ToArray());

                if (!ok)
                {
                    MessageBox.Show(
                        "Could not compute affine transform.\n" +
                        "The points may be collinear. Please pick three non-collinear points.",
                        "Transform Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            else if (nA == 0 && nB == 0)
            {
                // No points picked - do direct overlay (scale B to match A size)
                var result = DialogResult;
                // Just proceed without transform
                xform = null;
            }
            else
            {
                MessageBox.Show(
                    string.Format("Need 3 points on each PDF.\nCurrently: A={0}, B={1}", nA, nB),
                    "Insufficient Points", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Cursor = Cursors.WaitCursor;
            _statusLabel.Text = "Computing overlay...";
            Application.DoEvents();

            try
            {
                OverlayEngine.MergeTolerance = (int)_nudTolerance.Value;

                // If no transform, create a simple scale transform to match sizes
                if (xform == null)
                {
                    xform = new AffineTransform();
                    // Create identity-like transform that scales B to A's size
                    float sx = (float)_imageA.Width / _imageB.Width;
                    float sy = (float)_imageA.Height / _imageB.Height;
                    // Use three corner points to define the scale
                    var srcPts = new PointF[]
                    {
                        new PointF(0, 0),
                        new PointF(_imageB.Width, 0),
                        new PointF(0, _imageB.Height)
                    };
                    var dstPts = new PointF[]
                    {
                        new PointF(0, 0),
                        new PointF(_imageA.Width, 0),
                        new PointF(0, _imageA.Height)
                    };
                    xform.Compute(srcPts, dstPts);
                }

                _overlayImage?.Dispose();
                _overlayImage = OverlayEngine.CreateOverlay(_imageA, _imageB, xform);
                _panelOverlay.PageImage = _overlayImage;

                _statusLabel.Text = string.Format("Overlay generated. {0}", xform.GetSummary());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error generating overlay:\n" + ex.Message,
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        #endregion

        #region Export

        private void ExportOverlay()
        {
            if (_overlayImage == null)
            {
                MessageBox.Show("No overlay to export. Run Compare first.",
                    "No Overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg|BMP Image|*.bmp",
                Title = "Export Overlay Image",
                FileName = string.Format("Overlay_Page{0}_{1}.png", _pageIndexA + 1, _pageIndexB + 1)
            })
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    ImageFormat fmt = ImageFormat.Png;
                    string ext = Path.GetExtension(dlg.FileName).ToLower();
                    if (ext == ".jpg" || ext == ".jpeg") fmt = ImageFormat.Jpeg;
                    if (ext == ".bmp") fmt = ImageFormat.Bmp;

                    _overlayImage.Save(dlg.FileName, fmt);
                    _statusLabel.Text = "Exported: " + dlg.FileName;
                }
            }
        }

        private void ExportAllPages()
        {
            if (_rendererA == null || _rendererB == null)
            {
                MessageBox.Show("Please load both PDFs first.",
                    "Missing PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int nA = _panelA.PickedPoints.Count;
            int nB = _panelB.PickedPoints.Count;

            if (nA < 3 || nB < 3)
            {
                var ans = MessageBox.Show(
                    "No calibration points set. Export will use simple scaling.\nContinue?",
                    "No Calibration", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }

            using (var dlg = new FolderBrowserDialog
            {
                Description = "Select output folder for overlay images"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                Cursor = Cursors.WaitCursor;
                int totalPages = Math.Min(_rendererA.PageCount, _rendererB.PageCount);
                float dpi = (float)_nudDpi.Value;

                // Build transform from current points (or use simple scaling)
                AffineTransform xform = new AffineTransform();
                if (nA >= 3 && nB >= 3)
                {
                    xform.Compute(
                        _panelB.PickedPoints.ToArray(),
                        _panelA.PickedPoints.ToArray());
                }

                OverlayEngine.MergeTolerance = (int)_nudTolerance.Value;

                for (int pg = 0; pg < totalPages; pg++)
                {
                    _statusLabel.Text = string.Format("Exporting page {0} of {1}...", pg + 1, totalPages);
                    Application.DoEvents();

                    using (var imgA = _rendererA.RenderPage(pg, dpi))
                    using (var imgB = _rendererB.RenderPage(pg, dpi))
                    {
                        // If no calibration, create per-page scale transform
                        AffineTransform pageXform = xform;
                        if (!xform.IsValid)
                        {
                            pageXform = new AffineTransform();
                            var srcPts = new PointF[]
                            {
                                new PointF(0, 0),
                                new PointF(imgB.Width, 0),
                                new PointF(0, imgB.Height)
                            };
                            var dstPts = new PointF[]
                            {
                                new PointF(0, 0),
                                new PointF(imgA.Width, 0),
                                new PointF(0, imgA.Height)
                            };
                            pageXform.Compute(srcPts, dstPts);
                        }

                        using (var overlay = OverlayEngine.CreateOverlay(imgA, imgB, pageXform))
                        {
                            string outPath = Path.Combine(dlg.SelectedPath,
                                string.Format("Overlay_Page{0:D3}.png", pg + 1));
                            overlay.Save(outPath, ImageFormat.Png);
                        }
                    }
                }

                Cursor = Cursors.Default;
                _statusLabel.Text = string.Format("Exported {0} pages to {1}", totalPages, dlg.SelectedPath);
                MessageBox.Show(
                    string.Format("Exported {0} overlay pages.", totalPages),
                    "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void ExportAllPagesPdf()
        {
            if (_rendererA == null || _rendererB == null)
            {
                MessageBox.Show("Please load both PDFs first.",
                    "Missing PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int nA = _panelA.PickedPoints.Count;
            int nB = _panelB.PickedPoints.Count;

            if (nA < 3 || nB < 3)
            {
                var ans = MessageBox.Show(
                    "No calibration points set. Export will use simple scaling.\nContinue?",
                    "No Calibration", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (ans != DialogResult.Yes) return;
            }

            using (var dlg = new SaveFileDialog
            {
                Filter = "PDF Files|*.pdf",
                Title = "Export All Overlay Pages as PDF",
                FileName = "Overlay_Comparison.pdf"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                Cursor = Cursors.WaitCursor;
                int totalPages = Math.Min(_rendererA.PageCount, _rendererB.PageCount);
                float dpi = (float)_nudDpi.Value;

                // Build transform from current points (or use simple scaling)
                AffineTransform xform = new AffineTransform();
                if (nA >= 3 && nB >= 3)
                {
                    xform.Compute(
                        _panelB.PickedPoints.ToArray(),
                        _panelA.PickedPoints.ToArray());
                }

                OverlayEngine.MergeTolerance = (int)_nudTolerance.Value;

                // Collect overlay images into a temp folder, then merge into PDF
                string tempDir = Path.Combine(Path.GetTempPath(), "PdfOverlayCompare_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                try
                {
                    var tempFiles = new List<string>();

                    for (int pg = 0; pg < totalPages; pg++)
                    {
                        _statusLabel.Text = string.Format("Rendering page {0} of {1}...", pg + 1, totalPages);
                        Application.DoEvents();

                        using (var imgA = _rendererA.RenderPage(pg, dpi))
                        using (var imgB = _rendererB.RenderPage(pg, dpi))
                        {
                            AffineTransform pageXform = xform;
                            if (!xform.IsValid)
                            {
                                pageXform = new AffineTransform();
                                var srcPts = new PointF[]
                                {
                                    new PointF(0, 0),
                                    new PointF(imgB.Width, 0),
                                    new PointF(0, imgB.Height)
                                };
                                var dstPts = new PointF[]
                                {
                                    new PointF(0, 0),
                                    new PointF(imgA.Width, 0),
                                    new PointF(0, imgA.Height)
                                };
                                pageXform.Compute(srcPts, dstPts);
                            }

                            using (var overlay = OverlayEngine.CreateOverlay(imgA, imgB, pageXform))
                            {
                                // Save as JPEG for smaller PDF size (PNG makes huge PDFs)
                                string tempPath = Path.Combine(tempDir, string.Format("page_{0:D4}.jpg", pg));
                                var jpegEncoder = GetJpegEncoder();
                                var encoderParams = new EncoderParameters(1);
                                encoderParams.Param[0] = new EncoderParameter(
                                    System.Drawing.Imaging.Encoder.Quality, 92L);
                                overlay.Save(tempPath, jpegEncoder, encoderParams);
                                tempFiles.Add(tempPath);
                            }
                        }
                    }

                    // Build PDF from the image files
                    _statusLabel.Text = "Compiling PDF...";
                    Application.DoEvents();

                    PdfBuilder.CreatePdfFromImages(tempFiles, dlg.FileName, dpi);

                    Cursor = Cursors.Default;
                    _statusLabel.Text = "Exported PDF: " + dlg.FileName;
                    MessageBox.Show(
                        string.Format("Exported {0} overlay pages to:\n{1}", totalPages, dlg.FileName),
                        "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    MessageBox.Show("Error exporting PDF:\n" + ex.Message,
                        "Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    // Clean up temp files
                    try
                    {
                        if (Directory.Exists(tempDir))
                            Directory.Delete(tempDir, true);
                    }
                    catch { /* ignore cleanup errors */ }
                }
            }
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var codec in ImageCodecInfo.GetImageEncoders())
            {
                if (codec.MimeType == "image/jpeg")
                    return codec;
            }
            return null;
        }

        #endregion

        #region Helpers

        private void ZoomAllToFit()
        {
            _panelA.ZoomToFit();
            _panelB.ZoomToFit();
            _panelOverlay.ZoomToFit();
        }

        private void ClearAllPoints()
        {
            _panelA.ClearPoints();
            _panelB.ClearPoints();
            UpdateTransformStatus();
        }

        #endregion

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _rendererA?.Dispose();
            _rendererB?.Dispose();
            _imageA?.Dispose();
            _imageB?.Dispose();
            _overlayImage?.Dispose();
        }
    }
}
