using System.IO;
using System.Threading.Tasks;

namespace ScummEditor.Core.Services
{
    public static class FileInfoService
    {
        public static async Task<string> GetBasicInfoAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return "File not found.";
            }

            var info = new FileInfo(path);
            var sizeKb = info.Length / 1024.0;

            // Small async touch to keep API async-friendly.
            await Task.Yield();

            return $"{info.Name} — {sizeKb:F1} KB";
        }
    }
}
