using System;
using System.Drawing;

namespace FolderIconTool
{
    // Pixel coordinates refer to the image after EXIF orientation is applied.
    public sealed class OperationResult
    {
        public bool Success { get; set; }
        public string Operation { get; set; }
        public string Folder { get; set; }
        public string IconPath { get; set; }
        public string BackupDirectory { get; set; }
        public string Message { get; set; }
    }

    public static class ToolPaths
    {
        public static string BackupRoot
        {
            get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "backups"); }
        }
    }
}
