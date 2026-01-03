using System;
using System.IO;

namespace WplaceColorWatch
{

public static class Logger
{
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

        var path = Path.Combine(AppContext.BaseDirectory, "simple_color_watch.log");
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
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}";
        lock (LockObj)
        {
            _writer?.WriteLine(line);
        }
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

