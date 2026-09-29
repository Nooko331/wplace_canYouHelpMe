using System.Drawing;
using WplaceColorWatch;

internal static class Program
{
    private static readonly (string Name, Action Run)[] Tests =
    {
        ("MatchingPaintIsVerifiedOnceThenReusesBrush", MatchingPaintReusesBrush),
        ("FirstWrongPaintRepicksAtNextPoint", FirstWrongPaintRepicks),
        ("ConfirmedBrushSkipsAllLaterVerification", ConfirmedBrushSkipsVerification),
        ("ConsecutiveFailuresRepickWithoutLooping", ConsecutiveFailuresRepick),
        ("RejectedPickNeverVerifiesAnUnpaintedPoint", RejectedPickSkips),
        ("RejectedRecoveryPickStillRequiresPicking", RejectedRecoveryPick),
        ("OnlyUnconfirmedPickedPointsAreVerifiedAtEnd", LastAndSinglePoints),
        ("EachColorGroupStartsWithANewPick", EachGroupPicksAgain),
        ("CancellationDuringVerificationStopsCurrentFill", CancelDuringVerification),
        ("CancellationDuringPickingStopsCurrentFill", CancelDuringPicking),
        ("CancellationAfterMovingStopsBeforeActions", CancelAfterMoving)
    };

