using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FolderIconTool.Tests
{
    public static class EngineTests
    {
        private const string IconName = ".folder-portrait.ico";
        private static string runRoot;
        private static int passed;
        private static int failed;

        public static int Main(string[] arguments)
        {
            try
            {
                if (arguments.Length != 1) throw new ArgumentException("Usage: EngineTests.exe <output-root-within-current-workspace>");
                string workspace = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string outputRoot = Path.GetFullPath(arguments[0]);
                Require(outputRoot.StartsWith(workspace, StringComparison.OrdinalIgnoreCase), "Output root must be inside the current workspace.");
                runRoot = Path.Combine(outputRoot, Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(runRoot);
                Run("crop dimensions and selected pixels", CropBehavior);
                Run("wide auto-fit preserves edges, aspect and transparency", delegate(string fixture) { FitBehavior(fixture, false); });
                Run("tall auto-fit preserves edges, aspect and transparency", delegate(string fixture) { FitBehavior(fixture, true); });
                Run("tiny auto-fit enlarges into compatible ICO", TinyFit);
                Run("auto-fit apply preserves source and undo", ApplyFit);
                Run("auto-fit with explicit crop rejects without mutation", ConflictingFit);
                Run("EXIF orientation and source preservation", ExifOrientation);
                Run("JPEG input, apply and undo", delegate(string fixture) { RasterFormat(fixture, "jpg", ImageFormat.Jpeg); });
                Run("BMP input, apply and undo", delegate(string fixture) { RasterFormat(fixture, "bmp", ImageFormat.Bmp); });
                Run("GIF input, apply and undo", delegate(string fixture) { RasterFormat(fixture, "gif", ImageFormat.Gif); });
                Run("TIFF input, apply and undo", delegate(string fixture) { RasterFormat(fixture, "tiff", ImageFormat.Tiff); });
                Run("ICO largest frame, apply and undo", IconInput);
                Run("WebP codec support or safe rejection", delegate(string fixture) { OptionalFormat(fixture, "webp", "UklGRiYAAABXRUJQVlA4TBoAAAAvP8APAA8Q87//8x8OxILJ/KVj6Ivof1TBdw=="); });
                Run("AVIF codec support or safe rejection", delegate(string fixture) { OptionalFormat(fixture, "avif", "AAAAIGZ0eXBhdmlmAAAAAGF2aWZtaWYxbWlhZk1BMUEAAADrbWV0YQAAAAAAAAAhaGRscgAAAAAAAAAAcGljdAAAAAAAAAAAAAAAAAAAAAAOcGl0bQAAAAAAAQAAAB5pbG9jAAAAAEQAAAEAAQAAAAEAAAETAAAANgAAAChpaW5mAAAAAAABAAAAGmluZmUCAAAAAAEAAGF2MDFDb2xvcgAAAABqaXBycAAAAEtpcGNvAAAAFGlzcGUAAAAAAAAAQAAAAEAAAAAQcGl4aQAAAAADCAgIAAAADGF2MUOBIAAAAAAAE2NvbHJuY2x4AAEADQAGgAAAABdpcG1hAAAAAAAAAAEAAQQBAoMEAAAAPm1kYXQSAAoJOBV//YQENBpAMicQAAC0UbTxVeeEZAeidv30TQeRfGBgtghWYqddheV2ALr7/kPipaA="); });
                Run("HEIC codec support or safe rejection", delegate(string fixture) { OptionalFormat(fixture, "heic", "AAAAHGZ0eXBoZWljAAAAAG1pZjFoZWljbWlhZgAAAVZtZXRhAAAAAAAAACFoZGxyAAAAAAAAAABwaWN0AAAAAAAAAAAAAAAAAAAAACJpbG9jAAAAAERAAAEAAQAAAAABegABAAAAAAAAAC8AAAAjaWluZgAAAAAAAQAAABVpbmZlAgAAAAABAABodmMxAAAAAA5waXRtAAAAAAABAAAA1mlwcnAAAAC3aXBjbwAAAHhodmNDAQNwAAAAAAAAAAAAHvAA/P34+AAADwNgAAEAGEABDAH//wNwAAADAJAAAAMAAAMAHroCQGEAAQArQgEBA3AAAAMAkAAAAwAAAwAeoCCBBZbqSSmubgIaDAgAAAMAyAAAAwAIQGIAAQAHRAHBcrAiQAAAABNjb2xybmNseAABAA0ABoAAAAAUaXNwZQAAAAAAAABAAAAAQAAAABBwaXhpAAAAAAMICAgAAAAXaXBtYQAAAAAAAAABAAEEgQIDBAAAADdtZGF0AAAAKygBrwa4Y7Dc/9VyP///AWj6H+9g6Nam/gI5r7/gf+///jM8f/sDCb/gb1w="); });
                Run("apply ICO sizes, source hash and INI preservation", ApplyBehavior);
                Run("restore original bytes and attributes", RestoreExisting);
                Run("two replacements and two undos", UndoHistory);
                Run("new-folder undo preserves unrelated files", RestoreNewFolder);
                Run("source inside target stays unchanged", SourceInsideTarget);
                Run("target icon cannot be its own source", TargetIconAsSource);
                Run("changed icon blocks undo without mutation", delegate(string fixture) { Conflict(fixture, IconName); });
                Run("changed INI blocks undo without mutation", delegate(string fixture) { Conflict(fixture, "desktop.ini"); });
                Run("invalid images do not mutate target", InvalidImage);
                Run("out-of-bounds crops do not mutate target", InvalidCrop);
                Console.WriteLine("RESULT: {0} passed, {1} failed", passed, failed);
                Console.WriteLine("Fixtures retained: " + runRoot);
                return failed == 0 ? 0 : 1;
            }
            catch (Exception exception) { Console.Error.WriteLine("HARNESS ERROR: " + exception.Message); return 2; }
        }

        private static void Run(string name, Action<string> test)
        {
            string fixture = Path.Combine(runRoot, (passed + failed + 1).ToString("00"));
            Directory.CreateDirectory(fixture);
            try { test(fixture); passed++; Console.WriteLine("PASS: " + name); }
            catch (Exception exception) { failed++; Console.Error.WriteLine("FAIL: " + name + " | " + exception.GetType().Name + ": " + exception.Message); }
        }

        private static void CropBehavior(string fixture)
        {
            string source = Path.Combine(fixture, "source.png");
            using (Bitmap bitmap = new Bitmap(400, 200))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Lime);
                graphics.FillRectangle(Brushes.Red, 100, 0, 100, 200);
                graphics.FillRectangle(Brushes.Blue, 200, 0, 100, 200);
                graphics.FillRectangle(Brushes.Yellow, 300, 0, 100, 200);
                bitmap.Save(source, ImageFormat.Png);
            }
            using (Bitmap oriented = IconEngine.LoadOrientedImage(source))
            using (Bitmap centered = IconEngine.CropSquare(oriented, null))
            using (Bitmap selected = IconEngine.CropSquare(oriented, new Rectangle(200, 0, 200, 200)))
            {
                Require(centered.Width == 256 && centered.Height == 256, "Default crop must be 256 x 256.");
                Require(selected.Width == 256 && selected.Height == 256, "Explicit crop must be 256 x 256.");
                Require(Near(centered.GetPixel(64, 128), Color.Red), "Center crop lost left half.");
                Require(Near(centered.GetPixel(192, 128), Color.Blue), "Center crop lost right half.");
                Require(Near(selected.GetPixel(64, 128), Color.Blue), "Explicit crop did not select requested pixels.");
                Require(Near(selected.GetPixel(192, 128), Color.Yellow), "Explicit crop did not preserve its right edge.");
            }
        }

        private static void FitBehavior(string fixture, bool tall)
        {
            string source = MakeFitImage(fixture, tall);
            using (Bitmap image = IconEngine.LoadOrientedImage(source))
            using (Bitmap fitted = IconEngine.FitSquare(image))
            using (Bitmap rendered = IconEngine.RenderIconImage(image, null, true))
            using (Bitmap cropped = IconEngine.RenderIconImage(image, null, false))
            {
                Require(fitted.Width == 256 && fitted.Height == 256, "Fit canvas must be 256 x 256.");
                VerifyFitBands(fitted, tall);
                VerifyFitBands(rendered, tall);
                Require(tall ? Near(cropped.GetPixel(128, 64), Color.Red) && Near(cropped.GetPixel(128, 192), Color.Blue) :
                    Near(cropped.GetPixel(64, 128), Color.Red) && Near(cropped.GetPixel(192, 128), Color.Blue),
                    "Legacy render mode no longer center-crops.");
                Require(cropped.GetPixel(16, 16).A >= 240, "Legacy crop unexpectedly adds transparent padding.");
            }
        }

        private static string MakeFitImage(string fixture, bool tall)
        {
            string source = Path.Combine(fixture, "fit.png");
            using (Bitmap image = new Bitmap(tall ? 100 : 400, tall ? 400 : 100))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                Brush[] colors = new Brush[] { Brushes.Lime, Brushes.Red, Brushes.Blue, Brushes.Yellow };
                for (int band = 0; band < colors.Length; band++)
                    graphics.FillRectangle(colors[band], tall ? 0 : band * 100, tall ? band * 100 : 0, 100, 100);
                image.Save(source, ImageFormat.Png);
            }
            return source;
        }

        private static void VerifyFitBands(Bitmap image, bool tall)
        {
            int size = image.Width;
            Require(size == image.Height, "Fit canvas is not square.");
            Color[] colors = new Color[] { Color.Lime, Color.Red, Color.Blue, Color.Yellow };
            for (int band = 0; band < colors.Length; band++)
            {
                int along = size * (band * 2 + 1) / 8;
                Require(Near(image.GetPixel(tall ? size / 2 : along, tall ? along : size / 2), colors[band]),
                    "Auto-fit loses edge markers or distorts band placement.");
            }
            Require(image.GetPixel(tall ? size / 8 : size / 2, tall ? size / 2 : size / 8).A == 0,
                "Auto-fit padding must remain transparent.");
            VerifyContentBounds(image, tall ? size / 4 : size, tall ? size : size / 4);
        }

        private static void VerifyContentBounds(Bitmap image, int expectedWidth, int expectedHeight)
        {
            int left = image.Width, right = -1, top = image.Height, bottom = -1;
            for (int row = 0; row < image.Height; row++)
                for (int column = 0; column < image.Width; column++)
                    if (image.GetPixel(column, row).A >= 128)
                    {
                        left = Math.Min(left, column); right = Math.Max(right, column);
                        top = Math.Min(top, row); bottom = Math.Max(bottom, row);
                    }
            Require(right >= left && bottom >= top, "Fitted image has no visible pixels.");
            int width = right - left + 1, height = bottom - top + 1;
            Require(Math.Abs(width - expectedWidth) <= 2 && Math.Abs(height - expectedHeight) <= 2,
                "Fitted content does not preserve the source aspect ratio.");
            Require(Math.Abs(left - (image.Width - width) / 2) <= 1 && Math.Abs(top - (image.Height - height) / 2) <= 1,
                "Fitted content is not centered.");
        }

        private static void TinyFit(string fixture)
        {
            string source = Path.Combine(fixture, "tiny.png");
            using (Bitmap image = new Bitmap(16, 8))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.Clear(Color.Red); graphics.FillRectangle(Brushes.Blue, 8, 0, 8, 8);
                image.Save(source, ImageFormat.Png);
                using (Bitmap fitted = IconEngine.FitSquare(image))
                {
                    Require(fitted.Width == 256 && fitted.Height == 256, "Tiny source did not enlarge to the icon canvas.");
                    VerifyTinyFit(fitted);
                }
            }
            string originalHash = Hash(source), folder = MakeFolder(fixture);
            Snapshot before = Capture(folder);
            RequireSuccess(IconEngine.Apply(folder, source, null, Backups(fixture), true), "tiny fit apply");
            VerifyIco(Path.Combine(folder, IconName), VerifyTinyFit);
            Undo(folder, fixture);
            EqualSnapshot(before, Capture(folder), "Tiny fit undo");
            Require(Hash(source) == originalHash, "Tiny fit changed source bytes.");
        }

        private static void VerifyTinyFit(Bitmap image)
        {
            int size = image.Width;
            Require(Near(image.GetPixel(size / 4, size / 2), Color.Red) && Near(image.GetPixel(size * 3 / 4, size / 2), Color.Blue),
                "Tiny source is not visibly enlarged in the icon.");
            Require(image.GetPixel(size / 2, size / 8).A == 0, "Tiny fit padding must be transparent.");
            VerifyContentBounds(image, size, size / 2);
        }

        private static void ApplyFit(string fixture)
        {
            string source = MakeFitImage(fixture, false), originalHash = Hash(source), folder = MakeFolder(fixture);
            SeedExisting(folder);
            Snapshot before = Capture(folder);
            RequireSuccess(IconEngine.Apply(folder, source, null, Backups(fixture), true), "fit apply");
            VerifyIco(Path.Combine(folder, IconName), delegate(Bitmap image) { VerifyFitBands(image, false); });
            Require(Hash(source) == originalHash, "Fit apply changed source bytes.");
            Undo(folder, fixture);
            EqualSnapshot(before, Capture(folder), "Fit undo");
            Require(Hash(source) == originalHash, "Fit undo changed source bytes.");
        }

        private static void ConflictingFit(string fixture)
        {
            string source = MakeFitImage(fixture, false), originalHash = Hash(source), folder = MakeFolder(fixture);
            SeedExisting(folder);
            Snapshot before = Capture(folder);
            using (Bitmap image = IconEngine.LoadOrientedImage(source))
            {
                bool rejected = false;
                try { using (Bitmap result = IconEngine.RenderIconImage(image, new Rectangle(0, 0, 100, 100), true)) { } }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Render must reject an explicit crop together with fit.");
            }
            Rejected(delegate { return IconEngine.Apply(folder, source, new Rectangle(0, 0, 100, 100), Backups(fixture), true); },
                "Apply must reject an explicit crop together with fit.");
            EqualSnapshot(before, Capture(folder), "Rejected conflicting fit");
            Require(Hash(source) == originalHash, "Rejected fit changed source bytes.");
            Require(!Directory.Exists(Backups(fixture)), "Rejected fit created backup state.");
        }

        private static void RasterFormat(string fixture, string extension, ImageFormat format)
        {
            string source = Path.Combine(fixture, "source." + extension);
            using (Bitmap bitmap = new Bitmap(400, 200))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Red);
                graphics.FillRectangle(Brushes.Blue, 200, 0, 200, 200);
                bitmap.Save(source, format);
            }
            VerifyFormat(fixture, source, 400, 200);
        }

        private static void IconInput(string fixture)
        {
            string source = Path.Combine(fixture, "source.ico");
            WriteIconFixture(source);
            VerifyFormat(fixture, source, 64, 64);
        }

        private static void WriteIconFixture(string source)
        {
            byte[] small = IconDib(16, Color.Lime, Color.Yellow);
            byte[] large = IconDib(64, Color.Red, Color.Blue);
            using (BinaryWriter writer = new BinaryWriter(File.Create(source)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)2);
                WriteIconEntry(writer, 16, small.Length, 38);
                WriteIconEntry(writer, 64, large.Length, 38 + small.Length);
                writer.Write(small); writer.Write(large);
            }
        }

        private static byte[] IconDib(int size, Color left, Color right)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int maskStride = ((size + 31) / 32) * 4;
                writer.Write(40); writer.Write(size); writer.Write(size * 2);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
                writer.Write(size * size * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                for (int row = 0; row < size; row++)
                    for (int column = 0; column < size; column++)
                    {
                        Color color = column < size / 2 ? left : right;
                        writer.Write(color.B); writer.Write(color.G); writer.Write(color.R); writer.Write((byte)255);
                    }
                writer.Write(new byte[maskStride * size]); writer.Flush(); return stream.ToArray();
            }
        }

        private static void WriteIconEntry(BinaryWriter writer, int size, int length, int offset)
        {
            writer.Write((byte)size); writer.Write((byte)size); writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(length); writer.Write(offset);
        }

        private static void OptionalFormat(string fixture, string extension, string encoded)
        {
            string source = Path.Combine(fixture, "source." + extension);
            File.WriteAllBytes(source, Convert.FromBase64String(encoded));
            string originalHash = Hash(source);
            string unavailable = null;
            try
            {
                using (Bitmap image = IconEngine.LoadOrientedImage(source))
                    Require(image.Width == 64 && image.Height == 64, extension + " decoded dimensions changed.");
            }
            catch (InvalidDataException exception) { unavailable = exception.Message; }
            if (unavailable == null)
            {
                VerifyFormat(fixture, source, 64, 64);
                Console.WriteLine("INFO: " + extension.ToUpperInvariant() + " decoded successfully with installed codecs.");
                return;
            }
            Require(unavailable.IndexOf("JPG", StringComparison.OrdinalIgnoreCase) >= 0 &&
                unavailable.IndexOf("PNG", StringComparison.OrdinalIgnoreCase) >= 0,
                extension + " rejection must explain the JPG/PNG alternative.");
            string folder = MakeFolder(fixture);
            SeedExisting(folder);
            Snapshot before = Capture(folder);
            Rejected(delegate { return IconEngine.Apply(folder, source, null, Backups(fixture)); }, extension + " unsupported input unexpectedly applied.");
            EqualSnapshot(before, Capture(folder), extension + " unsupported input");
            Require(Hash(source) == originalHash, extension + " rejection changed the source.");
            Console.WriteLine("INFO: " + extension.ToUpperInvariant() + " codec unavailable; rejected safely: " + unavailable);
        }

        private static void VerifyFormat(string fixture, string source, int width, int height)
        {
            string originalHash = Hash(source);
            using (Bitmap image = IconEngine.LoadOrientedImage(source))
            using (Bitmap cropped = IconEngine.CropSquare(image, null))
            {
                Require(image.Width == width && image.Height == height, "Decoded dimensions changed for " + source);
                Require(Near(image.GetPixel(width / 4, height / 2), Color.Red), "Decoded left pixels changed for " + source);
                Require(Near(image.GetPixel(width * 3 / 4, height / 2), Color.Blue), "Decoded right pixels changed for " + source);
                Require(cropped.Width == 256 && cropped.Height == 256, "Format crop dimensions changed.");
                Require(Near(cropped.GetPixel(64, 128), Color.Red) && Near(cropped.GetPixel(192, 128), Color.Blue), "Format crop pixels changed.");
            }
            using (FileStream exclusive = File.Open(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Require(exclusive.Length > 0, "Decoded source is empty.");
            string folder = MakeFolder(fixture);
            SeedExisting(folder);
            Snapshot before = Capture(folder);
            RequireSuccess(IconEngine.Apply(folder, source, null, Backups(fixture)), "format apply");
            VerifyIco(Path.Combine(folder, IconName));
            Require(Hash(source) == originalHash, "Apply changed format source bytes.");
            Undo(folder, fixture);
            EqualSnapshot(before, Capture(folder), "Format undo");
            Require(Hash(source) == originalHash, "Undo changed format source bytes.");
        }

        private static void ExifOrientation(string fixture)
        {
            string source = Path.Combine(fixture, "orientation-6.jpg");
            byte[] jpeg;
            using (Bitmap bitmap = new Bitmap(80, 40))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (MemoryStream encoded = new MemoryStream())
            {
                graphics.Clear(Color.Red); graphics.FillRectangle(Brushes.Blue, 40, 0, 40, 40);
                bitmap.Save(encoded, ImageFormat.Jpeg); jpeg = encoded.ToArray();
            }
            byte[] app1 = new byte[] { 255, 225, 0, 34, 69, 120, 105, 102, 0, 0,
                73, 73, 42, 0, 8, 0, 0, 0, 1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 };
            using (FileStream output = File.Create(source))
            {
                output.Write(jpeg, 0, 2); output.Write(app1, 0, app1.Length); output.Write(jpeg, 2, jpeg.Length - 2);
            }
            string originalHash = Hash(source);
            using (Bitmap oriented = IconEngine.LoadOrientedImage(source))
            {
                Require(oriented.Width == 40 && oriented.Height == 80, "EXIF orientation 6 must swap dimensions.");
                Require(Near(oriented.GetPixel(20, 15), Color.Red), "EXIF rotation must place red at top.");
                Require(Near(oriented.GetPixel(20, 65), Color.Blue), "EXIF rotation must place blue at bottom.");
            }
            Require(Hash(source) == originalHash, "Loading changed the EXIF source.");
        }

        private static void ApplyBehavior(string fixture)
        {
            string folder = MakeFolder(fixture), source = MakeImage(fixture, "source.png", Color.Red, Color.Blue);
            string originalHash = Hash(source);
            SeedExisting(folder);
            string unrelatedHash = Hash(Path.Combine(folder, "keep.txt"));
            OperationResult result = IconEngine.Apply(folder, source, null, Backups(fixture));
            RequireSuccess(result, "apply");
            Require(File.Exists(Path.Combine(folder, IconName)), "Apply did not create the icon.");
            Require(Directory.Exists(result.BackupDirectory), "Apply did not report an existing backup directory.");
            Require(Path.GetFullPath(result.BackupDirectory).StartsWith(Path.GetFullPath(Backups(fixture)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Backup escaped supplied root.");
            VerifyIco(Path.Combine(folder, IconName));
            string ini = File.ReadAllText(Path.Combine(folder, "desktop.ini"));
            Require(ini.IndexOf("[ViewState]", StringComparison.OrdinalIgnoreCase) >= 0 && ini.Contains("Mode=4"), "Apply discarded unrelated INI section/value.");
            Require(ini.Contains("LocalizedResourceName=KeepMe"), "Apply discarded unrelated ShellClassInfo value.");
            Require(ini.IndexOf(IconName, StringComparison.OrdinalIgnoreCase) >= 0, "INI does not reference the tool icon.");
            Require(Hash(source) == originalHash, "Apply changed source image bytes.");
            Require(Hash(Path.Combine(folder, "keep.txt")) == unrelatedHash, "Apply changed unrelated file.");
        }

        private static void RestoreExisting(string fixture)
        {
            string folder = MakeFolder(fixture); SeedExisting(folder);
            Snapshot before = Capture(folder);
            Apply(folder, MakeImage(fixture, "source.png", Color.Red, Color.Blue), fixture);
            Undo(folder, fixture);
            EqualSnapshot(before, Capture(folder), "Restoring existing metadata");
        }

        private static void UndoHistory(string fixture)
        {
            string folder = MakeFolder(fixture); SeedExisting(folder);
            Snapshot original = Capture(folder);
            Apply(folder, MakeImage(fixture, "first.png", Color.Red, Color.Blue), fixture);
            Snapshot afterFirst = Capture(folder);
            Apply(folder, MakeImage(fixture, "second.png", Color.Lime, Color.Yellow), fixture);
            Require(Hash(Path.Combine(folder, IconName)) != afterFirst.Files[IconName].Digest, "Second replacement did not change icon.");
            Undo(folder, fixture); EqualSnapshot(afterFirst, Capture(folder), "First undo");
            Undo(folder, fixture); EqualSnapshot(original, Capture(folder), "Second undo");
        }

        private static void RestoreNewFolder(string fixture)
        {
            string folder = MakeFolder(fixture);
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "original unrelated data");
            File.SetAttributes(Path.Combine(folder, "keep.txt"), FileAttributes.ReadOnly);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.Hidden);
            Snapshot before = Capture(folder);
            Apply(folder, MakeImage(fixture, "source.png", Color.Red, Color.Blue), fixture);
            string added = Path.Combine(folder, "created-after-apply.txt"); File.WriteAllText(added, "must survive undo");
            Undo(folder, fixture);
            Require(File.Exists(added) && File.ReadAllText(added) == "must survive undo", "Undo deleted unrelated new file.");
            Snapshot after = Capture(folder); after.Files.Remove("created-after-apply.txt");
            EqualSnapshot(before, after, "Undo on new folder");
        }

        private static void SourceInsideTarget(string fixture)
        {
            string folder = MakeFolder(fixture), source = MakeImage(folder, "portrait.png", Color.Red, Color.Blue);
            Snapshot before = Capture(folder);
            Apply(folder, source, fixture);
            Require(Hash(source) == before.Files["portrait.png"].Digest, "Apply changed source inside target.");
            Undo(folder, fixture); EqualSnapshot(before, Capture(folder), "Source-in-target undo");
        }

        private static void TargetIconAsSource(string fixture)
        {
            string folder = MakeFolder(fixture);
            SeedExisting(folder);
            string source = Path.Combine(folder, IconName);
            FileAttributes attributes = File.GetAttributes(source);
            File.SetAttributes(source, FileAttributes.Normal);
            WriteIconFixture(source);
            File.SetAttributes(source, attributes);
            using (Bitmap image = IconEngine.LoadOrientedImage(source))
            {
                Require(image.Width == 64 && image.Height == 64 && Near(image.GetPixel(16, 32), Color.Red),
                    "Self-source fixture must be a valid, decodable ICO.");
            }
            Snapshot before = Capture(folder);
            foreach (bool fitWholeImage in new bool[] { false, true })
            {
                bool rejected = false;
                try { IconEngine.Apply(folder, source, null, Backups(fixture), fitWholeImage); }
                catch (ArgumentException exception)
                {
                    Require(exception.ParamName == "imagePath", "Self-source rejection must identify the input image.");
                    rejected = true;
                }
                Require(rejected, "The target ICO must not be accepted as its own source.");
                EqualSnapshot(before, Capture(folder), "Rejected target-icon source");
                Require(!Directory.Exists(Backups(fixture)), "Rejected target-icon source created backup state.");
            }
        }

        private static void Conflict(string fixture, string name)
        {
            string folder = MakeFolder(fixture);
            Apply(folder, MakeImage(fixture, "source.png", Color.Red, Color.Blue), fixture);
            string changed = Path.Combine(folder, name); File.SetAttributes(changed, FileAttributes.Normal);
            File.AppendAllText(changed, "external modification");
            Snapshot before = Capture(folder);
            Rejected(delegate { return IconEngine.RestoreLast(folder, Backups(fixture)); }, "Undo must reject externally changed " + name);
            EqualSnapshot(before, Capture(folder), "Rejected conflicting undo");
        }

        private static void InvalidImage(string fixture)
        {
            string folder = MakeFolder(fixture); SeedExisting(folder);
            string invalid = Path.Combine(fixture, "invalid.png"); File.WriteAllText(invalid, "not an image");
            Snapshot before = Capture(folder);
            Rejected(delegate { return IconEngine.Apply(folder, invalid, null, Backups(fixture)); }, "Invalid image must be rejected.");
            EqualSnapshot(before, Capture(folder), "Rejected invalid image");
        }

        private static void InvalidCrop(string fixture)
        {
            string folder = MakeFolder(fixture); SeedExisting(folder);
            string source = MakeImage(fixture, "source.png", Color.Red, Color.Blue);
            Snapshot before = Capture(folder);
            Rejected(delegate { return IconEngine.Apply(folder, source, new Rectangle(-1, 0, 100, 100), Backups(fixture)); }, "Negative crop origin must be rejected.");
            EqualSnapshot(before, Capture(folder), "Rejected negative crop");
            Rejected(delegate { return IconEngine.Apply(folder, source, new Rectangle(0, 0, 999, 999), Backups(fixture)); }, "Oversized crop must be rejected.");
            EqualSnapshot(before, Capture(folder), "Rejected oversized crop");
        }

        private static void VerifyIco(string path)
        {
            VerifyIco(path, delegate(Bitmap decoded)
            {
                if (decoded.Width != 256) return;
                Require(Near(decoded.GetPixel(64, 128), Color.Red), "256-pixel ICO view lost left source pixels.");
                Require(Near(decoded.GetPixel(192, 128), Color.Blue), "256-pixel ICO view lost right source pixels.");
            });
        }

        private static void VerifyIco(string path, Action<Bitmap> verifyFrame)
        {
            byte[] contents = File.ReadAllBytes(path);
            using (BinaryReader reader = new BinaryReader(new MemoryStream(contents)))
            {
                Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header.");
                int count = reader.ReadUInt16(); Require(count >= 7 && count <= 32, "Unexpected ICO frame count.");
                HashSet<int> sizes = new HashSet<int>();
                for (int index = 0; index < count; index++)
                {
                    byte[] entry = reader.ReadBytes(16); Require(entry.Length == 16, "Truncated ICO directory.");
                    int width = entry[0] == 0 ? 256 : entry[0], height = entry[1] == 0 ? 256 : entry[1];
                    int length = BitConverter.ToInt32(entry, 8), offset = BitConverter.ToInt32(entry, 12);
                    Require(width == height, "ICO frame is not square.");
                    Require(length > 0 && offset >= 6 + count * 16 && (long)offset + length <= contents.Length, "ICO frame points outside file.");
                    sizes.Add(width); byte[] frame = new byte[length]; Buffer.BlockCopy(contents, offset, frame, 0, length);
                    using (Bitmap decoded = DecodeFrame(entry, frame))
                    {
                        Require(decoded.Width == width && decoded.Height == height, "ICO decoded dimensions disagree with directory.");
                        verifyFrame(decoded);
                    }
                }
                foreach (int size in new int[] { 16, 24, 32, 48, 64, 128, 256 }) Require(sizes.Contains(size), "ICO is missing size " + size);
            }
        }

        private static Bitmap DecodeFrame(byte[] entry, byte[] frame)
        {
            if (frame.Length >= 8 && frame[0] == 137 && frame[1] == 80 && frame[2] == 78 && frame[3] == 71)
            {
                using (MemoryStream imageStream = new MemoryStream(frame))
                using (Bitmap decoded = new Bitmap(imageStream)) return new Bitmap(decoded);
            }
            using (MemoryStream iconStream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(iconStream))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write(entry, 0, 12); writer.Write(22); writer.Write(frame); writer.Flush(); iconStream.Position = 0;
                using (Icon icon = new Icon(iconStream)) return icon.ToBitmap();
            }
        }

        private static string MakeFolder(string fixture)
        {
            string folder = Path.Combine(fixture, "target"); Directory.CreateDirectory(folder); return folder;
        }
        private static string Backups(string fixture) { return Path.Combine(fixture, "b"); }
        private static void Apply(string folder, string source, string fixture) { RequireSuccess(IconEngine.Apply(folder, source, null, Backups(fixture)), "apply"); }
        private static void Undo(string folder, string fixture) { RequireSuccess(IconEngine.RestoreLast(folder, Backups(fixture)), "undo"); }

        private static string MakeImage(string fixture, string name, Color left, Color right)
        {
            string path = Path.Combine(fixture, name);
            using (Bitmap bitmap = new Bitmap(400, 200))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush rightBrush = new SolidBrush(right))
            {
                graphics.Clear(left); graphics.FillRectangle(rightBrush, 200, 0, 200, 200); bitmap.Save(path, ImageFormat.Png);
            }
            return path;
        }

        private static void SeedExisting(string folder)
        {
            string ini = Path.Combine(folder, "desktop.ini"), icon = Path.Combine(folder, IconName);
            File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=legacy.ico,0\r\nLocalizedResourceName=KeepMe\r\n[ViewState]\r\nMode=4\r\n", Encoding.Unicode);
            File.WriteAllBytes(icon, new byte[] { 7, 19, 2, 53, 0, 255 });
            File.WriteAllText(Path.Combine(folder, "keep.txt"), "Unrelated data must not change.");
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
            File.SetAttributes(icon, FileAttributes.Hidden | FileAttributes.ReadOnly);
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly | FileAttributes.System);
        }

        private sealed class FileState { public string Digest; public FileAttributes Attributes; }
        private sealed class Snapshot
        {
            public readonly Dictionary<string, FileState> Files = new Dictionary<string, FileState>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, FileAttributes> Directories = new Dictionary<string, FileAttributes>(StringComparer.OrdinalIgnoreCase);
        }
        private static Snapshot Capture(string folder)
        {
            Snapshot snapshot = new Snapshot(); snapshot.Directories.Add(".", File.GetAttributes(folder));
            string prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string path in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)) snapshot.Directories.Add(path.Substring(prefix.Length), File.GetAttributes(path));
            foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) snapshot.Files.Add(path.Substring(prefix.Length), new FileState { Digest = Hash(path), Attributes = File.GetAttributes(path) });
            return snapshot;
        }
        private static void EqualSnapshot(Snapshot expected, Snapshot actual, string context)
        {
            Require(expected.Files.Count == actual.Files.Count, context + ": file count changed.");
            Require(expected.Directories.Count == actual.Directories.Count, context + ": directory count changed.");
            foreach (KeyValuePair<string, FileAttributes> directory in expected.Directories)
                Require(actual.Directories.ContainsKey(directory.Key) && actual.Directories[directory.Key] == directory.Value, context + ": directory attributes changed for " + directory.Key);
            foreach (KeyValuePair<string, FileState> file in expected.Files)
            {
                Require(actual.Files.ContainsKey(file.Key), context + ": missing file " + file.Key);
                Require(actual.Files[file.Key].Digest == file.Value.Digest, context + ": bytes changed for " + file.Key);
                Require(actual.Files[file.Key].Attributes == file.Value.Attributes, context + ": attributes changed for " + file.Key);
            }
        }
        private static string Hash(string path)
        {
            using (SHA256 digest = SHA256.Create())
            using (FileStream stream = File.OpenRead(path)) return BitConverter.ToString(digest.ComputeHash(stream));
        }
        private static bool Near(Color actual, Color expected)
        {
            return actual.A >= 240 && Math.Abs(actual.R - expected.R) < 35 && Math.Abs(actual.G - expected.G) < 35 && Math.Abs(actual.B - expected.B) < 35;
        }
        private static void RequireSuccess(OperationResult result, string operation)
        {
            Require(result != null && result.Success, operation + " failed: " + (result == null ? "no result" : result.Message));
        }
        private static void Rejected(Func<OperationResult> action, string message)
        {
            bool rejected;
            try { OperationResult result = action(); rejected = result != null && !result.Success; }
            catch (Exception) { rejected = true; }
            Require(rejected, message);
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
