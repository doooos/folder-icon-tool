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

            string badCrop = Path.Combine(output, "bad-crop.json");
            Require(Run("apply", "--folder", folder, "--image", picture,
                "--crop", "9999,9999,10", "--backups", backups,
                "--result", badCrop) == 1, "An out-of-bounds crop must fail.");
            Require(!ReadSuccess(badCrop), "Invalid crop must return a failed result.");
            AssertUnchangedFolder();
            Console.WriteLine("PASS invalid crop is nonmutating");

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
            Console.WriteLine("RESULT 6 passed, 0 failed");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
