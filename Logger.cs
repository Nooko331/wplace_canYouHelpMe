using System;
using System.IO;

namespace WplaceColorWatch
{

public static class Logger
{
    private const long MaxErrorLogBytes = 1024 * 1024;
    private const string ErrorLogFileName = "wplace_canYouHelpMe_error_log.txt";
    private static readonly object LockObj = new();
    private static StreamWriter? _writer;
    public static bool DebugEnabled { get; private set; }

    public static void Init(bool debug)
    {
        DebugEnabled = debug;
        if (!debug)
        {
            return;
        }

        var path = Path.Combine(BaseDir, "simple_color_watch.log");
        _writer = new StreamWriter(path, append: true)
        {
            AutoFlush = true
        };
    }

    public static void Debug(string message)
    {
        if (!DebugEnabled)
        {
            return;
        }

        Write("DEBUG", message);
    }

    public static void Error(string message)
    {
        var line = FormatLine("ERROR", message);
        lock (LockObj)
        {
            try
            {
                var path = GetErrorLogPath();
                File.AppendAllText(path, line + Environment.NewLine);
                TrimErrorLogIfNeeded(path);
            }
            catch
            {
                // Logging must never interrupt the application.
            }
        }

        TryWriteConsole(line);
    }

    public static void Shutdown()
    {
        lock (LockObj)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static void Write(string level, string message)
    {
        var line = FormatLine(level, message);
        lock (LockObj)
        {
            _writer?.WriteLine(line);
        }
        TryWriteConsole(line);
    }

    private static string FormatLine(string level, string message)
    {
        return $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
    }

    private static string GetErrorLogPath()
    {
        return Path.Combine(BaseDir, ErrorLogFileName);
    }

    // 单文件发布时 AppContext.BaseDirectory 指向临时解压目录（%TEMP%\.net\...），
    // 而非 exe 实际所在目录。这里改用进程 exe 路径取目录，保证日志与用户可见的 exe 放在一起；
    // 取不到时回退到 BaseDirectory，避免日志完全无法写入。
    private static string BaseDir =>
        string.IsNullOrEmpty(Environment.ProcessPath)
            ? AppContext.BaseDirectory
            : Path.GetDirectoryName(Environment.ProcessPath)!;

    private static void TrimErrorLogIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= MaxErrorLogBytes)
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var keepLength = (int)Math.Min(MaxErrorLogBytes, bytes.LongLength);
        var start = bytes.Length - keepLength;
        while (start < bytes.Length && bytes[start] != (byte)'\n')
        {
            start++;
        }
        if (start < bytes.Length)
        {
            start++;
        }
        else
        {
            start = bytes.Length - keepLength;
        }

        var trimmedLength = bytes.Length - start;
        var trimmed = new byte[trimmedLength];
        Buffer.BlockCopy(bytes, start, trimmed, 0, trimmedLength);
        File.WriteAllBytes(path, trimmed);
    }

    private static void TryWriteConsole(string line)
    {
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // No console attached.
        }
    }
}
}

