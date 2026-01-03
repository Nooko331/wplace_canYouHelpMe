using System;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WplaceColorWatch
{

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Logger.Debug($"[fatal] unhandled exception: {e.ExceptionObject}");
            Logger.Shutdown();
        };
        Application.ThreadException += (_, e) =>
        {
            Logger.Debug($"[fatal] ui exception: {e.Exception}");
            Logger.Shutdown();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Debug($"[fatal] task exception: {e.Exception}");
            Logger.Shutdown();
        };

        try
        {
            NativeMethods.SetProcessDpiAwareness(2);
        }
        catch
        {
            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch
            {
                // Ignore if not supported.
            }
        }
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var options = ParseArgs(args);
        Logger.Init(options.Debug);
        Application.Run(new Form1(options));
        Logger.Shutdown();
    }    

    private static Options ParseArgs(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--debug":
                    options.Debug = true;
                    break;
                case "--interval-ms":
                    options.IntervalMs = ReadInt(args, ref i, options.IntervalMs);
                    break;
                case "--color-tol":
                    options.ColorTol = ReadInt(args, ref i, options.ColorTol);
                    break;
                case "--cooldown-ms":
                    options.CooldownMs = ReadInt(args, ref i, options.CooldownMs);
                    break;
                case "--action-delay-ms":
                    options.ActionDelayMs = ReadInt(args, ref i, options.ActionDelayMs);
                    break;
                case "--scan-step":
                    options.ScanStep = ReadInt(args, ref i, options.ScanStep);
                    break;
                case "--probe-x":
                    options.ProbeX = ReadNullableInt(args, ref i);
                    break;
                case "--probe-y":
                    options.ProbeY = ReadNullableInt(args, ref i);
                    break;
            }
        }
        return options;
    }

    private static int ReadInt(string[] args, ref int i, int fallback)
    {
        if (i + 1 >= args.Length)
        {
            return fallback;
        }
        if (int.TryParse(args[i + 1], out int value))
        {
            i++;
            return value;
        }
        return fallback;
    }

    private static int? ReadNullableInt(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            return null;
        }
        if (int.TryParse(args[i + 1], out int value))
        {
            i++;
            return value;
        }
        return null;
    }
}
}

