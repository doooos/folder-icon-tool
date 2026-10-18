using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using FolderIconTool;

internal static class GuiSmoke
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Supply an output directory.");
            string output = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(output);
            string folder = Path.Combine(output, "示例文件夹");
            Directory.CreateDirectory(folder);
            string picture = Path.Combine(output, "示例图片.png");
            using (var bitmap = new Bitmap(1000, 800))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(222, 238, 246));
                using (var ground = new SolidBrush(Color.FromArgb(154, 193, 166)))
                    g.FillEllipse(ground, -100, 550, 1200, 500);
                using (var body = new SolidBrush(Color.FromArgb(98, 125, 169)))
                    g.FillEllipse(body, 315, 380, 370, 510);
                using (var hair = new SolidBrush(Color.FromArgb(73, 63, 59)))
                    g.FillEllipse(hair, 355, 100, 290, 340);
                using (var face = new SolidBrush(Color.FromArgb(245, 211, 182)))
                    g.FillEllipse(face, 385, 150, 230, 270);
                using (var eyes = new SolidBrush(Color.FromArgb(72, 62, 63)))
                {
                    g.FillEllipse(eyes, 442, 266, 16, 16);
                    g.FillEllipse(eyes, 543, 266, 16, 16);
                }
                using (var smile = new Pen(Color.FromArgb(167, 101, 96), 5))
                    g.DrawArc(smile, 460, 305, 80, 45, 0, 180);
                bitmap.Save(picture, ImageFormat.Png);
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            byte[] originalPicture = File.ReadAllBytes(picture);
            using (var form = new MainForm(Path.Combine(output, "backups")))
            {
                Require(!Field<Button>(form, "applyButton").Enabled, "Apply must require a folder and image.");
                Require(!Field<Button>(form, "restoreButton").Enabled, "Undo must require a folder.");
                form.LoadSelection(folder, picture);
                PrepareHiddenControls(form);
                Require(!form.Visible, "The smoke test must never show the form.");
                Require(Field<Control>(form, "cropPreview").Width > 300, "The crop canvas was not laid out.");
                Require(Field<PictureBox>(form, "iconPreview").Height > 100, "The icon preview was not laid out.");
                Require(Field<Button>(form, "applyButton").Enabled, "Apply must accept a folder and image.");
                Require(Field<Button>(form, "restoreButton").Enabled, "Undo must accept a folder without backups.");
                CheckBox fitWholeImage = Field<CheckBox>(form, "fitWholeImageCheckBox");
                Require(fitWholeImage.Checked, "The default must preserve the whole image without cropping.");
                Require(!Field<Control>(form, "cropPreview").Enabled, "Auto-fit must disable manual cropping.");
                Require(!Field<TrackBar>(form, "zoomBar").Enabled, "Auto-fit must disable crop zoom.");
                Require(!Field<Button>(form, "resetButton").Enabled, "Auto-fit must disable crop reset.");
                AssertPreviewMatches(form, null);
                Bitmap fitted = (Bitmap)Field<PictureBox>(form, "iconPreview").Image;
                Require(fitted.GetPixel(0, 0).A == 0, "The landscape photo must use transparent padding, not clipping.");
                Require(fitted.GetPixel(128, 128).A == 255, "The fitted image must retain its opaque center.");
                using (var screenshot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, form.Size));
                    int changedSamples = 0;
                    for (int row = 0; row < screenshot.Height; row += 4)
                        for (int column = 0; column < screenshot.Width; column += 4)
                            if (screenshot.GetPixel(column, row).ToArgb() != form.BackColor.ToArgb())
                                changedSamples++;
                    Require(changedSamples > screenshot.Width * screenshot.Height / 80,
                        "Hidden rendering produced a blank form instead of the controls and photo.");
                    screenshot.Save(Path.Combine(output, "界面预览.png"), ImageFormat.Png);
                }
                TrackBar zoom = Field<TrackBar>(form, "zoomBar");
                fitWholeImage.Checked = false;
                Require(Field<Control>(form, "cropPreview").Enabled && zoom.Enabled && Field<Button>(form, "resetButton").Enabled,
                    "Optional crop mode must enable crop adjustments.");
                AssertPreviewMatches(form, new Rectangle(100, 0, 800, 800));
                zoom.Value = 800;
                AssertPreviewMatches(form, new Rectangle(450, 350, 100, 100));
                Control canvas = Field<Control>(form, "cropPreview");
                canvas.GetType().GetMethod("SetCrop", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas, new object[] { new Rectangle(-40, 10000, 100, 100) });
                AssertPreviewMatches(form, new Rectangle(0, 700, 100, 100));
                Button reset = Field<Button>(form, "resetButton");
                reset.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(reset, new object[] { EventArgs.Empty });
                Require(zoom.Value == 100, "Reset must restore 1x zoom.");
                AssertPreviewMatches(form, new Rectangle(100, 0, 800, 800));
                fitWholeImage.Checked = true;
                Click(Field<Button>(form, "customCropButton"));
                Require(!fitWholeImage.Checked && canvas.Enabled, "Custom crop must enable selection from auto-fit.");
                Drag(canvas, new Point(200, 180), new Point(700, 580));
                Rectangle selected = ReadCrop(canvas);
                RequireClose(selected, new Rectangle(200, 180, 500, 400), "Free selection");
                AssertPreviewMatches(form, selected);
                Require(((Bitmap)Field<PictureBox>(form, "iconPreview").Image).GetPixel(128, 0).A == 0,
                    "A rectangular selection must retain its aspect ratio with transparent padding.");

                Drag(canvas, new Point(450, 380), new Point(500, 420));
                Rectangle moved = ReadCrop(canvas);
                RequireClose(moved, new Rectangle(selected.X + 50, selected.Y + 40, selected.Width, selected.Height), "Move selection");
                Require(moved.Size == selected.Size, "Moving must preserve the crop size.");
                Drag(canvas, new Point(moved.Right, moved.Top + moved.Height / 2),
                    new Point(850, moved.Top + moved.Height / 2));
                Rectangle resized = ReadCrop(canvas);
                Require(Math.Abs(resized.Right - 850) <= 2 && resized.Left == moved.Left &&
                    resized.Top == moved.Top && resized.Height == moved.Height, "Dragging an edge must resize only that edge.");
                AssertPreviewMatches(form, resized);
                Drag(canvas, new Point(resized.Left + resized.Width / 2, resized.Top + resized.Height / 2),
                    new Point(1600, 1300));
                Rectangle bounded = ReadCrop(canvas);
                Require(bounded.Right == 1000 && bounded.Bottom == 800 && bounded.Size == resized.Size,
                    "Moving outside the image must stop at its bounds without shrinking.");

                Field<CheckBox>(form, "squareCropCheckBox").Checked = true;
                Click(Field<Button>(form, "customCropButton"));
                Drag(canvas, new Point(100, 100), new Point(600, 400));
                Rectangle square = ReadCrop(canvas);
                RequireClose(square, new Rectangle(100, 100, 500, 500), "Locked square selection");
                Require(square.Width == square.Height, "Square lock must preserve 1:1.");
                AssertPreviewMatches(form, square);
                using (var screenshot = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(screenshot, new Rectangle(Point.Empty, form.Size));
                    screenshot.Save(Path.Combine(output, "自定义裁剪预览.png"), ImageFormat.Png);
                }
                fitWholeImage.Checked = true;
                AssertPreviewMatches(form, null);
                Require(Field<Button>(form, "applyButton").Enabled, "Auto-fit must be immediately ready to apply.");
                if (Directory.GetFiles(folder).Length != 0)
                    throw new Exception("Loading the GUI unexpectedly changed the target folder.");
                Require(!Directory.Exists(Path.Combine(output, "backups")), "Preview must not create backups.");
                using (var unlocked = new FileStream(picture, FileMode.Open, FileAccess.Read, FileShare.None))
                    Require(unlocked.Length == originalPicture.Length, "Loading must not lock or modify the photo.");
            }
            using (var form = new MainForm(Path.Combine(output, "backups")))
            {
                form.LoadSelection(folder, null);
                Require(!Field<Button>(form, "applyButton").Enabled, "Folder-only selection must not enable Apply.");
                Require(Field<Button>(form, "restoreButton").Enabled, "Folder-only selection must enable Undo.");
            }
            byte[] resultingPicture = File.ReadAllBytes(picture);
            Require(originalPicture.Length == resultingPicture.Length, "The original photo length changed.");
            for (int index = 0; index < originalPicture.Length; index++)
                Require(originalPicture[index] == resultingPicture[index], "The original photo content changed.");
            Console.WriteLine("PASS: hidden render, whole-image fit, free selection, move, edge resize, bounds, square lock, transparent padding, matching preview, zoom, reset, folder-only undo and unchanged original.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void PrepareHiddenControls(Control control)
    {
        IntPtr unused = control.Handle;
        control.PerformLayout();
        foreach (Control child in control.Controls)
            PrepareHiddenControls(child);
        control.PerformLayout();
    }

    private static T Field<T>(MainForm form, string name) where T : class
    {
        return (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    }

    private static void Click(Button button)
    {
        button.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(button, new object[] { EventArgs.Empty });
    }

    private static Rectangle ReadCrop(Control canvas)
    {
        return (Rectangle)canvas.GetType().GetProperty("CropRectangle").GetValue(canvas, null);
    }

    private static Point CanvasPoint(Control canvas, Point point)
    {
        RectangleF bounds = (RectangleF)canvas.GetType().GetMethod("ImageBounds",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas, null);
        Bitmap source = (Bitmap)canvas.GetType().GetProperty("SourceImage").GetValue(canvas, null);
        return new Point((int)Math.Round(bounds.Left + point.X * bounds.Width / source.Width),
            (int)Math.Round(bounds.Top + point.Y * bounds.Height / source.Height));
    }

    private static void Drag(Control canvas, Point fromImage, Point toImage)
    {
        Point from = CanvasPoint(canvas, fromImage), to = CanvasPoint(canvas, toImage);
        Type type = canvas.GetType();
        type.GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0) });
        type.GetMethod("OnMouseMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0) });
        type.GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(canvas,
            new object[] { new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0) });
    }

    private static void RequireClose(Rectangle actual, Rectangle expected, string operation)
    {
        Require(Math.Abs(actual.Left - expected.Left) <= 2 && Math.Abs(actual.Top - expected.Top) <= 2 &&
            Math.Abs(actual.Right - expected.Right) <= 2 && Math.Abs(actual.Bottom - expected.Bottom) <= 2,
            operation + ": " + actual + "; expected approximately " + expected);
    }

    private static void AssertPreviewMatches(MainForm form, Rectangle? expectedCrop)
    {
        Control canvas = Field<Control>(form, "cropPreview");
        Type canvasType = canvas.GetType();
        Rectangle crop = (Rectangle)canvasType.GetProperty("CropRectangle").GetValue(canvas, null);
        bool fitWholeImage = Field<CheckBox>(form, "fitWholeImageCheckBox").Checked;
        Require(fitWholeImage == !expectedCrop.HasValue, "The preview mode differs from the requested mode.");
        if (expectedCrop.HasValue)
            Require(crop == expectedCrop.Value, "Unexpected crop: " + crop + "; expected " + expectedCrop);
        Bitmap source = (Bitmap)canvasType.GetProperty("SourceImage").GetValue(canvas, null);
        Bitmap actual = (Bitmap)Field<PictureBox>(form, "iconPreview").Image;
        Require(actual != null && actual.Size == new Size(256, 256), "The actual icon must be 256 x 256.");
        using (Bitmap expected = IconEngine.RenderIconImage(source, expectedCrop, fitWholeImage))
        {
            for (int row = 0; row < expected.Height; row++)
                for (int column = 0; column < expected.Width; column++)
                    Require(actual.GetPixel(column, row) == expected.GetPixel(column, row), "The GUI icon differs from the applied crop.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}
