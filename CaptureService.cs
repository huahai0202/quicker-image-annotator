using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

internal static class CaptureService
{
    private static readonly string[] SupportedImageExtensions = new string[]
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".bmp",
        ".gif",
        ".tif",
        ".tiff"
    };

    public static string GetInputImagePath(string[] args)
    {
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            string path = Environment.ExpandEnvironmentVariables(args[0]);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Image file does not exist.", path);
            }

            string fullPath = Path.GetFullPath(path);
            AppLog.Info("Using image path from arguments: " + fullPath);
            return fullPath;
        }

        string clipboardImagePath = TrySaveClipboardImage();
        if (!string.IsNullOrEmpty(clipboardImagePath))
        {
            AppLog.Info("Using image captured from clipboard: " + clipboardImagePath);
            return clipboardImagePath;
        }

        string droppedFilePath = TryGetClipboardImageFile();
        if (!string.IsNullOrEmpty(droppedFilePath))
        {
            AppLog.Info("Using image file from clipboard file list: " + droppedFilePath);
            return droppedFilePath;
        }

        throw new InvalidOperationException("No image found. Copy an image first, or pass an image path as the first argument.");
    }

    public static Bitmap LoadBitmap(string path)
    {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (Image image = Image.FromStream(stream))
        {
            return new Bitmap(image);
        }
    }

    private static string TrySaveClipboardImage()
    {
        if (!Clipboard.ContainsImage())
        {
            return null;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "quicker-annotate-clipboard.png");
        using (Image image = Clipboard.GetImage())
        {
            image.Save(tempPath, ImageFormat.Png);
        }

        return tempPath;
    }

    private static string TryGetClipboardImageFile()
    {
        if (!Clipboard.ContainsFileDropList())
        {
            return null;
        }

        foreach (string file in Clipboard.GetFileDropList())
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (IsSupportedImageExtension(ext))
            {
                return file;
            }
        }

        return null;
    }

    private static bool IsSupportedImageExtension(string extension)
    {
        for (int i = 0; i < SupportedImageExtensions.Length; i++)
        {
            if (string.Equals(extension, SupportedImageExtensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
