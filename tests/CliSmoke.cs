using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

internal static class CliSmoke
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes,
        out SHFILEINFO info, uint size, uint flags);

    private static string executable;
    private static string output;
    private static string folder;
    private static string picture;
    private static string backups;
    private static FileAttributes originalAttributes;
    private static byte[] originalImage;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Supply executable and output directory.");
            executable = Path.GetFullPath(args[0]);
            output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(output);
            folder = Path.Combine(output, "中文 folder");
            picture = Path.Combine(output, "横向 picture.png");
            backups = Path.Combine(output, "backups");
            Directory.CreateDirectory(folder);
            originalAttributes = File.GetAttributes(folder);
            using (var bitmap = new Bitmap(600, 300))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.CornflowerBlue);
                graphics.FillRectangle(Brushes.Gold, 0, 0, 50, 300);
                graphics.FillRectangle(Brushes.OrangeRed, 550, 0, 50, 300);
                bitmap.Save(picture, ImageFormat.Png);
            }
            originalImage = File.ReadAllBytes(picture);

            string applied = Path.Combine(output, "apply.json");
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--backups", backups, "--result", applied) == 0, "Apply exit code.");
            Require(ReadSuccess(applied), "Apply JSON result.");
            string iconPath = Path.Combine(folder, ".folder-portrait.ico");
            Require(File.Exists(iconPath), "The icon must exist.");
            Require(File.Exists(Path.Combine(folder, "desktop.ini")), "The INI must exist.");
            AssertOriginalImage();

            SHFILEINFO shellInfo;
            IntPtr shellResult = SHGetFileInfo(folder, 0, out shellInfo,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)), 0x1000);
            Require(shellResult != IntPtr.Zero, "Windows Shell could not read the folder icon.");
            Require(String.Equals(Path.GetFullPath(shellInfo.szDisplayName), iconPath,
                StringComparison.OrdinalIgnoreCase), "Windows Shell must resolve the actual custom icon.");
            Require(shellInfo.iIcon == 0, "The custom icon index must be zero.");
            Console.WriteLine("PASS packaged CLI apply and Windows Shell icon resolution");

            string undone = Path.Combine(output, "undo.json");
            Require(Run("undo", "--folder", folder, "--backups", backups,
                "--result", undone) == 0, "Undo exit code.");
            Require(ReadSuccess(undone), "Undo JSON result.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS packaged CLI undo and original file preservation");

            string rectangular = Path.Combine(output, "rectangular.json");
            Require(Run("apply", "--folder", folder, "--image", picture, "--crop", "50,0,500,250",
                "--backups", backups, "--result", rectangular) == 0 && ReadSuccess(rectangular),
                "Four-part rectangular crop must apply successfully.");
            Require(Run("undo", "--folder", folder, "--backups", backups) == 0, "Rectangular crop undo.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS rectangular crop CLI and undo");

            string square = Path.Combine(output, "square.json");
            Require(Run("apply", "--folder", folder, "--image", picture, "--crop", "50,0,250",
                "--backups", backups, "--result", square) == 0 && ReadSuccess(square),
                "Legacy three-part square crop must remain supported.");
            Require(Run("undo", "--folder", folder, "--backups", backups) == 0, "Legacy crop undo.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS legacy square crop CLI and undo");

            string rotated = Path.Combine(output, "rotated.json");
            Require(Run("apply", "--folder", folder, "--image", picture, "--rotate", "90",
                "--crop", "0,0,300", "--backups", backups, "--result", rotated) == 0 && ReadSuccess(rotated),
                "Clockwise rotation with a post-rotation crop must apply successfully.");
            using (Bitmap rendered = ReadIconFrame(Path.Combine(folder, ".folder-portrait.ico"), 256))
            {
                Require(rendered.Width == 256 && rendered.Height == 256, "Rotated CLI icon must expose its 256-pixel frame.");
                Require(Near(rendered.GetPixel(128, 16), Color.Gold) &&
                    Near(rendered.GetPixel(128, 240), Color.CornflowerBlue),
                    "The CLI crop must use coordinates from the clockwise-rotated image.");
            }
            AssertOriginalImage();
            Require(Run("undo", "--folder", folder, "--backups", backups) == 0, "Rotated crop undo.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS clockwise rotation CLI, crop coordinates and source preservation");

            string badCrop = Path.Combine(output, "bad-crop.json");
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--crop", "9999,9999,10", "--backups", backups,
                "--result", badCrop) == 1, "An out-of-bounds crop must fail.");
            Require(!ReadSuccess(badCrop), "Invalid crop must return a failed result.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS invalid crop is nonmutating");

            string invalidBackups = Path.Combine(output, "invalid-rotation-backups");
            foreach (string angle in new string[] { "45", "360", "-90", "090", "+90", "90x", "" })
            {
                string invalidReport = Path.Combine(output, "invalid-rotation-" +
                    (angle.Length == 0 ? "empty" : angle.Replace('+', 'p').Replace('-', 'm')) + ".json");
                Require(Run("apply", "--folder", folder, "--image", picture, "--rotate", angle,
                    "--backups", invalidBackups, "--result", invalidReport) == 1,
                    "Unsupported rotation angle must fail: " + angle);
                Require(!File.Exists(invalidReport), "Invalid rotation must be rejected before reserving a result file.");
                Require(!Directory.Exists(invalidBackups), "Invalid rotation must not create backups.");
                AssertUnchangedFolder();
            }
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--backups", invalidBackups, "--result", Path.Combine(output, "missing-rotation.json"),
                "--rotate") == 1, "A missing rotation value must fail.");
            Require(!Directory.Exists(invalidBackups), "Missing rotation value must not create backups.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS invalid rotation values are rejected without writes");

            string existingReport = Path.Combine(output, "existing.json");
            File.WriteAllText(existingReport, "retain this report", Encoding.UTF8);
            byte[] reportBefore = File.ReadAllBytes(existingReport);
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--backups", backups, "--result", existingReport) == 1,
                "An existing result path must be rejected.");
            Require(Equal(reportBefore, File.ReadAllBytes(existingReport)),
                "An existing report must not be overwritten.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS existing report protection");

            string insideReport = Path.Combine(folder, "inside.json");
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--backups", backups, "--result", insideReport) == 1,
                "A report inside the target must be rejected.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS report inside target is rejected before mutation");

            string missingParent = Path.Combine(Path.Combine(output, "missing"), "result.json");
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--backups", backups, "--result", missingParent) == 1,
                "A missing report parent must be rejected.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS missing report directory is rejected before mutation");
            Console.WriteLine("RESULT 10 passed, 0 failed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int Run(params string[] args)
    {
        string[] quoted = new string[args.Length];
        for (int i = 0; i < args.Length; i++) quoted[i] = QuoteArgument(args[i]);
        var start = new ProcessStartInfo(executable, String.Join(" ", quoted));
        start.WorkingDirectory = output;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.WindowStyle = ProcessWindowStyle.Hidden;
        using (Process process = Process.Start(start))
        {
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new TimeoutException("The test's CLI process did not exit within 30 seconds.");
            }
            return process.ExitCode;
        }
    }

    private static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1);
                result.Append('"');
            }
            else
            {
                result.Append('\\', slashes);
                result.Append(character);
            }
            slashes = 0;
        }
        result.Append('\\', slashes * 2);
        result.Append('"');
        return result.ToString();
    }

    private static bool ReadSuccess(string path)
    {
        var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
            File.ReadAllText(path, Encoding.UTF8));
        return result.ContainsKey("Success") && (bool)result["Success"];
    }

    private static void AssertUnchangedFolder()
    {
        Require(Directory.GetFileSystemEntries(folder).Length == 0, "The target must remain empty.");
        Require(File.GetAttributes(folder) == originalAttributes, "Original folder attributes must remain.");
        AssertOriginalImage();
    }

    private static void AssertOriginalImage()
    {
        Require(Equal(originalImage, File.ReadAllBytes(picture)), "Original image bytes must remain.");
        using (var stream = new FileStream(picture, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Require(stream.Length == originalImage.Length, "The source image must be unlocked.");
    }

    private static bool Equal(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        for (int i = 0; i < first.Length; i++)
            if (first[i] != second[i]) return false;
        return true;
    }

    private static bool Near(Color actual, Color expected)
    {
        return actual.A >= 240 && Math.Abs(actual.R - expected.R) < 35 &&
            Math.Abs(actual.G - expected.G) < 35 && Math.Abs(actual.B - expected.B) < 35;
    }

    private static Bitmap ReadIconFrame(string path, int requestedSize)
    {
        byte[] contents = File.ReadAllBytes(path);
        using (BinaryReader reader = new BinaryReader(new MemoryStream(contents)))
        {
            Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1, "Invalid ICO header.");
            int count = reader.ReadUInt16();
            for (int index = 0; index < count; index++)
            {
                byte[] entry = reader.ReadBytes(16);
                Require(entry.Length == 16, "Truncated ICO directory.");
                int width = entry[0] == 0 ? 256 : entry[0];
                int height = entry[1] == 0 ? 256 : entry[1];
                int length = BitConverter.ToInt32(entry, 8);
                int offset = BitConverter.ToInt32(entry, 12);
                Require(length > 0 && offset >= 6 + count * 16 &&
                    (long)offset + length <= contents.Length, "ICO frame points outside file.");
                if (width != requestedSize || height != requestedSize) continue;

                byte[] frame = new byte[length];
                Buffer.BlockCopy(contents, offset, frame, 0, length);
                if (frame.Length >= 8 && frame[0] == 137 && frame[1] == 80 &&
                    frame[2] == 78 && frame[3] == 71)
                {
                    using (MemoryStream imageStream = new MemoryStream(frame))
                    using (Bitmap decoded = new Bitmap(imageStream))
                        return new Bitmap(decoded);
                }

                using (MemoryStream iconStream = new MemoryStream())
                using (BinaryWriter writer = new BinaryWriter(iconStream))
                {
                    writer.Write((ushort)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)1);
                    writer.Write(entry, 0, 12);
                    writer.Write(22);
                    writer.Write(frame);
                    writer.Flush();
                    iconStream.Position = 0;
                    using (Icon icon = new Icon(iconStream)) return icon.ToBitmap();
                }
            }
        }
        throw new InvalidDataException("ICO is missing a " + requestedSize + "-pixel frame.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
