using Avalonia;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Wplace.Desktop;

internal static class Program
{
    internal static string[] Arguments { get; private set; } = [];
    internal static bool Preview => Arguments.Contains("--preview-ui") || Arguments.Contains("--capture") || Arguments.Contains("--smoke-test") || Arguments.Contains("--preview-release-notes");
    internal static string Version => typeof(Program).Assembly.GetName().Version!.ToString(3);
    internal static WplaceColorWatch.Options Options { get; private set; } = new();
    internal static Action? ActivateRequested;
    private const string PipeName = "wplace-avalonia-activate";
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        Options = WplaceColorWatch.Program.ParseArgs(args);
        bool first = false;
        using var mutex = Preview ? null : new Mutex(true, "Local\\WplaceColorWatch.SingleInstance", out first);
        if (!first && !Preview)
        {
            try { using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out); pipe.Connect(700); pipe.WriteByte(1); }
            catch { NotifyLegacyInstance(); }
            return 0;
        }
        using var cancellation = new CancellationTokenSource();
        WplaceColorWatch.Logger.Init(Options.Debug);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => WplaceColorWatch.Logger.Error("[desktop-fatal] " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => WplaceColorWatch.Logger.Error("[desktop-task] " + e.Exception);
        if (first && !Preview) _ = ListenForActivation(cancellation.Token);
        try
        {
            var result = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return DesktopReview.Failed ? 1 : result;
        }
        finally { cancellation.Cancel(); WplaceColorWatch.Logger.Shutdown(); if (first) mutex!.ReleaseMutex(); }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
    private static async Task ListenForActivation(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                ActivateRequested?.Invoke();
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { await Task.Delay(100, token); }
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    private static void NotifyLegacyInstance() => PostMessage(new IntPtr(0xffff), RegisterWindowMessage("WplaceColorWatch.ShowMainWindow"), IntPtr.Zero, IntPtr.Zero);
}
