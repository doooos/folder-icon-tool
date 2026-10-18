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
                Require(Field<Control>(form, "cropPreview").Enabled, "Auto-fit must retain a focusable paste target.");
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
            VerifyPathEntryAndRotation(output, folder, picture);
            VerifyClipboardPaste(output, picture);
            byte[] resultingPicture = File.ReadAllBytes(picture);
            Require(originalPicture.Length == resultingPicture.Length, "The original photo length changed.");
            for (int index = 0; index < originalPicture.Length; index++)
                Require(originalPicture[index] == resultingPicture[index], "The original photo content changed.");
            Console.WriteLine("PASS: hidden render, whole-image fit, free selection, move, edge resize, bounds, square lock, transparent padding, matching preview, zoom, reset, quoted pasted paths, invalid-path write guards, left/right rotation, matching rotated export, current-path undo, clipboard image paste, text shortcut isolation, temporary PNG cleanup and unchanged original.");
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

    private static void VerifyPathEntryAndRotation(string output, string originalFolder, string picture)
    {
        string pastedFolder = Path.Combine(output, "粘贴 路径 文件夹");
        string missingFolder = Path.Combine(output, "不存在的 文件夹");
        string backups = Path.Combine(output, "path-rotation-backups");
        string quote = new String((char)34, 1);
        Directory.CreateDirectory(pastedFolder);
        using (var form = new MainForm(backups))
        {
            Require(!Field<Button>(form, "rotateLeftButton").Enabled && !Field<Button>(form, "rotateRightButton").Enabled,
                "Rotation must require an image.");
            form.LoadSelection(originalFolder, picture);
            PrepareHiddenControls(form);
            TextBox pathBox = Field<TextBox>(form, "folderPathBox");
            Require(!pathBox.ReadOnly, "The folder path must accept pasted text.");
            Require(pathBox.Width > 250 && pathBox.Height > 15, "The editable folder path must retain usable layout bounds.");
            pathBox.Text = "  " + quote + pastedFolder + quote + "  ";
            KeyEventArgs enter = new KeyEventArgs(Keys.Enter);
            typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(pathBox, new object[] { enter });
            Require(enter.Handled && enter.SuppressKeyPress, "Enter must confirm the path without a beep.");
            Require(pathBox.Text == pastedFolder && Field<string>(form, "selectedFolder") == pastedFolder,
                "Quoted Chinese paths with spaces must normalize on Enter.");
            pathBox.Text = "  " + quote + missingFolder + quote + "  ";
            Require(!Field<Button>(form, "applyButton").Enabled && !Field<Button>(form, "restoreButton").Enabled,
                "An invalid edited path must immediately disable both write actions.");
            Require(Field<string>(form, "selectedFolder") == null, "An invalid edit must discard the old folder selection.");
            Require(Field<Label>(form, "statusLabel").Text.IndexOf("不存在", StringComparison.Ordinal) >= 0,
                "An invalid path must show an inline explanation.");
            Invoke(form, "ApplySelection");
            Invoke(form, "RestoreSelection");
            Require(!Field<bool>(form, "busy"), "Invalid paths must not start a background write.");
            Require(!Directory.Exists(backups) && !Directory.Exists(missingFolder),
                "Invalid paths must not create backups or missing folders.");
            form.LoadSelection(null, picture);
            Require(pathBox.Text.IndexOf(missingFolder, StringComparison.Ordinal) >= 0 &&
                !Field<Button>(form, "applyButton").Enabled, "Loading a new image must not restore the stale folder.");
            pathBox.Text = picture;
            Click(Field<Button>(form, "confirmFolderButton"));
            Require(!Field<Button>(form, "restoreButton").Enabled, "A file path must not be accepted as a folder.");
            pathBox.Text = String.Empty;
            Require(!Field<Button>(form, "applyButton").Enabled, "An empty path must disable Apply.");
            pathBox.Text = "  " + quote + originalFolder + quote + "  ";
            Click(Field<Button>(form, "confirmFolderButton"));
            Require(pathBox.Text == originalFolder && Field<Button>(form, "applyButton").Enabled,
                "The confirm button must accept and normalize an existing folder.");

            Click(Field<Button>(form, "rotateRightButton"));
            AssertRotation(form, picture, 90);
            AssertPreviewMatches(form, null);
            Click(Field<Button>(form, "rotateRightButton"));
            AssertRotation(form, picture, 180);
            Click(Field<Button>(form, "rotateLeftButton"));
            AssertRotation(form, picture, 90);
            Click(Field<Button>(form, "rotateLeftButton"));
            AssertRotation(form, picture, 0);
            Click(Field<Button>(form, "rotateLeftButton"));
            AssertRotation(form, picture, 270);
            form.LoadSelection(null, picture);
            AssertRotation(form, picture, 0);
            Field<CheckBox>(form, "squareCropCheckBox").Checked = true;
            Click(Field<Button>(form, "customCropButton"));
            Control canvas = Field<Control>(form, "cropPreview");
            Drag(canvas, new Point(100, 100), new Point(500, 500));
            Click(Field<Button>(form, "rotateRightButton"));
            AssertRotation(form, picture, 90);
            Require(!Field<CheckBox>(form, "fitWholeImageCheckBox").Checked &&
                Field<CheckBox>(form, "squareCropCheckBox").Checked, "Rotation must preserve crop mode and square lock.");
            Require(ReadCrop(canvas) == new Rectangle(0, 100, 800, 800) && Field<TrackBar>(form, "zoomBar").Value == 100,
                "Rotation must reset the crop and zoom to the rotated image bounds.");
            Field<CheckBox>(form, "squareCropCheckBox").Checked = false;
            Click(Field<Button>(form, "customCropButton"));
            Drag(canvas, new Point(100, 200), new Point(650, 600));
            AssertPreviewMatches(form, ReadCrop(canvas));
            pathBox.Text = "  " + quote + pastedFolder + quote + "  ";
            Invoke(form, "ApplySelection");
            WaitOperation(form);
            AssertAppliedMatchesPreview(form, pastedFolder);
            Require(Directory.GetFiles(originalFolder).Length == 0, "Apply must not write the previous folder.");
            byte[] applied = File.ReadAllBytes(Path.Combine(pastedFolder, ".folder-portrait.ico"));
            pathBox.Text = missingFolder;
            Invoke(form, "RestoreSelection");
            Require(!Field<bool>(form, "busy"), "Invalid edited paths must block Undo even after a successful Apply.");
            RequireBytes(File.ReadAllBytes(Path.Combine(pastedFolder, ".folder-portrait.ico")), applied,
                "Invalid-path Undo changed the previously selected folder.");
            form.LoadSelection(originalFolder, null);
            pathBox.Text = " " + quote + pastedFolder + quote + " ";
            Invoke(form, "RestoreSelection");
            WaitOperation(form);
            Require(Directory.GetFiles(pastedFolder).Length == 0 && Directory.GetFiles(originalFolder).Length == 0,
                "Undo must restore the current pasted folder, not the previous selected folder.");
            Field<CheckBox>(form, "fitWholeImageCheckBox").Checked = true;
            Click(Field<Button>(form, "rotateLeftButton"));
            AssertRotation(form, picture, 0);
            Click(Field<Button>(form, "rotateLeftButton"));
            AssertRotation(form, picture, 270);
            AssertPreviewMatches(form, null);
            Invoke(form, "ApplySelection");
            WaitOperation(form);
            AssertAppliedMatchesPreview(form, pastedFolder);
            Require(!form.Visible, "Path validation and rotation tests must never show the form.");
        }
    }

    private static void VerifyClipboardPaste(string output, string picture)
    {
        string folder = Path.Combine(output, "截图粘贴目标");
        string backups = Path.Combine(output, "clipboard-backups");
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(backups);
        string unrelatedFile = Path.Combine(backups, "keep-user-file.txt");
        File.WriteAllText(unrelatedFile, "preserve");
        string disposedPath;
        int providerCalls = 0;
        bool noImage = false, unavailable = false;
        using (Bitmap clipboardSource = new Bitmap(420, 260, PixelFormat.Format32bppArgb))
        {
            using (Graphics graphics = Graphics.FromImage(clipboardSource))
            {
                graphics.FillRectangle(Brushes.Crimson, 10, 20, 190, 220);
                graphics.FillRectangle(Brushes.RoyalBlue, 210, 40, 200, 180);
            }
            clipboardSource.SetPixel(21, 22, Color.FromArgb(77, 100, 150, 200));
            Color originalPixel = clipboardSource.GetPixel(21, 22);
            using (var form = new MainForm(backups, delegate
            {
                providerCalls++;
                if (unavailable)
                    throw new InvalidOperationException("Clipboard temporarily unavailable");
                return noImage ? null : new Bitmap(clipboardSource);
            }))
            {
                form.LoadSelection(folder, null);
                PrepareHiddenControls(form);
                Button paste = Field<Button>(form, "pasteImageButton");
                Require(paste.Enabled && paste.Width > 150, "Paste must be available before choosing an image.");
                TextBox pathBox = Field<TextBox>(form, "folderPathBox");
                form.ActiveControl = pathBox;
                string pathText = pathBox.Text;
                Require(!SendPasteShortcut(form) && providerCalls == 0 && pathBox.Text == pathText,
                    "Ctrl+V in the folder textbox must defer to native text paste without reading images.");
                TextBox nested = new TextBox();
                Panel nestedPanel = new Panel();
                nestedPanel.Controls.Add(nested);
                form.Controls.Add(nestedPanel);
                form.ActiveControl = nested;
                Require(!SendPasteShortcut(form) && providerCalls == 0,
                    "Nested text input must also retain native text paste.");
                Control canvas = Field<Control>(form, "cropPreview");
                form.Controls.Remove(nestedPanel);
                nestedPanel.Dispose();
                form.ActiveControl = pathBox;
                Require(canvas.Enabled, "The empty preview must remain enabled to receive a click for paste focus.");
                ClickPreview(canvas);
                Require(form.ActiveControl == canvas,
                    "Clicking the empty preview must move keyboard focus away from the folder textbox.");
                Require(SendPasteShortcut(form) && providerCalls == 1,
                    "Ctrl+V after clicking the empty preview must request the clipboard image.");
                string firstPath = Field<string>(form, "selectedImage");
                Require(File.Exists(firstPath) && Path.GetDirectoryName(firstPath).StartsWith(backups + Path.DirectorySeparatorChar),
                    "Clipboard images must use a unique owned application PNG.");
                Require(Field<TextBox>(form, "imagePathBox").Text.IndexOf("剪贴板", StringComparison.Ordinal) >= 0,
                    "The source field must clearly identify a pasted image.");
                using (Bitmap saved = IconEngine.LoadOrientedImage(firstPath))
                {
                    Require(saved.Size == clipboardSource.Size && saved.GetPixel(0, 0).A == 0,
                        "The pasted PNG must preserve screenshot dimensions and transparency.");
                    Require(saved.GetPixel(21, 22).A == originalPixel.A,
                        "Pasting must preserve partially transparent pixels.");
                }
                Rectangle fittedCrop = ReadCrop(canvas);
                int fittedZoom = Field<TrackBar>(form, "zoomBar").Value;
                form.ActiveControl = pathBox;
                ClickPreview(canvas);
                Require(form.ActiveControl == canvas && Field<CheckBox>(form, "fitWholeImageCheckBox").Checked,
                    "Clicking a fitted image must focus the preview without entering crop mode.");
                Drag(canvas, new Point(20, 20), new Point(100, 100));
                canvas.GetType().GetMethod("OnMouseWheel", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.None, 0, 20, 20, 120) });
                Require(ReadCrop(canvas) == fittedCrop && Field<TrackBar>(form, "zoomBar").Value == fittedZoom &&
                    !Field<TrackBar>(form, "zoomBar").Enabled && !Field<Button>(form, "resetButton").Enabled,
                    "A focused auto-fit preview must still reject manual crop and zoom input.");
                Require(SendPasteShortcut(form) && providerCalls == 2,
                    "Ctrl+V after clicking the fitted preview must paste another image.");
                Require(!File.Exists(firstPath), "Pasting from the focused preview must clean the previous temporary source.");
                firstPath = Field<string>(form, "selectedImage");
                Click(Field<Button>(form, "rotateRightButton"));
                AssertRotation(form, firstPath, 90);
                Field<CheckBox>(form, "fitWholeImageCheckBox").Checked = false;
                canvas.GetType().GetMethod("SetCrop", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas, new object[] { new Rectangle(30, 40, 200, 300) });
                AssertPreviewMatches(form, ReadCrop(canvas));
                Invoke(form, "ApplySelection");
                Require(!paste.Enabled, "Paste must be disabled while Apply reads the temporary image.");
                int readsBeforeBusyPaste = providerCalls;
                Click(paste);
                Require(providerCalls == readsBeforeBusyPaste && Field<string>(form, "selectedImage") == firstPath,
                    "Even a direct paste command must not read or replace the source during Apply.");
                WaitOperation(form);
                AssertAppliedMatchesPreview(form, folder);
                Require(File.Exists(firstPath), "The active pasted image must remain available for repeated rotation and Apply.");
                form.LoadSelection(folder, null);
                Require(File.Exists(firstPath), "Folder-only selection must retain the pasted image.");
                Click(paste);
                string secondPath = Field<string>(form, "selectedImage");
                Require(secondPath != firstPath && File.Exists(secondPath) && !File.Exists(firstPath) &&
                    !Directory.Exists(Path.GetDirectoryName(firstPath)),
                    "Replacing a pasted image must remove only the previous owned PNG and empty directory.");
                Require(Field<int>(form, "clockwiseRotation") == 0, "A new paste must reset rotation.");
                using (Bitmap invalid = new Bitmap(1, 1))
                {
                    invalid.Dispose();
                    try
                    {
                        typeof(MainForm).GetMethod("LoadPastedImage", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, new object[] { invalid });
                        throw new Exception("A disposed image must not load successfully.");
                    }
                    catch (TargetInvocationException)
                    {
                        Require(Field<string>(form, "selectedImage") == secondPath && File.Exists(secondPath) &&
                            Directory.GetDirectories(backups, "clipboard-*").Length == 1,
                            "A failed paste must retain the old image and clean the newly created temporary directory.");
                    }
                }
                noImage = true;
                Click(paste);
                Require(Field<string>(form, "selectedImage") == secondPath && File.Exists(secondPath) &&
                    Field<Label>(form, "statusLabel").Text.IndexOf("没有图片", StringComparison.Ordinal) >= 0,
                    "An empty image clipboard must report inline and retain the current image.");
                noImage = false;
                unavailable = true;
                Click(paste);
                Require(Field<string>(form, "selectedImage") == secondPath &&
                    Field<Label>(form, "statusLabel").Text.IndexOf("无法粘贴", StringComparison.Ordinal) >= 0,
                    "Clipboard access errors must report inline without discarding the current image.");
                unavailable = false;
                form.LoadSelection(null, picture);
                Require(!File.Exists(secondPath) && !Directory.Exists(Path.GetDirectoryName(secondPath)),
                    "Selecting a file must clean the previous clipboard source.");
                Click(paste);
                disposedPath = Field<string>(form, "selectedImage");
                Require(File.Exists(disposedPath) && !form.Visible,
                    "The final clipboard source must remain live without showing the form.");
                Require(clipboardSource.GetPixel(21, 22) == originalPixel,
                    "Pasting must not modify or dispose the supplied clipboard image.");
            }
        }
        Require(!File.Exists(disposedPath) && !Directory.Exists(Path.GetDirectoryName(disposedPath)),
            "Disposing the form must delete its active clipboard source and empty directory.");
        Require(File.ReadAllText(unrelatedFile) == "preserve", "Temporary cleanup must preserve unrelated user files.");
        Require(Directory.GetDirectories(backups, "clipboard-*").Length == 0,
            "The clipboard smoke test must not leave temporary image directories.");
    }

    private static bool SendPasteShortcut(MainForm form)
    {
        Message message = Message.Create(form.Handle, 0x100, new IntPtr((int)Keys.V), IntPtr.Zero);
        object[] arguments = new object[] { message, Keys.Control | Keys.V };
        return (bool)typeof(MainForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, arguments);
    }

    private static void ClickPreview(Control canvas)
    {
        canvas.GetType().GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, canvas.Width / 2, canvas.Height / 2, 0) });
        canvas.GetType().GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, canvas.Width / 2, canvas.Height / 2, 0) });
    }

    private static void AssertRotation(MainForm form, string picture, int angle)
    {
        Require(Field<int>(form, "clockwiseRotation") == angle, "The GUI rotation angle is incorrect.");
        Control canvas = Field<Control>(form, "cropPreview");
        Bitmap actual = (Bitmap)canvas.GetType().GetProperty("SourceImage").GetValue(canvas, null);
        using (Bitmap expected = IconEngine.LoadOrientedImage(picture))
        {
            if (angle == 90) expected.RotateFlip(RotateFlipType.Rotate90FlipNone);
            else if (angle == 180) expected.RotateFlip(RotateFlipType.Rotate180FlipNone);
            else if (angle == 270) expected.RotateFlip(RotateFlipType.Rotate270FlipNone);
            Require(actual.Size == expected.Size, "Rotation must swap image dimensions at 90 and 270 degrees.");
            for (int row = 0; row < expected.Height; row += 37)
                for (int column = 0; column < expected.Width; column += 31)
                    Require(actual.GetPixel(column, row) == expected.GetPixel(column, row), "The preview rotation direction is incorrect.");
        }
    }

    private static void AssertAppliedMatchesPreview(MainForm form, string folder)
    {
        byte[] expected = (byte[])typeof(IconEngine).GetMethod("MakeIcon", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Field<PictureBox>(form, "iconPreview").Image });
        RequireBytes(File.ReadAllBytes(Path.Combine(folder, ".folder-portrait.ico")), expected,
            "Applied icon must exactly match the rotated preview and crop.");
    }

    private static void RequireBytes(byte[] actual, byte[] expected, string message)
    {
        Require(actual.Length == expected.Length, message);
        for (int index = 0; index < actual.Length; index++)
            Require(actual[index] == expected[index], message);
    }

    private static void Invoke(MainForm form, string method)
    {
        typeof(MainForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
    }

    private static void WaitOperation(MainForm form)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (Field<bool>(form, "busy") && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(10);
        }
        Require(!Field<bool>(form, "busy"), "The hidden GUI operation timed out.");
    }

    private static T Field<T>(MainForm form, string name)
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
