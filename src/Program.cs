using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace FolderIconTool
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(ToolPaths.BackupRoot));
                return 0;
            }

            FileStream reportStream = null;
            OperationResult completed = null;
            try
            {
                if (args[0] == "--help" || args[0] == "help")
                {
                    Console.WriteLine(Help);
                    return 0;
                }
                string operation = args[0].ToLowerInvariant();
                if (operation != "apply" && operation != "undo")
                    throw new ArgumentException("未知命令。可用命令：apply、undo、--help。");
                var options = ParseOptions(args, operation);
                string folder = Required(options, "--folder");
                string backups = options.ContainsKey("--backups")
                    ? options["--backups"] : ToolPaths.BackupRoot;
                string imagePath = operation == "apply" ? Required(options, "--image") : null;
                Rectangle? crop = operation == "apply" ? ParseCrop(options) : null;
                bool fitWholeImage = operation == "apply" && ParseFit(options, crop);
                string resultPath = options.ContainsKey("--result") ? options["--result"] : null;
                // Validate and reserve the report before changing a folder.
                if (resultPath != null)
                {
                    resultPath = Path.GetFullPath(resultPath);
                    if (!Directory.Exists(Path.GetDirectoryName(resultPath)))
                        throw new ArgumentException("结果文件的父目录不存在。");
                    if (File.Exists(resultPath) || Directory.Exists(resultPath))
                        throw new ArgumentException("结果文件已存在，请选择新文件名。");
                    if (IsInside(resultPath, folder))
                        throw new ArgumentException("结果文件请保存在目标文件夹之外。");
                    reportStream = new FileStream(resultPath, FileMode.CreateNew,
                        FileAccess.Write, FileShare.Read);
                }
                completed = operation == "apply"
                    ? IconEngine.Apply(folder, imagePath, crop, backups, fitWholeImage)
                    : IconEngine.RestoreLast(folder, backups);
                string json = new JavaScriptSerializer().Serialize(completed);
                WriteResult(json, reportStream);
                Console.WriteLine(json);
                return completed.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                string json = new JavaScriptSerializer().Serialize(new
                {
                    Success = completed != null && completed.Success,
                    Operation = completed == null ? null : completed.Operation,
                    BackupDirectory = completed == null ? null : completed.BackupDirectory,
                    Message = completed == null ? ex.Message :
                        "图标操作已完成，但结果输出失败：" + ex.Message
                });
                Console.Error.WriteLine(json);
                // Only write to the new report file this process has already reserved.
                try { WriteResult(json, reportStream); }
                catch { }
                return completed != null && completed.Success ? 2 : 1;
            }
            finally
            {
                if (reportStream != null) reportStream.Dispose();
            }
        }

        private static Dictionary<string, string> ParseOptions(string[] args, string operation)
        {
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                string key = args[i];
                bool allowed = key == "--folder" || key == "--backups" || key == "--result" ||
                    (operation == "apply" &&
                        (key == "--image" || key == "--crop" || key == "--fit"));
                if (!allowed || i + 1 >= args.Length || options.ContainsKey(key))
                    throw new ArgumentException("参数无效或重复：" + key);
                options.Add(key, args[i + 1]);
            }
            return options;
        }

        private static string Required(Dictionary<string, string> options, string key)
        {
            string value;
            if (!options.TryGetValue(key, out value) || String.IsNullOrWhiteSpace(value))
                throw new ArgumentException("缺少参数：" + key);
            return value;
        }

        private static Rectangle? ParseCrop(Dictionary<string, string> options)
        {
            string value;
            if (!options.TryGetValue("--crop", out value)) return null;
            string[] values = value.Split(',');
            int x, y, size;
            if (values.Length != 3 ||
                !Int32.TryParse(values[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !Int32.TryParse(values[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                !Int32.TryParse(values[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out size) ||
                x < 0 || y < 0 || size <= 0)
                throw new ArgumentException("--crop 格式为 x,y,边长，单位为旋转校正后的图片像素。");
            return new Rectangle(x, y, size, size);
        }

        private static bool ParseFit(Dictionary<string, string> options, Rectangle? crop)
        {
            string value;
            if (!options.TryGetValue("--fit", out value)) return !crop.HasValue;
            if (value != "contain" && value != "cover")
                throw new ArgumentException("--fit 可选 contain（完整图片）或 cover（填满裁剪）。");
            if (value == "contain" && crop.HasValue)
                throw new ArgumentException("完整图片模式不能同时指定 --crop。");
            return value == "contain";
        }

        private static bool IsInside(string path, string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteResult(string json, FileStream stream)
        {
            if (stream == null) return;
            byte[] bytes = new UTF8Encoding(false).GetBytes(json + Environment.NewLine);
            stream.Position = 0;
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        private const string Help =
            "全能自定义文件夹图标 1.0.0\r\n" +
            "双击打开中文界面；命令行用法：\r\n" +
            "  apply --folder <文件夹> --image <图片> [--fit contain|cover] [--crop x,y,边长]\r\n" +
            "  undo --folder <文件夹>\r\n" +
            "可选：--backups <备份目录> --result <新建JSON结果文件>\r\n" +
            "默认完整显示图片，自动等比适配。cover 居中裁剪；--crop 指定裁剪。\r\n" +
            "所有命令均不打开界面。\r\n" +
            "默认备份：程序所在目录\\data\\backups。撤销须使用原备份目录。\r\n";
    }
}
