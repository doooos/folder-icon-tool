using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;

namespace FolderIconTool
{
    public static class IconEngine
    {
        private const string IconName = ".folder-portrait.ico";
        private const string IniName = "desktop.ini";
        private const FileAttributes IconAttributes = FileAttributes.Hidden | FileAttributes.System;

        public static Bitmap LoadOrientedImage(string imagePath)
        {
            if (String.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException("Choose an image file.", "imagePath");
            byte[] bytes = File.ReadAllBytes(imagePath);
            bool isIcon = bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0;
            Exception firstFailure = null;
            if (!isIcon)
            {
                try { return LoadGdiImage(bytes); }
                catch (Exception failure)
                {
                    if (!(failure is ArgumentException) && !(failure is ExternalException) && !(failure is OutOfMemoryException)) throw;
                    firstFailure = failure;
                }
            }
            try { return LoadWicImage(bytes); }
            catch (Exception failure)
            {
                throw new InvalidDataException("无法读取这张图片。文件可能已损坏，或 Windows 未安装对应的图片解码器。请改用 JPG/PNG，或安装所需的 Windows 图片扩展后重试。",
                    firstFailure == null ? failure : new AggregateException(firstFailure, failure));
            }
        }

        private static Bitmap LoadGdiImage(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes, false))
            using (Image source = Image.FromStream(stream, true, true))
            {
                if (Array.IndexOf(source.PropertyIdList, 0x112) >= 0)
                {
                    byte[] orientationBytes = source.GetPropertyItem(0x112).Value;
                    ApplyOrientation(source, orientationBytes.Length >= 2 ? BitConverter.ToUInt16(orientationBytes, 0) : 1);
                }
                Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
                try
                {
                    using (Graphics graphics = Graphics.FromImage(result))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(source, new Rectangle(0, 0, result.Width, result.Height),
                            0, 0, source.Width, source.Height, GraphicsUnit.Pixel);
                    }
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }

