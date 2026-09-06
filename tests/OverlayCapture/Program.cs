using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using WplaceColorWatch;

internal static class Program
{
    private static readonly (string Name, Action<Fixture> Run)[] Tests =
    {
        ("WorkerSamplesStayCleanAcrossTimerAndProgressUpdates", WorkerSamplesStayClean),
        ("UiThreadSamplingReadsBeneathRedMarkers", UiThreadSamplingReadsBeneathMarkers),
        ("RealRedCanvasRemainsRed", RealRedCanvasRemainsRed),
        ("AllOverlayShowPathsRespectCapture", AllShowPathsRespectCapture),
        ("NestedCapturePreservesFillProgress", NestedCapturePreservesProgress),
        ("ExceptionsReleaseSuppression", ExceptionsReleaseSuppression),
        ("UncheckedPreviewStaysHidden", UncheckedPreviewStaysHidden),
        ("RulePickingStaysHiddenBetweenSamples", RulePickingStaysHidden),
        ("AllThreeRegionScannersIgnoreMarkers", RegionScannersIgnoreMarkers),
        ("WorkerCanReleaseCaptureAfterWindowDisposal", ReleaseAfterWindowDisposal)
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
        var selected = args.Length == 0 ? Tests : Tests.Where(t => args.Contains(t.Name)).ToArray();
        if (selected.Length == 0) return 2;
        int failures = 0;
        foreach (var test in selected)
        {
            try
            {
                using var fixture = new Fixture();
                test.Run(fixture);
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {ex}");
            }
        }
        Console.WriteLine($"{selected.Length - failures}/{selected.Length} overlay capture tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void WorkerSamplesStayClean(Fixture f)
    {
        f.SetFill();
        f.AssertMarkerVisible();
        int ticksDuringCapture = 0;
        using var probe = new System.Windows.Forms.Timer { Interval = 15 };
        probe.Tick += (_, _) =>
        {
            if (Get<int>(f.Main, "_overlayCaptureDepth") == 0) return;
            ticksDuringCapture++;
            // This path used to re-show the overlay independently of the UI timer.
            Call(f.Main, "SetOverlayFillStartIndex", 0);
            Require(!f.Overlay.Visible, "A progress update re-showed markers during sampling.");
        };
        probe.Start();
        var sample = RunWorker(() =>
        {
            using var action = f.BeginCapture();
            var result = f.Sample();
            // Also cover a slow action after sampling, regardless of whether the
            // test host allows moving the cursor off the static color patch.
            Thread.Sleep(260);
            using var dc = new ScreenDc();
            Require(dc.GetPixel(f.SamplePoint.X, f.SamplePoint.Y) == result.clear,
                "Timer updates contaminated the clear sample while the action remained active.");
            return result;
        });
        probe.Stop();
        f.AssertSamples(sample);
        Require(ticksDuringCapture >= 2, "The regression did not exercise timer interleaving.");
        Require(f.Overlay.Visible, "Fill markers were not restored after sampling.");
        var state = Get<RuntimeState>(f.Main, "_state").Snapshot();
        Require(state.recordedBgrsRaw[1] == f.Expected, "The second UI swatch contains a marker color.");
    }

    private static void UiThreadSamplingReadsBeneathMarkers(Fixture f)
    {
        f.AssertMarkerVisible();
        f.AssertSamples(f.Sample());
        Require(f.Overlay.Visible, "The A-key path did not restore the range preview.");
    }

    private static void RealRedCanvasRemainsRed(Fixture f)
    {
        f.SetColor(Color.FromArgb(237, 28, 36));
        f.SetFill();
        f.AssertMarkerVisible();
        f.AssertSamples(RunWorker(() => f.Sample()));
    }

    private static void AllShowPathsRespectCapture(Fixture f)
    {
        using (var capture = f.BeginCapture())
        {
            Action[] refreshes =
            {
                () => Call(f.Main, "RefreshRangePreview"),
                () => f.SetFill(),
                () => Call(f.Main, "SetOverlayFillStartIndex", 1),
                () => Call(f.Main, "RestorePreviewOverlayIfChecked"),
                () => Call(f.Main, "UpdateTimerOnTick", null, EventArgs.Empty),
                () => Call(f.Main, "EnsureOverlay")
            };
            foreach (var refresh in refreshes)
            {
                refresh();
                Pump(25);
                Require(!f.Overlay.Visible, "An overlay refresh bypassed capture suppression.");
                f.AssertUnderlyingPixel();
            }
        }
        Require(f.Overlay.Visible, "Preview was not restored.");
    }

    private static void NestedCapturePreservesProgress(Fixture f)
    {
        f.SetFill();
        using (var outer = f.BeginCapture())
        {
            using (var inner = f.BeginCapture())
            {
                Call(f.Main, "SetOverlayFillStartIndex", 1);
            }
            Pump(80);
            Require(!f.Overlay.Visible, "An inner scope restored markers before the action finished.");
            f.AssertSamples(f.Sample());
            Require(!f.Overlay.Visible, "Sampling restored markers before the surrounding action finished.");
        }
        Require(f.Overlay.Visible, "Fill markers were not restored.");
        Require(Get<bool>(f.Main, "_overlayFillMode"), "Capture reset the fill mode.");
        Require(Get<int>(f.Main, "_overlayFillStartIndex") == 1, "Capture reset the fill progress.");
        Require(Get<int>(f.Overlay, "_startIndex") == 1, "The overlay lost its progress index.");
    }

    private static void ExceptionsReleaseSuppression(Fixture f)
    {
        try
        {
            using var capture = f.BeginCapture();
            throw new IOException("simulated capture failure");
        }
        catch (IOException) { }
        Require(Get<int>(f.Main, "_overlayCaptureDepth") == 0, "A failed capture leaked suppression.");
        Require(f.Overlay.Visible, "A failed capture left markers permanently hidden.");
    }

    private static void UncheckedPreviewStaysHidden(Fixture f)
    {
        using (var capture = f.BeginCapture()) f.ShowRange.Checked = false;
        Pump(150);
        Require(!f.Overlay.Visible, "Capture restored a preview the user had disabled.");
    }

    private static void RulePickingStaysHidden(Fixture f)
    {
        var pickType = typeof(Form1).GetNestedType("ColorPickTarget", BindingFlags.NonPublic)!;
        Call(f.Main, "EnterColorPicking", Enum.Parse(pickType, "Wanted"));
        Pump(150);
        Require(!f.Overlay.Visible, "The timer restored markers while waiting for a rule pick.");
        f.AssertSamples(f.Sample());
        Pump(150);
        Require(!f.Overlay.Visible, "Markers returned between rule picks.");
        Call(f.Main, "ExitColorPicking", false);
        Require(f.Overlay.Visible, "Exiting rule picking did not restore the preview.");
    }

    private static void RegionScannersIgnoreMarkers(Fixture f)
    {
        f.SetColor(Color.White);
        f.AssertMarkerVisible();
        // Preview includes the outline; bitmap scans exclude Right and Bottom.
        int expectedCount = ScanPattern.GetGridPoints(f.Range, 10).Count(f.Range.Contains);
        var all = RunWorker(() => (Dictionary<BgrColor, List<Point>>)Call(f.Main,
            "ScanMatchingPointsForAllColors", f.Range, new List<BgrColor> { f.Expected }, CancellationToken.None, null)!);
        Require(all.Count == 1 && all[f.Expected].Count == expectedCount,
            $"Full-auto matched {all.Values.Sum(points => points.Count)}/{expectedCount} points in {all.Count} groups.");
        var labeled = RunWorker(() => (Dictionary<long, BgrColor?>)Call(f.Main,
            "ScanLabeledGrid", f.Range, CancellationToken.None, null)!);
        Require(labeled.Count == expectedCount && labeled.Values.All(c => c == f.Expected), "Island screenshot contains overlay pixels.");
        var points = RunWorker(() => (List<Point>)Call(f.Main,
            "ScanMatchingPoints", f.Range, new List<BgrColor> { f.Expected }, CancellationToken.None, null)!);
        Require(points.Count == expectedCount, "Recorded-color screenshot contains overlay pixels.");
    }

    private static void ReleaseAfterWindowDisposal(Fixture f)
    {
        var capture = RunWorker(() => f.BeginCapture());
        f.Main.Dispose();
        RunWorker(() => { capture.Dispose(); capture.Dispose(); return true; });
    }

    private static T RunWorker<T>(Func<T> work)
    {
        var task = Task.Run(work);
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.ElapsedMilliseconds < 10000) Pump(5);
        Require(task.IsCompleted, "Worker timed out (possible UI-thread deadlock).");
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        do { Application.DoEvents(); Thread.Sleep(1); } while (watch.ElapsedMilliseconds < milliseconds);
    }

    private static object? Call(object instance, string method, params object?[] args)
    {
        try
        {
            return instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static T Get<T>(object instance, string field) =>
        (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class TestCanvas : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Point _originalCursor = Cursor.Position;
        private readonly TestCanvas _canvas;
        public Form1 Main { get; }
        public Rectangle Range { get; }
        public Point SamplePoint => new(Range.Left + 20, Range.Top + 20);
        public BgrColor Expected => BgrColor.FromColor(_canvas.BackColor);
        public CheckBox ShowRange => Get<CheckBox>(Main, "checkShowRange");
        public PreviewOverlayForm Overlay => Get<PreviewOverlayForm>(Main, "_previewOverlay");

        public Fixture()
        {
            var area = Screen.PrimaryScreen!.WorkingArea;
            _canvas = new TestCanvas
            {
                Text = "Overlay capture regression canvas",
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                Bounds = new Rectangle(area.Left + 80, area.Top + 80, 180, 120),
                BackColor = Color.FromArgb(172, 122, 132),
                TopMost = true,
                ShowInTaskbar = false
            };
            _canvas.Show();
            Pump(80);
            var options = new Options { IntervalMs = 20, ScanStep = 10 };
            Main = new Form1(options, 0x8002);
            // Exercise the real form/timer/capture code, without registering user
            // hotkeys, showing its main UI, checking updates, or sending I/click.
            Call(Main, "Unhook");
            _ = Main.Handle;
            options.ScanStep = 10;
            _canvas.BringToFront();
            _canvas.Refresh();
            // Ensure the native window is actually visible even under a hidden
            // console launcher: SHOWWINDOW | NOACTIVATE | NOMOVE | NOSIZE.
            typeof(Form1).Assembly.GetType("WplaceColorWatch.NativeMethods")!.GetMethod("SetWindowPos")!.Invoke(null,
                new object[] { _canvas.Handle, new IntPtr(-1), 0, 0, 0, 0, (uint)0x53 });
            Pump(150);
            Range = new Rectangle(_canvas.PointToScreen(new Point(20, 20)), new Size(60, 40));
            try
            {
                AssertUnderlyingPixel();
                Get<RuntimeState>(Main, "_state").SetRange(Range);
                ShowRange.Checked = true;
                Call(Main, "RefreshRangePreview");
                Pump(80);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public IDisposable BeginCapture() => (IDisposable)Call(Main, "BeginScreenCapture")!;

        public (BgrColor hover, BgrColor clear, bool stable, int waitMs, int reads, bool changedFromHover) Sample() =>
            ((BgrColor, BgrColor, bool, int, int, bool))Call(Main, "CaptureColorSamples", SamplePoint)!;

        public void SetFill() => Call(Main, "SetOverlayFillPoints",
            new List<Point> { SamplePoint, new(SamplePoint.X + 10, SamplePoint.Y) }, true);

        public void SetColor(Color color)
        {
            _canvas.BackColor = color;
            _canvas.Refresh();
            Pump(80);
        }

        public void AssertMarkerVisible()
        {
            Pump(80);
            Require(Overlay.Visible, "The regression requires a visible red marker.");
            using var dc = new ScreenDc();
            var pixel = dc.GetPixel(SamplePoint.X, SamplePoint.Y);
            Require(pixel.R > 100 && pixel.G < 10 && pixel.B < 10,
                $"Marker was not actually visible in the screen capture: {string.Join(',', pixel.ToRgbArray())}.");
        }

        public void AssertUnderlyingPixel()
        {
            using var dc = new ScreenDc();
            var pixel = dc.GetPixel(SamplePoint.X, SamplePoint.Y);
            var native = typeof(Form1).Assembly.GetType("WplaceColorWatch.NativeMethods")!;
            var windowAtPoint = native.GetMethod("WindowFromPoint", new[] { typeof(Point) })!.Invoke(null, new object[] { SamplePoint });
            var processArgs = new object[] { windowAtPoint!, (uint)0 };
            native.GetMethod("GetWindowThreadProcessId")!.Invoke(null, processArgs);
            string coveringProcess = (uint)processArgs[1] == 0 ? "unknown" : Process.GetProcessById((int)(uint)processArgs[1]).ProcessName;
            Require(pixel == Expected,
                $"Capture at {SamplePoint} read RGB {string.Join(',', pixel.ToRgbArray())}, expected {string.Join(',', Expected.ToRgbArray())}; canvas bounds={_canvas.Bounds} visible={_canvas.Visible} hwnd={_canvas.Handle} dpi={_canvas.DeviceDpi} atPoint={windowAtPoint} process={coveringProcess} mainVisible={Main.Visible}.");
        }

        public void AssertSamples((BgrColor hover, BgrColor clear, bool stable, int waitMs, int reads, bool changedFromHover) sample)
        {
            Require(sample.hover == Expected, $"First sample is {string.Join(',', sample.hover.ToRgbArray())}.");
            Require(sample.clear == Expected, $"Second sample is {string.Join(',', sample.clear.ToRgbArray())}.");
        }

        public void Dispose()
        {
            Get<System.Windows.Forms.Timer>(Main, "updateTimer").Stop();
            Overlay?.Dispose();
            Main.Dispose();
            _canvas.Dispose();
            Cursor.Position = _originalCursor;
            Pump(20);
        }
    }
}
