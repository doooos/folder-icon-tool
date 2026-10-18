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
            Console.WriteLine("PASS: hidden render, default whole-image fit, transparent padding, optional 256px crop, zoom, clamping, reset, folder-only undo, unlocked original and no writes.");
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
        using (Bitmap expected = fitWholeImage ? IconEngine.FitSquare(source) : IconEngine.CropSquare(source, expectedCrop))
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
