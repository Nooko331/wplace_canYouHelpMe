using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using WplaceColorWatch;

internal static class Program
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static readonly string OutputDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts");
    private static readonly (string Name, Action Run)[] Tests =
    {
        ("EmbeddedNotesContainReadableChinese", EmbeddedNotesContainReadableChinese),
        ("FirstLaunchShowsAndRestartRemembers", FirstLaunchShowsAndRestartRemembers),
        ("EachVersionIsRememberedSeparately", EachVersionIsRememberedSeparately),
        ("MissingOrCorruptHistoryDoesNotBlockStartup", MissingOrCorruptHistoryDoesNotBlockStartup),
        ("UnwritableHistoryDoesNotInterruptUse", UnwritableHistoryDoesNotInterruptUse),
        ("DialogHandlesLongContentAndResizing", DialogHandlesLongContentAndResizing),
        ("StartupDismissAndManualReopen", StartupDismissAndManualReopen),
        ("VersionLinkFitsBothMainLayouts", VersionLinkFitsBothMainLayouts)
    };

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            foreach (var test in Tests) Console.WriteLine(test.Name);
            return 0;
        }
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Control.CheckForIllegalCrossThreadCalls = true;
        Directory.CreateDirectory(OutputDirectory);
        int failures = 0;
        foreach (var test in Tests)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {ex}");
            }
        }
        Console.WriteLine($"{Tests.Length - failures}/{Tests.Length} release notes tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static string NewHistoryPath() => Path.Combine(OutputDirectory, Guid.NewGuid().ToString("N"), "history.json");

    private static void EmbeddedNotesContainReadableChinese()
    {
        string notes = ReleaseNotes.Load();
        Require(notes.Contains("本次更新") && notes.Contains("更新说明"), "Embedded content is missing or incorrectly encoded.");
        Require(!notes.Contains('\uFFFD'), "Content contains replacement characters.");
    }

    private static void FirstLaunchShowsAndRestartRemembers()
    {
        string path = NewHistoryPath();
        var history = new ReleaseNotesHistory(path);
        Require(history.ShouldShow("1.6.2"), "First launch must show notes.");
        Require(!File.Exists(path), "Checking must not mark notes as viewed before showing.");
        history.MarkViewed("1.6.2");
        Require(!new ReleaseNotesHistory(path).ShouldShow("1.6.2"), "Viewed state was lost after restart.");
    }

    private static void EachVersionIsRememberedSeparately()
    {
        string path = NewHistoryPath();
        var history = new ReleaseNotesHistory(path);
        history.MarkViewed("1.6.1");
        Require(history.ShouldShow("1.6.2"), "Upgrade must show its own notes.");
        history.MarkViewed("1.6.2");
        var restarted = new ReleaseNotesHistory(path);
        Require(!restarted.ShouldShow("1.6.1") && !restarted.ShouldShow("1.6.2"), "Revisiting an older version must not forget newer history.");
        Require(restarted.ShouldShow("2.0.0"), "A later version must show notes.");
    }

    private static void MissingOrCorruptHistoryDoesNotBlockStartup()
    {
        string path = NewHistoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        foreach (string content in new[] { "", "{invalid", "null", "{}", "[42]" })
        {
            File.WriteAllText(path, content);
            var history = new ReleaseNotesHistory(path);
            Require(history.ShouldShow("1.6.2"), "Invalid state should recover as a first launch.");
            history.MarkViewed("1.6.2");
            Require(!new ReleaseNotesHistory(path).ShouldShow("1.6.2"), "Recovered state was not persisted.");
        }
    }

    private static void UnwritableHistoryDoesNotInterruptUse()
    {
        string path = NewHistoryPath();
        Directory.CreateDirectory(path); // A directory cannot be overwritten by the history file.
        var history = new ReleaseNotesHistory(path);
        history.MarkViewed("1.6.2");
        Require(!history.ShouldShow("1.6.2"), "A save failure should still be remembered in this process.");
        Require(history.ShouldShow("1.6.3"), "Save failure should not suppress another version.");
    }

    private static void DialogHandlesLongContentAndResizing()
    {
        using var dialog = new ReleaseNotesDialog("1.6.2", string.Join("\n\n", Enumerable.Repeat(ReleaseNotes.Load(), 8)));
        dialog.Show();
        Pump();
        var content = (RichTextBox)dialog.Controls.Find("releaseNotesContent", true).Single();
        var close = (Button)dialog.Controls.Find("closeReleaseNotes", true).Single();
        Require(content.ReadOnly && content.WordWrap && content.ScrollBars == RichTextBoxScrollBars.Vertical,
            "Long notes must wrap and scroll while remaining read-only.");
        foreach (Size size in new[] { new Size(440, 360), new Size(780, 640) })
        {
            dialog.ClientSize = size;
            Pump();
            Require(content.Height > 40, "Resizing collapsed the content.");
            Require(dialog.ClientRectangle.Contains(dialog.RectangleToClient(close.RectangleToScreen(close.ClientRectangle))),
                "Close button is clipped.");
        }
        content.SelectionStart = content.TextLength;
        content.ScrollToCaret();
        Require(content.GetCharIndexFromPosition(Point.Empty) > 0, "The last notes cannot be reached by scrolling.");
        ((IButtonControl)dialog.CancelButton!).PerformClick();
    }

    private static void StartupDismissAndManualReopen()
    {
        string path = NewHistoryPath();
        var history = new ReleaseNotesHistory(path);
        using var main = new MainFixture(history);
        int shown = 0;
        Exception? dialogFailure = null;
        using var closer = new System.Windows.Forms.Timer { Interval = 120 };
        closer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<ReleaseNotesDialog>().FirstOrDefault();
            if (dialog == null) return;
            shown++;
            try
            {
                string version = Get<string>(main.Form, "_currentVersionText");
                Require(!IsWindowEnabled(main.Form.Handle), "Main controls must be disabled while reading.");
                Require(dialog.Text.Contains(version), "Dialog must use the executable version.");
                Require(shown > 1 || history.ShouldShow(version), "History was written before dismissal.");
                Call(main.Form, "EnsureVisibleAndActivated");
                if (shown == 1) SaveImage(dialog, "release-notes.png");
                Require(Form.ActiveForm == dialog,
                    $"Startup focus: active={Form.ActiveForm?.Text ?? "null"}; visible={dialog.Visible}; foreground={GetForegroundWindow()}; dialog={dialog.Handle}.");
                // A deliberately null key payload proves the hook returns before reading it
                // while the dialog is open; no keyboard or mouse events are sent.
                Call(main.Form, "HookCallback", 0, (IntPtr)0x0100, IntPtr.Zero);
            }
            catch (Exception ex) { dialogFailure = ex; }
            finally
            {
                if (shown == 1) dialog.Close(); // Also cover the window's X button semantics.
                else ((IButtonControl)dialog.CancelButton!).PerformClick();
            }
        };
        closer.Start();
        main.Form.Show();
        Pump();
        Require(dialogFailure == null, $"Dialog integration failed: {dialogFailure}");
        Require(shown == 1 && File.Exists(path), "Startup did not show and persist exactly one dialog.");
        string current = Get<string>(main.Form, "_currentVersionText");
        Require(!new ReleaseNotesHistory(path).ShouldShow(current), "Closing with X did not persist history.");
        Call(main.Form, "ShowCurrentReleaseNotes", true);
        Require(shown == 1, "Automatic check repeated an already viewed version.");
        var link = Get<LinkLabel>(main.Form, "labelCurrentVersion");
        typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
        Require(shown == 2 && dialogFailure == null, "Manual link did not reopen viewed notes.");
        Require(main.Form.Enabled, "Main controls did not recover after dismissal.");
    }

    private static void VersionLinkFitsBothMainLayouts()
    {
        var history = new ReleaseNotesHistory(NewHistoryPath());
        history.MarkViewed(typeof(Form1).Assembly.GetName().Version!.ToString(3));
        using var main = new MainFixture(history);
        main.Form.Show();
        Pump();
        foreach (string layout in new[] { "full", "compact" })
        {
            Call(main.Form, "ApplyLayout", Get<object>(main.Form, "_layoutMode"));
            main.Form.PerformLayout();
            var link = Get<LinkLabel>(main.Form, "labelCurrentVersion");
            Require(main.Form.ClientRectangle.Contains(link.Bounds), "Version link is clipped.");
            Require(link.Text.Substring(link.LinkArea.Start, link.LinkArea.Length) == "更新说明", "Link does not target the notes text.");
            if (layout == "compact")
            {
                var range = Get<CheckBox>(main.Form, "checkShowRange");
                Require(link.Right < range.Left, "Compact version link overlaps range controls.");
            }
            SaveImage(main.Form, $"main-{layout}.png");
            Call(main.Form, "ToggleLayoutMode");
        }
    }

    private sealed class MainFixture : IDisposable
    {
        public Form1 Form { get; }
        public MainFixture(ReleaseNotesHistory history)
        {
            Form = new Form1(new Options(), 0x8002, history);
            Call(Form, "Unhook");
            Get<System.Windows.Forms.Timer>(Form, "updateTimer").Stop();
        }
        public void Dispose()
        {
            Get<System.Windows.Forms.Timer?>(Form, "_startupActivationTimer")?.Dispose();
            // Dispose without OnFormClosing so tests never overwrite actual user settings.
            Form.Dispose();
        }
    }

    private static object? Call(object target, string name, params object?[] args) =>
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static T Get<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Pump()
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 300)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
    private static void SaveImage(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(Path.Combine(OutputDirectory, name));
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
