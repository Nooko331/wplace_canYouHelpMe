using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace WplaceColorWatch
{
    internal static class ReleaseNotes
    {
        public static string Load()
        {
            using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("WplaceColorWatch.ReleaseNotes.txt");
            if (stream == null)
            {
                throw new InvalidOperationException("缺少内置更新说明 ReleaseNotes.txt。");
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd().Trim();
        }
    }

    // 与 EXE 所在目录分离，升级、重命名或移动软件后仍保留各版本的查看记录。
    internal sealed class ReleaseNotesHistory
    {
        private readonly string _filePath;
        private readonly HashSet<string> _viewedVersions;

        public ReleaseNotesHistory(string? filePath = null)
        {
            _filePath = filePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "wplace_canYouHelpMe", "release_notes_state.json");
            _viewedVersions = Load();
        }

        public bool ShouldShow(string version) => !_viewedVersions.Contains(version);

        public void MarkViewed(string version)
        {
            if (!_viewedVersions.Add(version)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(_filePath, JsonSerializer.Serialize(_viewedVersions), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                // 写入失败也只在本次运行中显示一次，不影响使用和关闭。
                Logger.Error($"[release-notes] save history failed: {ex}");
            }
        }

        private HashSet<string> Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(_filePath))
                        ?? new HashSet<string>(StringComparer.Ordinal);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[release-notes] load history failed: {ex}");
            }
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