    private static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            foreach (var test in Tests) Console.WriteLine(test.Name);
            return 0;
        }
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
        Console.WriteLine($"{Tests.Length - failures}/{Tests.Length} auto-fill tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void MatchingPaintReusesBrush()
    {
        var f = new Fixture();
        Require(f.Run(), "No points were filled.");
        Require(f.Picks.SequenceEqual(new[] { 0 }), "A confirmed brush should be reused.");
        Require(f.Verified.SequenceEqual(new[] { 0 }), "A successful pick must be verified only once for this color group.");
        Require(f.Pixels.Values.All(color => color == f.Target), "The simulated paint differs from the target.");
        Require(f.Events.IndexOf("move:1") < f.Events.IndexOf("verify:0") &&
            f.Events.IndexOf("verify:0") < f.Events.IndexOf("fill:1:reuse"),
            "The previous point must be checked after moving and before painting.");
        Require(f.Processed == 4, "Progress did not cover all points.");
        Require(f.Events.Last() == "progress:4", "An already confirmed group must not add a final check.");
    }

    private static void FirstWrongPaintRepicks()
    {
        var f = new Fixture();
        f.PickBrush = index => index == 0 ? f.Wrong : f.Target;
        f.Run();
        Require(f.Picks.SequenceEqual(new[] { 0, 1 }), "The failed first pick was reused or recovery kept picking after success.");
        Require(f.Verified.SequenceEqual(new[] { 0, 1 }), "Recovery must verify the new pick, then stop checking after success.");
        Require(f.Pixels[f.Points[1]] == f.Target && f.Pixels[f.Points[3]] == f.Target,
            "Recovery did not paint the current and following points with the target.");
        Require(f.Events.IndexOf("verify:0") < f.Events.IndexOf("pick:1") &&
            f.Events.IndexOf("pick:1") < f.Events.IndexOf("fill:1:pick"), "Recovery occurred after painting the current point.");
    }

    private static void ConfirmedBrushSkipsVerification()
    {
        var f = new Fixture();
        f.BeforeFill = index => { if (index == 2) f.Brush = f.Wrong; };
        f.Run();
        Require(f.Picks.SequenceEqual(new[] { 0 }) && f.Verified.SequenceEqual(new[] { 0 }),
            "An already confirmed brush must continue without further sampling or checks.");
        Require(f.Processed == 4 && f.Pixels[f.Points[3]] == f.Wrong,
            "The later points were not filled directly with the current brush.");
    }

    private static void ConsecutiveFailuresRepick()
    {
        var f = new Fixture { PickBrush = _ => new BgrColor(0, 0, 0) };
        f.Run();
        Require(f.Picks.SequenceEqual(new[] { 0, 1, 2, 3 }), "Each failed paint must trigger a new pick at the next point.");
        Require(f.Processed == 4 && f.Verified.Count == 4, "Repeated failures must finish the finite point list.");
    }

    private static void RejectedPickSkips()
    {
        var f = new Fixture { RejectPick = index => index == 0 };
        f.Run();
        Require(f.Picks.SequenceEqual(new[] { 0, 1 }), "A skipped first point must leave picking required.");
        Require(f.Verified.SequenceEqual(new[] { 1 }) && !f.Pixels.ContainsKey(f.Points[0]),
            "Only the successful picked point must be checked; skipped and subsequent points must not be checked.");
        Require(f.Processed == 4, "Skipped points must still advance existing task progress.");
    }

    private static void RejectedRecoveryPick()
    {
        var f = new Fixture { RejectPick = index => index == 1 };
        f.PickBrush = index => index == 0 ? f.Wrong : f.Target;
        f.Run();
        Require(f.Picks.SequenceEqual(new[] { 0, 1, 2 }), "Recovery reused a rejected pick.");
        Require(f.Verified.SequenceEqual(new[] { 0, 2 }), "Only filled points after fresh picks may be verified during recovery.");
        Require(f.Pixels[f.Points[2]] == f.Target, "Recovery failed after skipping an unusable sample.");
    }

    private static void LastAndSinglePoints()
    {
        var f = new Fixture();
        f.Run(new[] { f.Points[0] });
        Require(f.Verified.SequenceEqual(new[] { 0 }), "A single-point group has no verification.");
        Require(f.Events.Last() == "verify:0", "The last paint must be checked before finishing the group.");
        var failed = new Fixture();
        failed.PickBrush = _ => failed.Wrong;
        failed.Run(new[] { failed.Points[0] });
        Require(failed.Verified.Count == 1 && failed.Picks.Count == 1,
            "A failed terminal check must be recorded without retrying the consumed point indefinitely.");
        var confirmed = new Fixture();
        confirmed.Run();
        Require(confirmed.Verified.SequenceEqual(new[] { 0 }), "The final point of a confirmed group was unnecessarily checked.");
        var pickedAtEnd = new Fixture();
        pickedAtEnd.PickBrush = index => index == 3 ? pickedAtEnd.Target : pickedAtEnd.Wrong;
        pickedAtEnd.Run();
        Require(pickedAtEnd.Picks.SequenceEqual(new[] { 0, 1, 2, 3 }) &&
            pickedAtEnd.Verified.SequenceEqual(new[] { 0, 1, 2, 3 }), "A fresh pick at the last point lost its verification.");
    }

    private static void EachGroupPicksAgain()
    {
        var f = new Fixture();
        f.Run(f.Points.Take(2).ToArray());
        f.Run(f.Points.Skip(2).ToArray());
        Require(f.Picks.SequenceEqual(new[] { 0, 2 }), "A new color group inherited brush confidence from the old group.");
        Require(f.Verified.SequenceEqual(new[] { 0, 2 }), "Each color group must validate its own pick exactly once.");
    }

    private static void CancelDuringVerification()
    {
        using var cts = new CancellationTokenSource();
        var f = new Fixture();
        f.PickBrush = _ => f.Wrong;
        f.BeforeVerify = _ => cts.Cancel();
        RequireCancelled(() => f.Run(token: cts.Token));
        Require(f.Picks.SequenceEqual(new[] { 0 }) && f.Pixels.Count == 1 && f.Processed == 1,
            "Cancellation during a failed check must not repick or paint the next point.");
    }

    private static void CancelDuringPicking()
    {
        using var cts = new CancellationTokenSource();
        var f = new Fixture { BeforePick = _ => cts.Cancel() };
        RequireCancelled(() => f.Run(token: cts.Token));
        Require(f.Pixels.Count == 0 && f.Processed == 0, "Cancellation after sampling still sent a fill.");
    }

    private static void CancelAfterMoving()
    {
        using var cts = new CancellationTokenSource();
        var f = new Fixture { AfterMove = _ => cts.Cancel() };
        RequireCancelled(() => f.Run(token: cts.Token));
        Require(f.Picks.Count == 0 && f.Verified.Count == 0 && f.Pixels.Count == 0,
            "Cancellation after moving did not stop input and screen operations.");
    }

    private static void RequireCancelled(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected cancellation.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Fixture
    {
        public Point[] Points { get; } = Enumerable.Range(0, 4).Select(i => new Point(100 + i * 10, 100)).ToArray();
        public BgrColor Target { get; } = new(24, 0, 96);
        public BgrColor Wrong { get; } = new(36, 28, 237);
        public BgrColor Brush { get; set; }
        public Dictionary<Point, BgrColor> Pixels { get; } = new();
        public List<int> Picks { get; } = new();
        public List<int> Verified { get; } = new();
        public List<string> Events { get; } = new();
        public int Processed { get; private set; }
        public Func<int, BgrColor>? PickBrush { get; set; }
        public Func<int, bool> RejectPick { get; set; } = _ => false;
        public Action<int>? AfterMove { get; set; }
        public Action<int>? BeforeVerify { get; set; }
        public Action<int>? BeforePick { get; set; }
        public Action<int>? BeforeFill { get; set; }
        private int _current;

        public bool Run(IReadOnlyList<Point>? points = null, CancellationToken token = default) =>
            AutoFillColorGroup.Execute(points ?? Points,
                point =>
                {
                    _current = Array.IndexOf(Points, point);
                    Events.Add($"move:{_current}");
                    AfterMove?.Invoke(_current);
                },
                point =>
                {
                    int index = Array.IndexOf(Points, point);
                    Events.Add($"verify:{index}");
                    Verified.Add(index);
                    BeforeVerify?.Invoke(index);
                    return Pixels[point] == Target;
                },
                point =>
                {
                    int index = Array.IndexOf(Points, point);
                    Events.Add($"pick:{index}");
                    Picks.Add(index);
                    BeforePick?.Invoke(index);
                    if (RejectPick(index)) return false;
                    Brush = PickBrush?.Invoke(index) ?? Target;
                    return true;
                },
                pickedNow =>
                {
                    BeforeFill?.Invoke(_current);
                    Events.Add($"fill:{_current}:{(pickedNow ? "pick" : "reuse")}");
                    Pixels[Points[_current]] = Brush;
                },
                () => { Processed++; Events.Add($"progress:{Processed}"); }, token);
    }
}
