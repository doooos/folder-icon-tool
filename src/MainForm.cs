using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace FolderIconTool
{
    public sealed class MainForm : Form
    {
        private readonly string backupRoot;
        private readonly TextBox folderPathBox, imagePathBox;
        private readonly Button chooseFolderButton, chooseImageButton, applyButton, restoreButton, resetButton, customCropButton;
        private readonly TrackBar zoomBar;
        private readonly Label zoomLabel, statusLabel, modeHelpLabel;
        private readonly CheckBox fitWholeImageCheckBox, squareCropCheckBox;
        private readonly PictureBox iconPreview;
        private readonly CropPreview cropPreview;
        private readonly ToolTip toolTip = new ToolTip();
        private string selectedFolder, selectedImage;
        private bool busy;
        private bool updatingCropControls;

        public MainForm(string backupRoot)
        {
            if (String.IsNullOrWhiteSpace(backupRoot))
                throw new ArgumentException("请指定备份目录。", "backupRoot");
            this.backupRoot = Path.GetFullPath(backupRoot);
            Text = "全能自定义文件夹图标";
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1060, 790);
            MinimumSize = new Size(900, 740);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 248, 251);
            AllowDrop = true;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(22, 16, 22, 14);
            layout.ColumnCount = 1;
            layout.RowCount = 6;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            Controls.Add(layout);
            TableLayoutPanel introduction = new TableLayoutPanel();
            introduction.Dock = DockStyle.Fill;
            introduction.ColumnCount = 1;
            introduction.RowCount = 2;
            introduction.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            introduction.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label title = MakeLabel("全能自定义文件夹图标");
            title.Font = new Font(Font.FontFamily, 16F, FontStyle.Bold);
            introduction.Controls.Add(title, 0, 0);
            introduction.Controls.Add(MakeLabel("选择文件夹 → 选择图片 → 应用。自动转换格式和尺寸，可直接使用。"), 0, 1);
            layout.Controls.Add(introduction, 0, 0);
            folderPathBox = MakePathBox("尚未选择文件夹");
            imagePathBox = MakePathBox("尚未选择照片，也可以拖入照片");
            chooseFolderButton = MakeButton("选择文件夹…");
            chooseImageButton = MakeButton("选择照片…");
            layout.Controls.Add(MakePathRow("目标文件夹", folderPathBox, chooseFolderButton), 0, 1);
            layout.Controls.Add(MakePathRow("照片文件", imagePathBox, chooseImageButton), 0, 2);

            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.Margin = new Padding(0, 12, 0, 0);
            content.ColumnCount = 2;
            content.RowCount = 1;
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 242F));
            cropPreview = new CropPreview();
            cropPreview.Dock = DockStyle.Fill;
            cropPreview.Margin = new Padding(0, 0, 16, 0);
            content.Controls.Add(cropPreview, 0, 0);
            TableLayoutPanel sidebar = new TableLayoutPanel();
            sidebar.Dock = DockStyle.Fill;
            sidebar.BackColor = Color.White;
            sidebar.Padding = new Padding(14, 12, 14, 12);
            sidebar.ColumnCount = 1;
            sidebar.RowCount = 9;
            sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            foreach (float height in new float[] { 34, 38, 32, 28, 32, 36, 60 })
                sidebar.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            sidebar.Controls.Add(MakeLabel("实际图标预览 · 256 × 256"), 0, 0);
            iconPreview = new PictureBox();
            iconPreview.Dock = DockStyle.Fill;
            iconPreview.SizeMode = PictureBoxSizeMode.Zoom;
            iconPreview.BackColor = Color.FromArgb(241, 244, 248);
            iconPreview.Margin = new Padding(8, 3, 8, 3);
            sidebar.Controls.Add(iconPreview, 0, 1);
            fitWholeImageCheckBox = new CheckBox();
            fitWholeImageCheckBox.Text = "完整图片（自动适配）";
            fitWholeImageCheckBox.Checked = true;
            fitWholeImageCheckBox.Dock = DockStyle.Fill;
            fitWholeImageCheckBox.Margin = Padding.Empty;
            sidebar.Controls.Add(fitWholeImageCheckBox, 0, 2);
            customCropButton = MakeButton("自定义裁剪 / 重新框选");
            customCropButton.Dock = DockStyle.Fill;
            sidebar.Controls.Add(customCropButton, 0, 3);
            squareCropCheckBox = new CheckBox();
            squareCropCheckBox.Text = "锁定正方形（1:1）";
            squareCropCheckBox.Dock = DockStyle.Fill;
            squareCropCheckBox.Margin = Padding.Empty;
            sidebar.Controls.Add(squareCropCheckBox, 0, 4);
            zoomLabel = MakeLabel("自动适配 · 不裁剪");
            sidebar.Controls.Add(zoomLabel, 0, 5);
            zoomBar = new TrackBar();
            zoomBar.Dock = DockStyle.Fill;
            zoomBar.Minimum = 100;
            zoomBar.Maximum = 800;
            zoomBar.Value = 100;
            zoomBar.SmallChange = 10;
            zoomBar.LargeChange = 50;
            zoomBar.TickStyle = TickStyle.None;
            sidebar.Controls.Add(zoomBar, 0, 6);
            resetButton = MakeButton("重置裁剪");
            resetButton.Dock = DockStyle.Fill;
            sidebar.Controls.Add(resetButton, 0, 7);
            modeHelpLabel = MakeLabel("保留整张图片，无需裁剪。\n点击“自定义裁剪”可自己框选。");
            modeHelpLabel.Font = new Font(Font.FontFamily, 9F);
            sidebar.Controls.Add(modeHelpLabel, 0, 8);
            content.Controls.Add(sidebar, 1, 0);
            layout.Controls.Add(content, 0, 3);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 12, 0, 0);
            applyButton = MakeButton("应用到文件夹");
            applyButton.Size = new Size(184, 42);
            applyButton.BackColor = Color.FromArgb(40, 99, 204);
            applyButton.ForeColor = Color.White;
            applyButton.FlatAppearance.BorderSize = 0;
            restoreButton = MakeButton("撤销上次替换");
            restoreButton.Size = new Size(164, 42);
            actions.Controls.Add(applyButton);
            actions.Controls.Add(restoreButton);
            layout.Controls.Add(actions, 0, 4);
            statusLabel = MakeLabel("请先选择目标文件夹和照片。也可拖入一个文件夹或一张照片。");
            statusLabel.AutoEllipsis = true;
            layout.Controls.Add(statusLabel, 0, 5);

            chooseFolderButton.Click += delegate { ChooseFolder(); };
            chooseImageButton.Click += delegate { ChooseImage(); };
            applyButton.Click += delegate { ApplySelection(); };
            restoreButton.Click += delegate { RestoreSelection(); };
            fitWholeImageCheckBox.CheckedChanged += delegate
            {
                UpdateButtons();
                UpdateIconPreview();
                if (cropPreview.HasImage)
                    SetStatus(fitWholeImageCheckBox.Checked ? "已自动适配，保留整张图片。选择目标文件夹后即可应用。" : "拖动框内移动选区，拖动边角调整大小；也可点击“重新框选”。", false);
            };
            customCropButton.Click += delegate
            {
                fitWholeImageCheckBox.Checked = false;
                cropPreview.BeginSelection();
                SetStatus("在图片上按住鼠标左键，画出你想保留的范围。按 Esc 可取消。", false);
            };
            squareCropCheckBox.CheckedChanged += delegate { cropPreview.LockSquare = squareCropCheckBox.Checked; };
            resetButton.Click += delegate { cropPreview.ResetCrop(); };
            zoomBar.ValueChanged += delegate
            {
                if (!updatingCropControls && !fitWholeImageCheckBox.Checked)
                    cropPreview.SetZoom(zoomBar.Value);
            };
            cropPreview.CropChanged += delegate { UpdateCropControls(); UpdateIconPreview(); };
            cropPreview.ZoomRequested += delegate(int direction)
            {
                zoomBar.Value = Math.Max(zoomBar.Minimum, Math.Min(zoomBar.Maximum, zoomBar.Value + direction * 25));
            };
            DragEnter += HandleDragEnter;
            DragDrop += HandleDragDrop;
            cropPreview.AllowDrop = true;
            cropPreview.DragEnter += HandleDragEnter;
            cropPreview.DragDrop += HandleDragDrop;
            FormClosing += delegate(object sender, FormClosingEventArgs eventArgs)
            {
                if (busy)
                {
                    eventArgs.Cancel = true;
                    SetStatus("正在完成操作，请稍候再关闭。", false);
                }
            };
            UpdateButtons();
        }

        public void LoadSelection(string folder, string imagePath)
        {
            if (busy)
                throw new InvalidOperationException("正在处理上一次操作，请稍候。");
            string newFolder = selectedFolder;
            if (!String.IsNullOrWhiteSpace(folder))
            {
                newFolder = Path.GetFullPath(folder);
                if (!Directory.Exists(newFolder))
                    throw new DirectoryNotFoundException("目标文件夹不存在。");
            }
            Bitmap newImage = null;
            string newImagePath = selectedImage;
            if (!String.IsNullOrWhiteSpace(imagePath))
            {
                newImagePath = Path.GetFullPath(imagePath);
                if (!File.Exists(newImagePath))
                    throw new FileNotFoundException("照片文件不存在。", newImagePath);
                newImage = IconEngine.LoadOrientedImage(newImagePath);
            }
            selectedFolder = newFolder;
            folderPathBox.Text = selectedFolder ?? "尚未选择文件夹";
            toolTip.SetToolTip(folderPathBox, selectedFolder);
            if (newImage != null)
            {
                selectedImage = newImagePath;
                imagePathBox.Text = selectedImage;
                toolTip.SetToolTip(imagePathBox, selectedImage);
                cropPreview.SetImage(newImage);
            }
            UpdateButtons();
            if (!cropPreview.HasImage)
                SetStatus("文件夹已选择。请选择照片，或点击“撤销上次替换”。", false);
            else if (String.IsNullOrEmpty(selectedFolder))
                SetStatus("照片已载入。请选择目标文件夹，再点击应用。", false);
            else
                SetStatus(fitWholeImageCheckBox.Checked ? "图片已自动适配。点击“应用到文件夹”即可使用，原图片不会被修改。" : "可选裁剪预览已就绪。点击“应用到文件夹”才会替换。", false);
        }

        private static Label MakeLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Color.FromArgb(64, 81, 103);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Margin = Padding.Empty;
            return label;
        }

        private static TextBox MakePathBox(string placeholder)
        {
            TextBox box = new TextBox();
            box.Text = placeholder;
            box.ReadOnly = true;
            box.Dock = DockStyle.Fill;
            box.BackColor = Color.White;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Margin = new Padding(0, 8, 12, 6);
            return box;
        }

        private static Button MakeButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 213, 224);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(40, 57, 79);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.Margin = new Padding(0, 3, 12, 3);
            return button;
        }

        private static TableLayoutPanel MakePathRow(string title, TextBox box, Button button)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.Margin = Padding.Empty;
            row.ColumnCount = 3;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 144F));
            row.Controls.Add(MakeLabel(title), 0, 0);
            row.Controls.Add(box, 1, 0);
            button.Dock = DockStyle.Fill;
            row.Controls.Add(button, 2, 0);
            return row;
        }

        private void ChooseFolder()
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择要更换头像的文件夹";
                dialog.ShowNewFolderButton = false;
                if (!String.IsNullOrEmpty(selectedFolder))
                    dialog.SelectedPath = selectedFolder;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    TryLoadSelection(dialog.SelectedPath, null);
            }
        }

        private void ChooseImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择头像照片（不会修改原文件）";
                dialog.Filter = "照片文件|*.jpg;*.jpeg;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.webp;*.heic;*.heif;*.avif;*.wdp;*.jxr;*.hdp|所有文件|*.*";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    TryLoadSelection(null, dialog.FileName);
            }
        }

        private void TryLoadSelection(string folder, string imagePath)
        {
            try { LoadSelection(folder, imagePath); }
            catch (Exception error) { ShowError("无法载入选择", error); }
        }

        private void HandleDragEnter(object sender, DragEventArgs eventArgs)
        {
            eventArgs.Effect = !busy && eventArgs.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void HandleDragDrop(object sender, DragEventArgs eventArgs)
        {
            if (busy)
                return;
            string[] paths = eventArgs.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length != 1)
            {
                SetStatus("每次请拖入一个文件夹或一张照片。", true);
                return;
            }
            if (Directory.Exists(paths[0]))
                TryLoadSelection(paths[0], null);
            else
                TryLoadSelection(null, paths[0]);
        }

        private void UpdateIconPreview()
        {
            if (!cropPreview.HasImage)
                return;
            Rectangle? crop = fitWholeImageCheckBox.Checked ? (Rectangle?)null : cropPreview.CropRectangle;
            Bitmap preview = IconEngine.RenderIconImage(cropPreview.SourceImage, crop, fitWholeImageCheckBox.Checked);
            Image previous = iconPreview.Image;
            iconPreview.Image = preview;
            if (previous != null)
                previous.Dispose();
        }

        private void ApplySelection()
        {
            if (busy || String.IsNullOrEmpty(selectedFolder) || !cropPreview.HasImage)
                return;
            string folder = selectedFolder;
            string imagePath = selectedImage;
            bool fitWholeImage = fitWholeImageCheckBox.Checked;
            Rectangle? crop = fitWholeImage ? (Rectangle?)null : cropPreview.CropRectangle;
            RunOperation("正在应用头像，请稍候…", delegate { return IconEngine.Apply(folder, imagePath, crop, backupRoot, fitWholeImage); });
        }

        private void RestoreSelection()
        {
            if (busy || String.IsNullOrEmpty(selectedFolder))
                return;
            string folder = selectedFolder;
            RunOperation("正在撤销上次替换，请稍候…", delegate { return IconEngine.RestoreLast(folder, backupRoot); });
        }

        private void RunOperation(string progress, Func<OperationResult> operation)
        {
            busy = true;
            UpdateButtons();
            SetStatus(progress, false);
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object sender, DoWorkEventArgs eventArgs) { eventArgs.Result = operation(); };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs eventArgs)
            {
                busy = false;
                UpdateButtons();
                worker.Dispose();
                if (eventArgs.Error != null)
                {
                    ShowError("操作未完成", eventArgs.Error);
                    return;
                }
                OperationResult result = eventArgs.Result as OperationResult;
                if (result == null || !result.Success)
                {
                    SetStatus(result == null ? "操作未完成，未收到有效结果。" : result.Message, true);
                    return;
                }
                string message = String.IsNullOrWhiteSpace(result.Message) ? "操作已完成。" : result.Message;
                if (!String.IsNullOrWhiteSpace(result.BackupDirectory))
                    message += "\n备份：" + result.BackupDirectory;
                SetStatus(message, false);
            };
            worker.RunWorkerAsync();
        }

        private void UpdateButtons()
        {
            chooseFolderButton.Enabled = !busy;
            chooseImageButton.Enabled = !busy;
            fitWholeImageCheckBox.Enabled = !busy;
            bool cropEnabled = !busy && cropPreview.HasImage && !fitWholeImageCheckBox.Checked;
            cropPreview.FitWholeImage = fitWholeImageCheckBox.Checked;
            cropPreview.Enabled = cropEnabled;
            customCropButton.Enabled = !busy && cropPreview.HasImage;
            squareCropCheckBox.Enabled = cropEnabled;
            zoomBar.Enabled = cropEnabled;
            resetButton.Enabled = cropEnabled;
            UpdateCropControls();
            modeHelpLabel.Text = fitWholeImageCheckBox.Checked ? "保留整张图片，无需裁剪。\n点击“自定义裁剪”可自己框选。" : "拖边角改大小，拖框内移动。\n矩形选区自动留透明边，不拉伸。\n点击应用才会替换文件夹图标。";
            applyButton.Enabled = !busy && !String.IsNullOrEmpty(selectedFolder) && cropPreview.HasImage;
            restoreButton.Enabled = !busy && !String.IsNullOrEmpty(selectedFolder);
            UseWaitCursor = busy;
        }

        private void UpdateCropControls()
        {
            updatingCropControls = true;
            try
            {
                if (cropPreview.HasImage)
                    zoomBar.Value = Math.Max(zoomBar.Minimum, Math.Min(zoomBar.Maximum, cropPreview.ZoomPercent));
                Rectangle area = cropPreview.CropRectangle;
                zoomLabel.Text = fitWholeImageCheckBox.Checked ? "自动适配 · 不裁剪" :
                    "选区：" + area.Width + " × " + area.Height + " 像素";
            }
            finally { updatingCropControls = false; }
        }

        private void SetStatus(string message, bool error)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = error ? Color.FromArgb(172, 51, 51) : Color.FromArgb(70, 91, 115);
            toolTip.SetToolTip(statusLabel, message);
        }

        private void ShowError(string title, Exception error)
        {
            string message = error is OutOfMemoryException ? "无法读取这张照片。文件可能损坏、缺少对应解码器，或可用内存不足。" : error.Message;
            SetStatus(title + "：" + message, true);
            MessageBox.Show(this, message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (iconPreview != null)
                {
                    Image previous = iconPreview.Image;
                    iconPreview.Image = null;
                    if (previous != null)
                        previous.Dispose();
                }
                toolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class CropPreview : Control
        {
            private enum DragMode { None, Draw, Move, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }
            private Bitmap sourceImage;
            private Rectangle crop;
            private Point dragStart;
            private Rectangle dragCrop;
            private DragMode dragMode;
            private bool selectionArmed;
            private bool lockSquare;
            private bool fitWholeImage = true;
            public event EventHandler CropChanged;
            public event Action<int> ZoomRequested;

            public CropPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(228, 233, 240);
                Cursor = Cursors.Default;
                TabStop = true;
            }

            public bool HasImage { get { return sourceImage != null; } }
            public Bitmap SourceImage { get { return sourceImage; } }
            public Rectangle CropRectangle { get { return crop; } }
            public int ZoomPercent
            {
                get { return HasImage ? (int)Math.Min(Int32.MaxValue, Math.Round(100.0 * Math.Min((double)sourceImage.Width / crop.Width, (double)sourceImage.Height / crop.Height))) : 100; }
            }
            public bool LockSquare
            {
                get { return lockSquare; }
                set
                {
                    lockSquare = value;
                    if (value && HasImage)
                    {
                        int side = Math.Min(crop.Width, crop.Height);
                        SetCrop(new Rectangle(crop.X + (crop.Width - side) / 2, crop.Y + (crop.Height - side) / 2, side, side));
                    }
                }
            }
            public bool FitWholeImage
            {
                get { return fitWholeImage; }
                set
                {
                    if (fitWholeImage == value)
                        return;
                    fitWholeImage = value;
                    dragMode = DragMode.None;
                    selectionArmed = false;
                    if (Capture)
                        Capture = false;
                    Cursor = value ? Cursors.Default : Cursors.Cross;
                    Invalidate();
                }
            }

            public void SetImage(Bitmap image)
            {
                Bitmap previous = sourceImage;
                sourceImage = image;
                if (previous != null)
                    previous.Dispose();
                ResetCrop();
            }

            public void ResetCrop()
            {
                if (!HasImage)
                    return;
                selectionArmed = false;
                dragMode = DragMode.None;
                Capture = false;
                int side = Math.Min(sourceImage.Width, sourceImage.Height);
                SetCrop(new Rectangle((sourceImage.Width - side) / 2, (sourceImage.Height - side) / 2, side, side));
            }

            public void BeginSelection()
            {
                selectionArmed = true;
                Cursor = Cursors.Cross;
                Focus();
                Invalidate();
            }

            public void SetZoom(int percent)
            {
                if (!HasImage || percent < 100)
                    return;
                double factor = Math.Min((double)sourceImage.Width / crop.Width, (double)sourceImage.Height / crop.Height) * 100.0 / percent;
                int width = Math.Max(1, (int)Math.Round(crop.Width * factor));
                int height = Math.Max(1, (int)Math.Round(crop.Height * factor));
                SetCrop(new Rectangle((int)Math.Round(crop.X + (crop.Width - width) / 2.0),
                    (int)Math.Round(crop.Y + (crop.Height - height) / 2.0), width, height));
            }

            private void SetCrop(Rectangle value)
            {
                int width = Math.Max(1, Math.Min(sourceImage.Width, value.Width));
                int height = Math.Max(1, Math.Min(sourceImage.Height, value.Height));
                if (lockSquare) width = height = Math.Min(width, height);
                crop = new Rectangle(Math.Max(0, Math.Min(sourceImage.Width - width, value.X)),
                    Math.Max(0, Math.Min(sourceImage.Height - height, value.Y)), width, height);
                Invalidate();
                EventHandler handler = CropChanged;
                if (handler != null)
                    handler(this, EventArgs.Empty);
            }

            private RectangleF ImageBounds()
            {
                float width = Math.Max(1, ClientSize.Width - 24);
                float height = Math.Max(1, ClientSize.Height - 24);
                float scale = Math.Min(width / sourceImage.Width, height / sourceImage.Height);
                float drawnWidth = sourceImage.Width * scale;
                float drawnHeight = sourceImage.Height * scale;
                return new RectangleF((ClientSize.Width - drawnWidth) / 2, (ClientSize.Height - drawnHeight) / 2, drawnWidth, drawnHeight);
            }

            protected override void OnPaint(PaintEventArgs eventArgs)
            {
                base.OnPaint(eventArgs);
                Graphics graphics = eventArgs.Graphics;
                if (!HasImage)
                {
                    TextRenderer.DrawText(graphics, "选择或拖入一张照片\n\n点击“自定义裁剪”，自己框选想保留的画面", Font, ClientRectangle, Color.FromArgb(100, 114, 133), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                    return;
                }
                RectangleF imageBounds = ImageBounds();
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(sourceImage, imageBounds);
                if (fitWholeImage)
                    return;
                float scale = imageBounds.Width / sourceImage.Width;
                RectangleF frame = new RectangleF(imageBounds.X + crop.X * scale, imageBounds.Y + crop.Y * scale, crop.Width * scale, crop.Height * scale);
                using (Brush shade = new SolidBrush(Color.FromArgb(145, 14, 24, 39)))
                {
                    graphics.FillRectangle(shade, imageBounds.X, imageBounds.Y, imageBounds.Width, Math.Max(0, frame.Top - imageBounds.Top));
                    graphics.FillRectangle(shade, imageBounds.X, frame.Bottom, imageBounds.Width, Math.Max(0, imageBounds.Bottom - frame.Bottom));
                    graphics.FillRectangle(shade, imageBounds.X, frame.Top, Math.Max(0, frame.Left - imageBounds.Left), frame.Height);
                    graphics.FillRectangle(shade, frame.Right, frame.Top, Math.Max(0, imageBounds.Right - frame.Right), frame.Height);
                }
                using (Pen outline = new Pen(Color.FromArgb(90, 0, 0, 0), 4F))
                    graphics.DrawRectangle(outline, frame.X, frame.Y, frame.Width, frame.Height);
                using (Pen outline = new Pen(Color.White, 2F))
                    graphics.DrawRectangle(outline, frame.X, frame.Y, frame.Width, frame.Height);
                using (Pen guide = new Pen(Color.FromArgb(110, 255, 255, 255), 1F))
                {
                    for (int division = 1; division <= 2; division++)
                    {
                        float x = frame.Left + frame.Width * division / 3F;
                        float y = frame.Top + frame.Height * division / 3F;
                        graphics.DrawLine(guide, x, frame.Top, x, frame.Bottom);
                        graphics.DrawLine(guide, frame.Left, y, frame.Right, y);
                    }
                }
                using (Brush handleBrush = new SolidBrush(Color.White))
                using (Pen handleBorder = new Pen(Color.FromArgb(40, 99, 204), 1F))
                {
                    foreach (PointF point in HandlePoints(frame))
                    {
                        float radius = HandleRadius;
                        graphics.FillRectangle(handleBrush, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                        graphics.DrawRectangle(handleBorder, point.X - radius, point.Y - radius, radius * 2, radius * 2);
                    }
                }
            }

            private float HandleRadius { get { return Math.Max(5F, Font.Height / 3F); } }

            private static PointF[] HandlePoints(RectangleF frame)
            {
                float cx = frame.Left + frame.Width / 2, cy = frame.Top + frame.Height / 2;
                return new PointF[] {
                    new PointF(frame.Left, frame.Top), new PointF(cx, frame.Top), new PointF(frame.Right, frame.Top),
                    new PointF(frame.Right, cy), new PointF(frame.Right, frame.Bottom), new PointF(cx, frame.Bottom),
                    new PointF(frame.Left, frame.Bottom), new PointF(frame.Left, cy) };
            }

            private DragMode HitTest(Point location)
            {
                RectangleF bounds = ImageBounds();
                float scale = bounds.Width / sourceImage.Width;
                RectangleF frame = new RectangleF(bounds.X + crop.X * scale, bounds.Y + crop.Y * scale, crop.Width * scale, crop.Height * scale);
                PointF[] points = HandlePoints(frame);
                for (int i = 0; i < points.Length; i++)
                    if (Math.Abs(location.X - points[i].X) <= HandleRadius + 3 && Math.Abs(location.Y - points[i].Y) <= HandleRadius + 3)
                        return (DragMode)((int)DragMode.TopLeft + i);
                return frame.Contains(location) ? DragMode.Move : DragMode.None;
            }

            private Point ImagePoint(Point location)
            {
                RectangleF bounds = ImageBounds();
                float scale = bounds.Width / sourceImage.Width;
                return new Point(Math.Max(0, Math.Min(sourceImage.Width, (int)Math.Round((location.X - bounds.X) / scale))),
                    Math.Max(0, Math.Min(sourceImage.Height, (int)Math.Round((location.Y - bounds.Y) / scale))));
            }

            private static Cursor CursorFor(DragMode mode)
            {
                switch (mode)
                {
                    case DragMode.Move: return Cursors.SizeAll;
                    case DragMode.TopLeft: case DragMode.BottomRight: return Cursors.SizeNWSE;
                    case DragMode.TopRight: case DragMode.BottomLeft: return Cursors.SizeNESW;
                    case DragMode.Top: case DragMode.Bottom: return Cursors.SizeNS;
                    case DragMode.Left: case DragMode.Right: return Cursors.SizeWE;
                    default: return Cursors.Cross;
                }
            }

            protected override void OnMouseDown(MouseEventArgs eventArgs)
            {
                base.OnMouseDown(eventArgs);
                if (!HasImage || fitWholeImage || eventArgs.Button != MouseButtons.Left)
                    return;
                Focus();
                RectangleF bounds = ImageBounds();
                DragMode mode = selectionArmed ? DragMode.None : HitTest(eventArgs.Location);
                if (mode == DragMode.None && !bounds.Contains(eventArgs.Location))
                    return;
                dragStart = ImagePoint(eventArgs.Location);
                dragCrop = crop;
                Capture = true;
                dragMode = mode == DragMode.None ? DragMode.Draw : mode;
                selectionArmed = false;
                Cursor = CursorFor(dragMode);
            }

            protected override void OnMouseMove(MouseEventArgs eventArgs)
            {
                base.OnMouseMove(eventArgs);
                if (!HasImage || fitWholeImage)
                    return;
                if (dragMode == DragMode.None)
                {
                    Cursor = selectionArmed ? Cursors.Cross : CursorFor(HitTest(eventArgs.Location));
                    return;
                }
                Point point = ImagePoint(eventArgs.Location);
                if (dragMode == DragMode.Move)
                {
                    SetCrop(new Rectangle(dragCrop.X + point.X - dragStart.X, dragCrop.Y + point.Y - dragStart.Y, dragCrop.Width, dragCrop.Height));
                    return;
                }
                if (dragMode == DragMode.Draw)
                {
                    SetCrop(SelectionBetween(dragStart, point));
                    return;
                }
                bool left = dragMode == DragMode.Left || dragMode == DragMode.TopLeft || dragMode == DragMode.BottomLeft;
                bool right = dragMode == DragMode.Right || dragMode == DragMode.TopRight || dragMode == DragMode.BottomRight;
                bool top = dragMode == DragMode.Top || dragMode == DragMode.TopLeft || dragMode == DragMode.TopRight;
                bool bottom = dragMode == DragMode.Bottom || dragMode == DragMode.BottomLeft || dragMode == DragMode.BottomRight;
                int x1 = left ? Math.Min(point.X, dragCrop.Right - 1) : dragCrop.Left;
                int x2 = right ? Math.Max(point.X, dragCrop.Left + 1) : dragCrop.Right;
                int y1 = top ? Math.Min(point.Y, dragCrop.Bottom - 1) : dragCrop.Top;
                int y2 = bottom ? Math.Max(point.Y, dragCrop.Top + 1) : dragCrop.Bottom;
                if (lockSquare && (left || right) && (top || bottom))
                {
                    SetCrop(SelectionBetween(new Point(left ? dragCrop.Right : dragCrop.Left, top ? dragCrop.Bottom : dragCrop.Top),
                        new Point(left ? x1 : x2, top ? y1 : y2)));
                }
                else if (lockSquare)
                {
                    bool horizontal = left || right;
                    double center = horizontal ? dragCrop.Y + dragCrop.Height / 2.0 : dragCrop.X + dragCrop.Width / 2.0;
                    int maximum = (int)Math.Floor(2 * Math.Min(center, (horizontal ? sourceImage.Height : sourceImage.Width) - center));
                    int side = Math.Max(1, Math.Min(maximum, horizontal ? x2 - x1 : y2 - y1));
                    SetCrop(new Rectangle(horizontal ? (left ? x2 - side : x1) : (int)Math.Round(center - side / 2.0),
                        horizontal ? (int)Math.Round(center - side / 2.0) : (top ? y2 - side : y1), side, side));
                }
                else SetCrop(Rectangle.FromLTRB(x1, y1, x2, y2));
            }

            private Rectangle SelectionBetween(Point anchor, Point point)
            {
                int dx = point.X - anchor.X, dy = point.Y - anchor.Y;
                int sx = dx < 0 || anchor.X == sourceImage.Width ? -1 : 1;
                int sy = dy < 0 || anchor.Y == sourceImage.Height ? -1 : 1;
                int width = Math.Max(1, Math.Abs(dx)), height = Math.Max(1, Math.Abs(dy));
                if (lockSquare)
                {
                    int side = Math.Max(width, height);
                    side = Math.Min(side, sx < 0 ? anchor.X : sourceImage.Width - anchor.X);
                    side = Math.Min(side, sy < 0 ? anchor.Y : sourceImage.Height - anchor.Y);
                    width = height = Math.Max(1, side);
                }
                return new Rectangle(sx < 0 ? anchor.X - width : anchor.X, sy < 0 ? anchor.Y - height : anchor.Y, width, height);
            }

            protected override void OnMouseUp(MouseEventArgs eventArgs)
            {
                base.OnMouseUp(eventArgs);
                if (eventArgs.Button != MouseButtons.Left) return;
                dragMode = DragMode.None;
                Capture = false;
            }

            protected override void OnMouseCaptureChanged(EventArgs eventArgs)
            {
                base.OnMouseCaptureChanged(eventArgs);
                dragMode = DragMode.None;
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (keyData == Keys.Escape && (selectionArmed || dragMode != DragMode.None))
                {
                    if (dragMode != DragMode.None) SetCrop(dragCrop);
                    selectionArmed = false;
                    dragMode = DragMode.None;
                    Capture = false;
                    Cursor = Cursors.Cross;
                    Invalidate();
                    return true;
                }
                return base.ProcessCmdKey(ref message, keyData);
            }

            protected override void OnMouseWheel(MouseEventArgs eventArgs)
            {
                base.OnMouseWheel(eventArgs);
                if (HasImage && !fitWholeImage && ZoomRequested != null)
                    ZoomRequested(Math.Sign(eventArgs.Delta));
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && sourceImage != null)
                {
                    sourceImage.Dispose();
                    sourceImage = null;
                }
                base.Dispose(disposing);
            }
        }
    }
}
