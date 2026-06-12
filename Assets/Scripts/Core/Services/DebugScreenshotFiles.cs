using UnityEngine;

public static class DebugScreenshotFiles
{
    const string DebugScreenshotFolder = "local-only/debug-screenshots";

    public static Texture2D Downsample(Texture2D source, int maxWidth)
    {
        int targetWidth = Mathf.Clamp(maxWidth, 160, 1920);
        if (source.width <= targetWidth)
            return source;

        int targetHeight = Mathf.Max(1, Mathf.RoundToInt(source.height * (targetWidth / (float)source.width)));
        RenderTexture previous = RenderTexture.active;
        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);

        try
        {
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            Texture2D scaled = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            scaled.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            scaled.Apply(false, false);
            return scaled;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    public static string GetDirectory()
    {
        string folder = string.IsNullOrWhiteSpace(DebugScreenshotFolder)
            ? "local-only/debug-screenshots"
            : DebugScreenshotFolder;

        return System.IO.Path.IsPathRooted(folder)
            ? folder
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", folder));
    }

    public static void Prune(string directory, int keepFiles)
    {
        if (keepFiles <= 0 || string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
            return;

        System.Collections.Generic.List<System.IO.FileInfo> captures = new System.Collections.Generic.List<System.IO.FileInfo>();
        System.IO.DirectoryInfo dir = new System.IO.DirectoryInfo(directory);
        System.IO.FileInfo[] files = dir.GetFiles("F10-*.*", System.IO.SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string extension = files[i].Extension;
            if (string.Equals(extension, ".png", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".txt", System.StringComparison.OrdinalIgnoreCase))
            {
                captures.Add(files[i]);
            }
        }

        if (captures.Count <= keepFiles)
            return;

        captures.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        for (int i = keepFiles; i < captures.Count; i++)
        {
            try
            {
                captures[i].Delete();
            }
            catch (System.Exception ex)
            {
                LoggerProvider.Log(LogLevel.Warning, "DebugCapture", $"Could not prune F10 debug capture '{captures[i].FullName}': {ex.Message}");
            }
        }
    }

    public static string SanitizeFilePart(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "capture";

        char[] chars = value.ToCharArray();
        char[] invalid = System.IO.Path.GetInvalidFileNameChars();
        for (int i = 0; i < chars.Length; i++)
        {
            if (System.Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i]))
                chars[i] = '_';
        }

        return new string(chars);
    }

    public static void RecordLastCaptureCamera()
    {
        if (ServiceLocator.TryGet<ICameraTeleportRegistry>(out var teleports))
            teleports.RecordLastDebugCapture();
    }
}
