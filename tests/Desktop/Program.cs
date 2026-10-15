using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using WplaceColorWatch;

internal static class Program
{
    private static int passed;
    private static readonly string DirectoryPath = Path.Combine(AppContext.BaseDirectory, "artifacts", Guid.NewGuid().ToString("N"));
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name); passed++;
    }

    // The harness owns its canvas and disables global hooks. It never sends
    // painting input: cancellation is queued on the STA before fill can start.
    private static async Task<int> Main()
    {
        Directory.CreateDirectory(DirectoryPath);
        Control.CheckForIllegalCrossThreadCalls = true;
        var path = Path.Combine(DirectoryPath, "user_settings.json");
        // Simulate settings saved while the previous version was in compact mode.
        File.WriteAllText(path, """
            { "ScanWorkers": 1, "ScanStep": 14, "SpeedPreset": 1, "CompactLayout": true }
            """);
        var host = new WindowsEngineHost(enableHotkeys: false, settingsPath: path);
        try
        {
            await host.Ready.WaitAsync(TimeSpan.FromSeconds(10));
            var form = Get<Form1>(host, "_engine");
            Check(await OnEngine(form, () => !form.Visible && Thread.CurrentThread.GetApartmentState() == ApartmentState.STA), "Legacy window stays hidden on its STA thread");
            Check(await OnEngine(form, () => Get<IntPtr>(form, "_hookId") == IntPtr.Zero), "Integration harness installs no global hook");
            Check(host.Snapshot.Step == 14 && host.Snapshot.Extreme, "Existing settings load through the bridge");
            Check(!host.Snapshot.Compact, "Legacy saved compact layout cannot override full mode at startup");

            await host.SetParametersAsync(1, 17, false, false);
            await host.SetCompactAsync(true);
            Check(host.Snapshot.Step == 17 && !host.Snapshot.Extreme && host.Snapshot.Compact, "Parameters and layout reach the original engine");
            var invalidRejected = false;
            try { await host.SetParametersAsync(1, 0, false, false); }
            catch (ArgumentOutOfRangeException) { invalidRejected = true; }
            Check(invalidRejected && host.Snapshot.Step == 17, "Invalid step cannot overwrite working parameters");

            await host.ExecuteAsync(DesktopCommand.AddWanted, "#ED1C24");
            await host.ExecuteAsync(DesktopCommand.AddExcluded, "#ED1C24");
            Check(host.Snapshot.Wanted.Count == 0 && host.Snapshot.Excluded.Count == 1 && host.Snapshot.EffectiveCount == 62, "Color rules retain mutually exclusive semantics");
            await host.ExecuteAsync(DesktopCommand.AddWanted, "#123456");
            Check(host.Snapshot.Wanted.Count == 0, "Non-palette colors are rejected");
            await host.ExecuteAsync(DesktopCommand.SelectAllColors);
            Check(host.Snapshot.Wanted.Count == 63 && host.Snapshot.Excluded.Count == 0, "All colors resets exclusions");
            await host.ExecuteAsync(DesktopCommand.ClearWanted);

            await OnEngine(form, () =>
            {
                Get<RuntimeState>(form, "_state").RecordColors(new() { new BgrColor(3, 2, 1), new BgrColor(6, 5, 4) }, new() { new BgrColor(3, 2, 1), new BgrColor(6, 5, 4) }, new Point(50, 50));
                form.PublishDesktopSnapshot(); return true;
            });
            Check(host.Snapshot.HoverColor == "#010203" && host.Snapshot.ClearColor == "#040506", "Both raw sampled color channels survive the bridge");

            var prompted = new TaskCompletionSource<EnginePrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
            var answer = new TaskCompletionSource<EngineAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.PromptHandler = prompt => { prompted.TrySetResult(prompt); return answer.Task; };
            var startWithoutRange = host.ExecuteAsync(DesktopCommand.StartFill);
            var missingRangePrompt = await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(missingRangePrompt.Message.Contains("框选范围") && host.PromptOpen, "Original start validation reaches the new prompt handler");
            await host.SetParametersAsync(1, 99, false, false).WaitAsync(TimeSpan.FromSeconds(3));
            Check(host.Snapshot.Step == 17, "STA stays responsive and rejects edits while a modal prompt is open");
            answer.SetResult(new EngineAnswer(true));
            await startWithoutRange.WaitAsync(TimeSpan.FromSeconds(5));

            await host.ExecuteAsync(DesktopCommand.OpenColors);
            await host.ExecuteAsync(DesktopCommand.PickWanted);
            Check(host.Snapshot.Picking == "Wanted" && host.Snapshot.ColorsOpen, "Continuous color selection enters the original pick state");
            await host.ExecuteAsync(DesktopCommand.StopPicking);
            Check(host.Snapshot.Picking == "" && host.Snapshot.ColorsOpen, "Esc-equivalent stops picking without closing color management");
            await host.ExecuteAsync(DesktopCommand.CloseColors);
            Check(!host.Snapshot.ColorsOpen, "Closing color management clears its engine state");

            var canvas = await OnEngine(form, () =>
            {
                var screen = Screen.PrimaryScreen!.WorkingArea;
                var c = new Form { Text = "Wplace integration test canvas", BackColor = Color.FromArgb(11, 77, 113), FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(screen.Left + 90, screen.Top + 90, 260, 160), TopMost = true };
                c.Show(); c.Refresh();
                Get<RuntimeState>(form, "_state").SetRange(new Rectangle(c.Left + 20, c.Top + 20, 200, 100), null);
                form.PublishDesktopSnapshot(); return c;
            });
            try
            {
                host.PromptHandler = prompt => Task.FromResult(new EngineAnswer(false));
                await host.ExecuteAsync(DesktopCommand.DetectMissing);
                Check(!host.Snapshot.Busy, "Declining the original missing-detection recommendation never starts a task");

                var resultPrompt = new TaskCompletionSource<EnginePrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
                host.PromptHandler = prompt =>
                {
                    if (prompt.CheckBox == null) resultPrompt.TrySetResult(prompt);
                    return Task.FromResult(new EngineAnswer(true, prompt.CheckBox != null));
                };
                await host.SetParametersAsync(1, 10, false, false);
                await host.ExecuteAsync(DesktopCommand.DetectMissing);
                var result = await resultPrompt.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Until(() => !host.Snapshot.Busy);
                Check(result.Message.Contains("未检测到遗漏"), "Real missing-detection scan runs against the harness canvas and returns through the prompt bridge");
                Check(await OnEngine(form, () => Get<UserSettings>(form, "_settings").SkipIslandRecommendation), "Recommendation checkbox persists its original setting");

                await OnEngine(form, () =>
                {
                    form.ExecuteDesktopCommand(DesktopCommand.StartFill, null);
                    form.ExecuteDesktopCommand(DesktopCommand.Stop, null);
                    form.PublishDesktopSnapshot(); return true;
                });
                await Until(() => !host.Snapshot.Busy);
                Check(host.Snapshot.Status == "已停止", "Immediate cancel drains a scheduled scan without painting");

                await OnEngine(form, () =>
                {
                    form.ExecuteDesktopCommand(DesktopCommand.StartFill, null);
                    var key = Marshal.AllocHGlobal(24);
                    try
                    {
                        for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(key, i, 0);
                        Marshal.WriteInt32(key, 0, 0x1B);
                        var handled = (IntPtr)Call(form, "HookCallback", 0, new IntPtr(0x100), key)!;
                        if (handled != new IntPtr(1)) throw new Exception("Global Esc did not handle the active task.");
                    }
                    finally { Marshal.FreeHGlobal(key); }
                    return true;
                });
                await Until(() => !host.Snapshot.Busy);
                Check(host.Snapshot.Status == "已停止", "Original global Esc callback cancels the bridged task");
            }
            finally { await OnEngine(form, () => { canvas.Dispose(); return true; }); }

            await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var saved = UserSettings.Load(path);
            Check(saved.ScanStep == 10 && saved.SkipIslandRecommendation, "Clean shutdown preserves existing scan and prompt preferences");
            using (var savedJson = JsonDocument.Parse(File.ReadAllText(path)))
                Check(!savedJson.RootElement.TryGetProperty("CompactLayout", out _), "Closing in compact mode does not write layout into settings");
            var restart = new WindowsEngineHost(enableHotkeys: false, settingsPath: path);
            try
            {
                await restart.Ready.WaitAsync(TimeSpan.FromSeconds(10));
                Check(!restart.Snapshot.Compact && restart.Snapshot.Step == 10, "Restart opens in full mode while retaining the saved step");
                Check(restart.Snapshot.Wanted.Count == 0 && restart.Snapshot.Excluded.Count == 0, "Session-only color rules do not become persistent");
            }
            finally { await restart.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            Console.WriteLine($"{passed}/{passed} desktop bridge checks passed.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error); return 1; }
        finally { await host.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private static Task<T> OnEngine<T>(Form1 form, Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        form.BeginInvoke((Action)(() => { try { done.SetResult(work()); } catch (Exception error) { done.SetException(error); } }));
        return done.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(30, timeout.Token);
    }
}
