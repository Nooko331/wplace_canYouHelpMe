using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WplaceColorWatch
{
    public enum DesktopCommand
    {
        SelectRange, StartFill, DetectMissing, Stop, AutoWorkers,
        OpenColors, CloseColors, PickWanted, PickExcluded, StopPicking,
        SelectAllColors, ClearWanted, AddWanted, AddExcluded, RemoveWanted, RemoveExcluded
    }

    public sealed record EnginePrompt(string Title, string Message, bool Confirm, string? CheckBox);
    public sealed record EngineAnswer(bool Accepted, bool DontShowAgain = false);

    public sealed record DesktopSnapshot
    {
        public bool Ready { get; init; }
        public bool Busy { get; init; }
        public bool SelectingRange { get; init; }
        public bool ColorsOpen { get; init; }
        public string Picking { get; init; } = "";
        public string PickMessage { get; init; } = "";
        public string Status { get; init; } = "就绪";
        public int Workers { get; init; } = 1;
        public int Step { get; init; } = 10;
        public bool ShowRange { get; init; }
        public bool Extreme { get; init; }
        public bool Compact { get; init; }
        public string? HoverColor { get; init; }
        public string? ClearColor { get; init; }
        public string Range { get; init; } = "尚未划取范围";
        public bool HasRange { get; init; }
        public int Current { get; init; }
        public int Total { get; init; }
        public int Percent { get; init; }
        public string Remaining { get; init; } = "--:--";
        public IReadOnlyList<string> Wanted { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Excluded { get; init; } = Array.Empty<string>();
        public int EffectiveCount { get; init; } = 63;
    }

    /// <summary>
    /// Owns the existing Windows capture/fill engine on its original STA message loop.
    /// Avalonia receives immutable state and sends commands; no Windows Forms control
    /// is touched from the Avalonia dispatcher. The legacy main form is never shown.
    /// </summary>
    public sealed class WindowsEngineHost
    {
        private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Options _options;
        private readonly Thread _thread;
        private Form1? _engine;
        private volatile bool _shutdown;
        private int _promptDepth;
        private volatile DesktopSnapshot _snapshot = new();
        public bool ReadOnly { get; }
        public bool EnableHotkeys { get; }
        public string? SettingsPath { get; }
        private volatile bool _hotkeysSuspended;
        private volatile bool _editingInput;
        public bool HotkeysSuspended { get => _hotkeysSuspended; set => _hotkeysSuspended = value; }
        public bool EditingInput { get => _editingInput; set => _editingInput = value; }
        public bool PromptOpen => Volatile.Read(ref _promptDepth) > 0;
        public Task Ready => _ready.Task;
        public DesktopSnapshot Snapshot => _snapshot;
        public event Action<DesktopSnapshot>? SnapshotChanged;
        public event Action? ActivationRequested;
        public event Action<bool>? RangeSelectionChanged;
        public Func<EnginePrompt, Task<EngineAnswer>>? PromptHandler { get; set; }
        public static IReadOnlyList<string> Palette { get; } = GetPalette();

        public WindowsEngineHost(bool readOnly = false, bool enableHotkeys = true, string? settingsPath = null, Options? options = null)
        {
            ReadOnly = readOnly;
            EnableHotkeys = enableHotkeys && !readOnly;
            SettingsPath = settingsPath;
            _options = options ?? new Options();
            _thread = new Thread(Run) { IsBackground = true, Name = "Wplace Windows engine" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
        private void Run()
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4));
                Application.EnableVisualStyles();
                using var context = new ApplicationContext();
                using var engine = new Form1(_options, NativeMethods.RegisterWindowMessage("WplaceColorWatch.ShowMainWindow"), new ReleaseNotesHistory(Path.Combine(Path.GetTempPath(), "wplace-unused-engine-history.json")), this);
                _engine = engine;
                engine.ShowInTaskbar = false;
                _ = engine.Handle; // Create an invisible dispatch handle, never Show().
                engine.FormClosed += (_, _) => context.ExitThread();
                engine.PublishDesktopSnapshot();
                _ready.TrySetResult(true);
                Application.Run(context);
            }
            catch (Exception error)
            {
                _ready.TrySetException(error);
                Logger.Error("[desktop-engine] " + error);
            }
            finally { _engine = null; _stopped.TrySetResult(true); }
        }

        private async Task DispatchAsync(Action<Form1> action)
        {
            await Ready.ConfigureAwait(false);
            if (_shutdown) return;
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var engine = _engine;
            if (engine == null || engine.IsDisposed) throw new InvalidOperationException("Windows 内核已经关闭。");
            engine.BeginInvoke((Action)(() =>
            {
                try { action(engine); engine.PublishDesktopSnapshot(); completion.TrySetResult(true); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }));
            await completion.Task.ConfigureAwait(false);
        }

        public Task ExecuteAsync(DesktopCommand command, string? color = null) => DispatchAsync(form => form.ExecuteDesktopCommand(command, color));
        public Task SetParametersAsync(int workers, int step, bool showRange, bool extreme) => DispatchAsync(form => form.SetDesktopParameters(workers, step, showRange, extreme));
        public Task SetCompactAsync(bool compact) => DispatchAsync(form => form.SetDesktopCompact(compact));

        internal void Publish(DesktopSnapshot snapshot)
        {
            _snapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
        internal void RequestActivation() => ActivationRequested?.Invoke();
        internal void SetSelectingRange(bool selecting) => RangeSelectionChanged?.Invoke(selecting);

        internal EngineAnswer Confirm(EnginePrompt prompt)
        {
            if (_shutdown || PromptHandler == null) return new EngineAnswer(false);
            Interlocked.Increment(ref _promptDepth);
            try
            {
                var task = PromptHandler(prompt);
                // Keep the STA pump alive while Avalonia owns the modal dialog.
                // Blocking this thread would starve the global low-level keyboard hook.
                while (!task.IsCompleted && !_shutdown)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                return _shutdown ? new EngineAnswer(false) : task.GetAwaiter().GetResult();
            }
            finally { Interlocked.Decrement(ref _promptDepth); }
        }

        public async Task ShutdownAsync()
        {
            if (_shutdown) { await _stopped.Task.ConfigureAwait(false); return; }
            await Ready.ConfigureAwait(false);
            _shutdown = true;
            var engine = _engine;
            if (engine == null) return;
            engine.BeginInvoke((Action)(() => engine.ShutdownDesktopEngine()));
            await _stopped.Task.ConfigureAwait(false);
        }

        private static IReadOnlyList<string> GetPalette()
        {
            var list = new List<string>();
            foreach (var color in PaletteColorMatcher.Colors) list.Add(ToHex(color));
            return list.AsReadOnly();
        }
        internal static string ToHex(BgrColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