        private static Bitmap LoadWicImage(byte[] bytes)
        {
            using (MemoryStream stream = new MemoryStream(bytes, false))
            {
                BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0) throw new InvalidDataException("图片没有可读取的画面。");
                BitmapFrame frame = decoder.Frames[0];
                if (decoder is IconBitmapDecoder)
                    foreach (BitmapFrame candidate in decoder.Frames)
                        if ((long)candidate.PixelWidth * candidate.PixelHeight > (long)frame.PixelWidth * frame.PixelHeight)
                            frame = candidate;
                int orientation = ReadWicOrientation(frame);
                FormatConvertedBitmap converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                Bitmap result = new Bitmap(converted.PixelWidth, converted.PixelHeight, PixelFormat.Format32bppArgb);
                try
                {
                    BitmapData pixels = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
                        ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        converted.CopyPixels(System.Windows.Int32Rect.Empty, pixels.Scan0,
                            checked(pixels.Stride * result.Height), pixels.Stride);
                    }
                    finally { result.UnlockBits(pixels); }
                    ApplyOrientation(result, orientation);
                    return result;
                }
                catch { result.Dispose(); throw; }
            }
        }

        private static int ReadWicOrientation(BitmapFrame frame)
        {
            BitmapMetadata metadata;
            try { metadata = frame.Metadata as BitmapMetadata; }
            catch (NotSupportedException) { return 1; }
            catch (COMException) { return 1; }
            if (metadata == null) return 1;
            string[] queries = { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}", "/xmp/tiff:Orientation" };
            foreach (string query in queries)
            {
                try
                {
                    object value = metadata.GetQuery(query);
                    if (value == null) continue;
                    int orientation = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
                    if (orientation >= 1 && orientation <= 8) return orientation;
                }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (COMException) { }
                catch (FormatException) { }
                catch (OverflowException) { }
            }
            return 1;
        }

        private static void ApplyOrientation(Image image, int orientation)
        {
            switch (orientation)
            {
                case 2: image.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                case 3: image.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                case 4: image.RotateFlip(RotateFlipType.Rotate180FlipX); break;
                case 5: image.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                case 6: image.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                case 7: image.RotateFlip(RotateFlipType.Rotate270FlipX); break;
                case 8: image.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
            }
        }

        public static Bitmap CropSquare(Bitmap image, Rectangle? crop)
        {
            if (image == null) throw new ArgumentNullException("image");
            int side = Math.Min(image.Width, image.Height);
            Rectangle area = crop ?? new Rectangle((image.Width - side) / 2, (image.Height - side) / 2, side, side);
            if (area.Width <= 0 || area.Width != area.Height || area.X < 0 || area.Y < 0 ||
                (long)area.X + area.Width > image.Width || (long)area.Y + area.Height > image.Height)
                throw new ArgumentException("The crop must be a nonempty square inside the oriented image.", "crop");
            return Resize(image, area, 256);
        }

        public static OperationResult Apply(string folder, string imagePath, Rectangle? crop, string backupRoot)
        {
            return Apply(folder, imagePath, crop, backupRoot, false);
        }

        public static Bitmap FitSquare(Bitmap image)
        {
            if (image == null) throw new ArgumentNullException("image");
            double scale = 256.0 / Math.Max(image.Width, image.Height);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            Rectangle destination = new Rectangle((256 - width) / 2, (256 - height) / 2, width, height);
            Bitmap result = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destination, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static Bitmap RenderIconImage(Bitmap image, Rectangle? crop, bool fitWholeImage)
        {
            if (fitWholeImage && crop.HasValue)
                throw new ArgumentException("保留整张图片时不能同时指定裁剪区域。", "crop");
            return fitWholeImage ? FitSquare(image) : CropSquare(image, crop);
        }

        public static OperationResult Apply(string folder, string imagePath, Rectangle? crop, string backupRoot, bool fitWholeImage)
        {
            if (fitWholeImage && crop.HasValue)
                throw new ArgumentException("保留整张图片时不能同时指定裁剪区域。", "crop");
            folder = ValidateFolder(folder);
            if (String.IsNullOrWhiteSpace(imagePath)) throw new ArgumentException("请选择图片文件。", "imagePath");
            imagePath = Path.GetFullPath(imagePath);
            if (String.Equals(imagePath, Path.Combine(folder, IconName), StringComparison.OrdinalIgnoreCase) ||
                String.Equals(imagePath, Path.Combine(folder, IniName), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("不能将目标文件夹的 .folder-portrait.ico 或 desktop.ini 用作源图片。请先复制为其他文件名，再选择副本。", "imagePath");
            using (FolderLock operationLock = new FolderLock(folder))
            using (Bitmap image = LoadOrientedImage(imagePath))
            using (Bitmap square = RenderIconImage(image, crop, fitWholeImage))
            {
                string root = ValidateBackupRoot(backupRoot, folder);
                FileState originalIcon = Capture(Path.Combine(folder, IconName));
                FileState originalIni = Capture(Path.Combine(folder, IniName));
                int originalFolderAttributes = (int)File.GetAttributes(folder);
                byte[] iconBytes = MakeIcon(square);
                byte[] iniBytes = MakeIni(originalIni.Data);
                string bucket = Path.Combine(root, FolderHash(folder));
                EnsureDirectory(bucket);
                string operationDirectory = Path.Combine(bucket,
                    DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                EnsureDirectory(operationDirectory);
                SaveBackup(operationDirectory, "icon.before.bin", originalIcon);
                SaveBackup(operationDirectory, "desktop.before.bin", originalIni);
                Journal journal = new Journal
                {
                    Version = 1, Folder = folder, Status = "Prepared",
                    BeforeFolderAttributes = originalFolderAttributes,
                    BeforeIcon = originalIcon, BeforeIni = originalIni,
                    AfterIcon = Expected(iconBytes, DesiredAttributes(originalIcon)),
                    AfterIni = Expected(iniBytes, DesiredAttributes(originalIni)),
                    AfterFolderAttributes = originalFolderAttributes | (int)FileAttributes.ReadOnly
                };
                WriteJournal(operationDirectory, journal);
                bool changesStarted = false;
                try
                {
                    AssertCurrent(folder, originalIcon, originalIni, originalFolderAttributes);
                    changesStarted = true;
                    PutFile(Path.Combine(folder, IconName), iconBytes, journal.AfterIcon.Attributes);
                    PutFile(Path.Combine(folder, IniName), iniBytes, journal.AfterIni.Attributes);
                    File.SetAttributes(folder, (FileAttributes)journal.AfterFolderAttributes);
                    AssertCurrent(folder, journal.AfterIcon, journal.AfterIni, journal.AfterFolderAttributes);
                    journal.Status = "Applied";
                    WriteJournal(operationDirectory, journal);
                }
                catch (Exception failure)
                {
                    if (changesStarted)
                        RollBackOrThrow(folder, originalIcon, originalIni, originalFolderAttributes,
                            operationDirectory, journal, "RolledBack", failure);
                    throw new IOException("Folder icon was not applied: " + failure.Message +
                        " Backup: " + operationDirectory, failure);
                }
                Notify(folder);
                return Result("apply", folder, operationDirectory, "文件夹头像已设置，原状态已备份。");
            }
        }

        public static OperationResult RestoreLast(string folder, string backupRoot)
        {
            folder = ValidateFolder(folder);
            using (FolderLock operationLock = new FolderLock(folder))
            {
                string root = ValidateBackupRoot(backupRoot, folder);
                string bucket = Path.Combine(root, FolderHash(folder));
                if (!Directory.Exists(bucket)) throw new InvalidOperationException("No icon backup exists for this folder.");
                ValidateNoReparseAncestors(bucket);
                string[] directories = Directory.GetDirectories(bucket);
                Array.Sort(directories, StringComparer.Ordinal);
                for (int index = directories.Length - 1; index >= 0; index--)
                {
                    string operationDirectory = directories[index];
                    ValidateNoReparseAncestors(operationDirectory);
                    string manifest = Path.Combine(operationDirectory, "journal.json");
                    if (!File.Exists(manifest)) continue;
                    Capture(manifest);
                    Journal journal = new JavaScriptSerializer().Deserialize<Journal>(File.ReadAllText(manifest, Encoding.UTF8));
                    ValidateJournal(journal, folder);
                    if (journal.Status == "Restored" || journal.Status == "RolledBack") continue;
                    if (journal.Status != "Applied")
                        throw new InvalidOperationException("An incomplete icon operation needs inspection before undo: " + operationDirectory);
                    AssertCurrent(folder, journal.AfterIcon, journal.AfterIni, journal.AfterFolderAttributes);
                    LoadBackup(operationDirectory, "icon.before.bin", journal.BeforeIcon);
                    LoadBackup(operationDirectory, "desktop.before.bin", journal.BeforeIni);
                    FileState currentIcon = Capture(Path.Combine(folder, IconName));
                    FileState currentIni = Capture(Path.Combine(folder, IniName));
                    int currentFolderAttributes = (int)File.GetAttributes(folder);
                    try
                    {
                        RestoreFiles(folder, journal.BeforeIcon, journal.BeforeIni, journal.BeforeFolderAttributes);
                        AssertCurrent(folder, journal.BeforeIcon, journal.BeforeIni, journal.BeforeFolderAttributes);
                        journal.Status = "Restored";
                        WriteJournal(operationDirectory, journal);
                    }
                    catch (Exception failure)
                    {
                        RollBackOrThrow(folder, currentIcon, currentIni, currentFolderAttributes,
                            operationDirectory, journal, "Applied", failure);
                        throw new IOException("Undo failed; the applied icon was restored. " + failure.Message, failure);
                    }
                    Notify(folder);
                    return Result("restore", folder, operationDirectory, "已恢复上一次文件夹图标和属性。");
                }
                throw new InvalidOperationException("No applied icon operation remains to undo for this folder.");
            }
        }

        private static Bitmap Resize(Image image, Rectangle source, int size)
        {
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                using (ImageAttributes attributes = new ImageAttributes())
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.CompositingQuality = CompositingQuality.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    attributes.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, new Rectangle(0, 0, size, size), source.X, source.Y,
                        source.Width, source.Height, GraphicsUnit.Pixel, attributes);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static byte[] MakeIcon(Bitmap square)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
            List<byte[]> images = new List<byte[]>();
            foreach (int size in sizes)
            {
                using (Bitmap resized = Resize(square, new Rectangle(0, 0, square.Width, square.Height), size))
                using (MemoryStream imageStream = new MemoryStream())
                {
                    resized.Save(imageStream, ImageFormat.Png);
                    images.Add(imageStream.ToArray());
                }
            }
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int index = 0; index < sizes.Length; index++)
                {
                    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    writer.Write((byte)(sizes[index] == 256 ? 0 : sizes[index]));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(images[index].Length);
                    writer.Write(offset);
                    offset += images[index].Length;
                }
                foreach (byte[] bytes in images) writer.Write(bytes);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static byte[] MakeIni(byte[] original)
        {
            string text = DecodeIni(original);
            string[] lines = Regex.Split(text, "\\r\\n|\\r|\\n");
            List<string> result = new List<string>();
            bool inShell = false;
            bool foundShell = false;
            int count = lines.Length;
            if (count > 0 && lines[count - 1].Length == 0) count--;
            for (int index = 0; index < count; index++)
            {
                string line = lines[index];
                if (Regex.IsMatch(line, @"^\s*\[.*\]\s*$"))
                {
                    if (inShell) AddIconKeys(result);
                    inShell = String.Equals(line.Trim(), "[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    foundShell |= inShell;
                }
                if (inShell && Regex.IsMatch(line, @"^\s*(IconResource|IconFile|IconIndex)\s*=", RegexOptions.IgnoreCase)) continue;
                result.Add(line);
            }
            if (inShell) AddIconKeys(result);
            if (!foundShell)
            {
                if (result.Count > 0 && result[result.Count - 1].Length > 0) result.Add("");
                result.Add("[.ShellClassInfo]");
                AddIconKeys(result);
            }
            byte[] body = Encoding.Unicode.GetBytes(String.Join("\r\n", result.ToArray()) + "\r\n");
            byte[] preamble = Encoding.Unicode.GetPreamble();
            byte[] output = new byte[preamble.Length + body.Length];
            Buffer.BlockCopy(preamble, 0, output, 0, preamble.Length);
            Buffer.BlockCopy(body, 0, output, preamble.Length, body.Length);
            return output;
        }

        private static void AddIconKeys(List<string> lines)
        {
            lines.Add("IconResource=" + IconName + ",0");
            lines.Add("IconFile=" + IconName);
            lines.Add("IconIndex=0");
        }

        private static string ValidateFolder(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Choose an existing folder.", "folder");
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string driveRoot = Path.GetPathRoot(Path.GetFullPath(folder)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Drive and share roots cannot be customized.", "folder");
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder does not exist: " + full);
            ValidateNoReparseAncestors(full);
            ValidateRegularFileIfPresent(Path.Combine(full, IconName));
            ValidateRegularFileIfPresent(Path.Combine(full, IniName));
            return full;
        }

        private static string ValidateBackupRoot(string backupRoot, string folder)
        {
            string full = Path.GetFullPath(String.IsNullOrWhiteSpace(backupRoot) ? ToolPaths.BackupRoot : backupRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(full, folder, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The backup directory must be outside the target folder.", "backupRoot");
            ValidateNoReparseAncestors(full);
            return full;
        }

        private static void ValidateNoReparseAncestors(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(path);
            while (directory != null)
            {
                FileAttributes attributes;
                if (TryAttributes(directory.FullName, out attributes))
                {
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Reparse points and linked paths are not allowed: " + directory.FullName);
                    if ((attributes & FileAttributes.Directory) == 0)
                        throw new IOException("A parent path is not a directory: " + directory.FullName);
                }
                directory = directory.Parent;
            }
        }

        private static bool TryAttributes(string path, out FileAttributes attributes)
        {
            try { attributes = File.GetAttributes(path); return true; }
            catch (FileNotFoundException) { attributes = 0; return false; }
            catch (DirectoryNotFoundException) { attributes = 0; return false; }
        }

        private static void ValidateRegularFileIfPresent(string path)
        {
            FileAttributes attributes;
            if (TryAttributes(path, out attributes) &&
                (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new IOException("Refusing to replace a directory, link, or special file: " + path);
        }

        private static void ValidateSingleLink(FileStream stream, string path)
        {
            FileInformation information;
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out information))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Cannot inspect file: " + path);
            if (information.NumberOfLinks != 1 || (information.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked files cannot be modified or used as backups: " + path);
        }

        private static FileState Capture(string path)
        {
            ValidateRegularFileIfPresent(path);
            FileAttributes attributes;
            if (!TryAttributes(path, out attributes)) return new FileState();
            byte[] data;
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ValidateSingleLink(stream, path);
                using (MemoryStream buffer = new MemoryStream()) { stream.CopyTo(buffer); data = buffer.ToArray(); }
            }
            return new FileState { Exists = true, Attributes = (int)attributes, Sha256 = Hash(data), Data = data };
        }

        private static FileState Expected(byte[] bytes, int attributes)
        {
            return new FileState { Exists = true, Attributes = attributes, Sha256 = Hash(bytes) };
        }

        private static int DesiredAttributes(FileState before)
        {
            return ((before.Exists ? before.Attributes : 0) & ~(int)FileAttributes.Normal) | (int)IconAttributes;
        }

        private static void AssertCurrent(string folder, FileState icon, FileState ini, int folderAttributes)
        {
            ValidateFolder(folder);
            AssertFile(Path.Combine(folder, IconName), icon);
            AssertFile(Path.Combine(folder, IniName), ini);
            if ((int)File.GetAttributes(folder) != folderAttributes)
                throw new IOException("Folder attributes changed. No overwrite is allowed: " + folder);
        }

        private static void AssertFile(string path, FileState expected)
        {
            FileState actual = Capture(path);
            if (actual.Exists != expected.Exists || (actual.Exists &&
                (!String.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase) || actual.Attributes != expected.Attributes)))
                throw new IOException("The file changed since the recorded operation. Refusing to overwrite: " + path);
        }

        private static void PutFile(string path, byte[] bytes, int attributes)
        {
            ValidateNoReparseAncestors(Path.GetDirectoryName(path));
            ValidateRegularFileIfPresent(path);
            FileAttributes previous;
            bool exists = TryAttributes(path, out previous);
            if (exists) File.SetAttributes(path, previous & ~FileAttributes.ReadOnly);
            try
            {
                using (FileStream stream = new FileStream(path, exists ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    ValidateSingleLink(stream, path);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.SetLength(bytes.Length);
                    stream.Flush(true);
                }
                File.SetAttributes(path, (FileAttributes)attributes);
            }
            catch
            {
                if (exists && File.Exists(path)) File.SetAttributes(path, previous);
                throw;
            }
        }

        private static void RestoreFile(string path, FileState state)
        {
            FileState current = Capture(path);
            if (!current.Exists && !state.Exists) return;
            if (current.Exists && state.Exists && String.Equals(current.Sha256, state.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                if (current.Attributes != state.Attributes) File.SetAttributes(path, (FileAttributes)state.Attributes);
                return;
            }
            if (state.Exists) PutFile(path, state.Data, state.Attributes);
            else
            {
                ValidateRegularFileIfPresent(path);
                FileAttributes attributes;
                if (TryAttributes(path, out attributes))
                {
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        ValidateSingleLink(stream, path);
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(path);
                }
            }
        }

        private static void RestoreFiles(string folder, FileState icon, FileState ini, int folderAttributes)
        {
            ValidateFolder(folder);
            List<Exception> failures = new List<Exception>();
            try { RestoreFile(Path.Combine(folder, IconName), icon); } catch (Exception error) { failures.Add(error); }
            try { RestoreFile(Path.Combine(folder, IniName), ini); } catch (Exception error) { failures.Add(error); }
            try { File.SetAttributes(folder, (FileAttributes)folderAttributes); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("One or more folder files could not be restored.", failures);
        }

        private static void RollBackOrThrow(string folder, FileState icon, FileState ini, int folderAttributes,
            string operationDirectory, Journal journal, string status, Exception originalFailure)
        {
            try
            {
                RestoreFiles(folder, icon, ini, folderAttributes);
                AssertCurrent(folder, icon, ini, folderAttributes);
                journal.Status = status;
                WriteJournal(operationDirectory, journal);
                Notify(folder);
            }
            catch (Exception rollbackFailure)
            {
                throw new AggregateException("Operation failed and rollback could not be fully verified. Keep the backups: " +
                    operationDirectory, originalFailure, rollbackFailure);
            }
        }

        private static void SaveBackup(string directory, string name, FileState state)
        {
            if (!state.Exists) return;
            using (FileStream stream = new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(state.Data, 0, state.Data.Length);
                stream.Flush(true);
            }
        }

        private static void LoadBackup(string directory, string name, FileState state)
        {
            if (!state.Exists) return;
            FileState backup = Capture(Path.Combine(directory, name));
            if (!backup.Exists || !String.Equals(backup.Sha256, state.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup is missing or damaged: " + Path.Combine(directory, name));
            state.Data = backup.Data;
        }

        private static void EnsureDirectory(string directory)
        {
            ValidateNoReparseAncestors(directory);
            Directory.CreateDirectory(directory);
            ValidateNoReparseAncestors(directory);
        }

        private static void WriteJournal(string directory, Journal journal)
        {
            ValidateNoReparseAncestors(directory);
            string path = Path.Combine(directory, "journal.json");
            ValidateRegularFileIfPresent(path);
            string temporary = Path.Combine(directory, ".j-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            byte[] bytes = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(journal));
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void ValidateJournal(Journal journal, string folder)
        {
            if (journal == null || journal.Version != 1 || !String.Equals(journal.Folder, folder, StringComparison.OrdinalIgnoreCase) ||
                journal.BeforeIcon == null || journal.BeforeIni == null || journal.AfterIcon == null || journal.AfterIni == null ||
                !journal.AfterIcon.Exists || !journal.AfterIni.Exists || String.IsNullOrEmpty(journal.AfterIcon.Sha256) ||
                String.IsNullOrEmpty(journal.AfterIni.Sha256))
                throw new InvalidDataException("The icon journal is invalid or belongs to another folder.");
        }

        private static string FolderHash(string folder)
        {
            return Hash(Encoding.UTF8.GetBytes(folder.ToUpperInvariant())).Substring(0, 32);
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static OperationResult Result(string operation, string folder, string backupDirectory, string message)
        {
            return new OperationResult { Success = true, Operation = operation, Folder = folder,
                IconPath = Path.Combine(folder, IconName), BackupDirectory = backupDirectory, Message = message };
        }

        private static void Notify(string folder)
        {
            SHChangeNotify(0x2000, 0x2005, folder, IntPtr.Zero);
            SHChangeNotify(0x1000, 0x2005, folder, IntPtr.Zero);
        }

        public sealed class FileState
        {
            public bool Exists { get; set; }
            public int Attributes { get; set; }
            public string Sha256 { get; set; }
            [ScriptIgnore]
            public byte[] Data { get; set; }
        }

        public sealed class Journal
        {
            public int Version { get; set; }
            public string Folder { get; set; }
            public string Status { get; set; }
            public int BeforeFolderAttributes { get; set; }
            public int AfterFolderAttributes { get; set; }
            public FileState BeforeIcon { get; set; }
            public FileState BeforeIni { get; set; }
            public FileState AfterIcon { get; set; }
            public FileState AfterIni { get; set; }
        }

        private sealed class FolderLock : IDisposable
        {
            private readonly Mutex mutex;
            public FolderLock(string folder)
            {
                mutex = new Mutex(false, "Local\\FolderIconTool-" + FolderHash(folder));
                bool acquired;
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired)
                {
                    mutex.Dispose();
                    throw new IOException("Another folder icon operation is using this folder.");
                }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileInformation
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHChangeNotify(uint eventId, uint flags, string item, IntPtr otherItem);

        private static string DecodeIni(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            string value;
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 254)
                value = new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2);
            else if (bytes.Length >= 2 && bytes[0] == 254 && bytes[1] == 255)
                value = new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2);
            else
            {
                int skip = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191 ? 3 : 0;
                try { value = new UTF8Encoding(false, true).GetString(bytes, skip, bytes.Length - skip); }
                catch (DecoderFallbackException)
                {
                    value = Encoding.GetEncoding(Encoding.Default.CodePage,
                        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
                }
            }
            if (value.IndexOf('\0') >= 0) throw new InvalidDataException("desktop.ini has an unsupported encoding or contains NUL characters.");
            return value;
        }
    }
}
