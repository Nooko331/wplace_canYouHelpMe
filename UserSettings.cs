using System;
using System.IO;
using System.Text.Json;

namespace WplaceColorWatch
{
    public enum MaskType { BlueMarble = 1, Official = 2 }

    /// <summary>
    /// 用户 UI 偏好的持久化设置，独立于 <see cref="Options"/>（后者是 CLI 注入的算法参数）。
    /// 存为 exe 同级目录下的 user_settings.json。任何读写异常都被吞掉，绝不影响启动或关闭流程。
    /// </summary>
    public sealed class UserSettings
    {
        // 速度模式：0=Balanced, 1=Extreme（与 Form1.SpeedPreset 枚举的整数值对应）
        public const int SpeedBalanced = 0;
        public const int SpeedExtreme = 1;

        public int ScanWorkers { get; set; } = 1;
        public int ScanStep { get; set; } = 10;
        public bool ShowRange { get; set; }
        public int SpeedPreset { get; set; } = SpeedBalanced;
        public MaskType? Mask { get; set; }
        public bool SkipIslandRecommendation { get; set; }

        internal void Write(string? filePath)
        {
            var path = Path.GetFullPath(filePath ?? GetFilePath());
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
                File.Move(temporary, path, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }

        private const string FileName = "user_settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        // 单文件发布时 AppContext.BaseDirectory 指向临时解压目录（%TEMP%\.net\...），
        // 改用进程 exe 路径取目录，保证设置文件与用户可见的 exe 放在一起（与 Logger 一致）。
        private static string GetFilePath()
        {
            var dir = string.IsNullOrEmpty(Environment.ProcessPath)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(Environment.ProcessPath)!;
            return Path.Combine(dir, FileName);
        }

        /// <summary>
        /// 从磁盘加载设置；文件缺失/损坏/无权限时返回默认实例，绝不抛异常。
        /// </summary>
        public static UserSettings Load(string? filePath = null)
        {
            try
            {
                var path = filePath ?? GetFilePath();
                if (!File.Exists(path))
                {
                    return new UserSettings();
                }
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<UserSettings>(json);
                if (settings == null)
                {
                    return new UserSettings();
                }
                // 速度模式越界则回退平衡
                if (settings.SpeedPreset != SpeedExtreme)
                {
                    settings.SpeedPreset = SpeedBalanced;
                }
                if (settings.Mask is not (MaskType.BlueMarble or MaskType.Official)) settings.Mask = null;
                return settings;
            }
            catch (Exception ex)
            {
                Logger.Error($"[settings] load failed: {ex}");
                return new UserSettings();
            }
        }

        /// <summary>
        /// 写入磁盘；任何异常被吞掉并记入错误日志，绝不中断关闭流程。
        /// </summary>
        public void Save(string? filePath = null)
        {
            try
            {
                Write(filePath);
            }
            catch (Exception ex)
            {
                Logger.Error($"[settings] save failed: {ex}");
            }
        }
    }
}
