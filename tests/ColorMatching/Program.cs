using WplaceColorWatch;

internal static class Program
{
    private static readonly (string Name, Action Run)[] Cases =
    {
        ("OriginalPaletteRetainsAll63Colors", OriginalPaletteRetainsAll63Colors),
        ("ReportedDisplaySamplesMapToOriginal", ReportedDisplaySamplesMapToOriginal),
        ("EveryHalfTransparentColorMapsBackToOriginal", EveryHalfTransparentColorMapsBackToOriginal),
        ("AllHalfUnitRoundingCombinationsMapBack", AllHalfUnitRoundingCombinationsMapBack),
        ("ToleranceIsNotGloballyWidened", ToleranceIsNotGloballyWidened),
        ("HoverAndBackgroundAreNotExactMatches", HoverAndBackgroundAreNotExactMatches),
        ("BackgroundRemainsUnmatchedAtIslandTolerance", BackgroundRemainsUnmatchedAtIslandTolerance),
        ("ExcludedOriginalAlsoExcludesBlendedSample", ExcludedOriginalAlsoExcludesBlendedSample),
        ("WantedListRemainsCanonical", WantedListRemainsCanonical),
        ("MixedRenderingsShareGroupsAndPickedColors", MixedRenderingsShareGroupsAndPickedColors)
    };

    private static int Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            foreach (var test in Cases) Console.WriteLine(test.Name);
            return 0;
        }

        int failures = 0;
        foreach (var test in Cases)
        {
            try
            {
                test.Run();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"{Cases.Length - failures}/{Cases.Length} passed");
        return failures == 0 ? 0 : 1;
    }

    private static void OriginalPaletteRetainsAll63Colors()
    {
        // Snapshot of the original Form1 palette before extracting the matcher.
        const string expected = "#000000 #3C3C3C #787878 #AAAAAA #D2D2D2 #FFFFFF #600018 #A50E1E #ED1C24 #FA8072 #E45C1A #FF7F27 #F6AA09 #F9DD3B #FFFABC #9C8431 #C5AD31 #E8D45F #4A6B3A #5A944A #84C573 #0EB968 #13E67B #87FF5E #0C816E #10AEA6 #13E1BE #0F799F #60F7F2 #BBFAF2 #28509E #4093E4 #7DC7FF #4D31B8 #6B50F6 #99B1FB #4A4284 #7A71C4 #B5AEF1 #780C99 #AA38B9 #E09FF9 #CB007A #EC1F80 #F38DA9 #9B5249 #D18078 #FAB6A4 #684634 #95682A #DBA463 #7B6352 #9C846B #D6B594 #D18051 #F8B277 #FFC5A5 #6D643F #948C6B #CDC59E #333941 #6D758D #B3B9D1";
        Require(string.Join(" ", PaletteColorMatcher.Colors.Select(Hex)) == expected,
            "Canonical palette values or ordering changed.");
        Require(PaletteColorMatcher.Colors.Distinct().Count() == 63, "Expected 63 unique originals.");
        foreach (var color in PaletteColorMatcher.Colors) RequireMatch(color, color);
    }

    private static void ReportedDisplaySamplesMapToOriginal()
    {
        var original = Rgb(96, 0, 24);
        var displayed = Rgb(172, 122, 132);
        Require(PaletteColorMatcher.GetHalfTransparentColor(original) == displayed,
            "The user's #600018 -> #AC7A84 calibration must be reproduced.");
        RequireMatch(displayed, original);
        RequireMatch(original, original);

        // Independent screen measurements supplied by the user. These use the
        // lower half-unit rounding variants, not the table's upper representative.
        RequireMatch(Rgb(233, 204, 169), Rgb(219, 164, 99)); // #E9CCA9 -> #DBA463
        RequireMatch(Rgb(202, 188, 144), Rgb(156, 132, 49)); // #CABC90 -> #9C8431
        RequireMatch(Rgb(184, 128, 196), Rgb(120, 12, 153)); // #B880C4 -> #780C99
    }

    private static void EveryHalfTransparentColorMapsBackToOriginal()
    {
        var seen = new HashSet<BgrColor>();
        foreach (var original in PaletteColorMatcher.Colors)
        {
            var displayed = Rgb(
                (int)Math.Round((original.R + 248) * 0.5, MidpointRounding.AwayFromZero),
                (int)Math.Round((original.G + 244) * 0.5, MidpointRounding.AwayFromZero),
                (int)Math.Round((original.B + 240) * 0.5, MidpointRounding.AwayFromZero));
            Require(seen.Add(displayed), $"Display alias collision at {Hex(displayed)}.");
            Require(PaletteColorMatcher.GetHalfTransparentColor(original) == displayed,
                $"Incorrect displayed color for {Hex(original)}.");
            RequireMatch(displayed, original);
        }
        Require(seen.Count == 63, "Expected 63 distinct calculated display colors.");
    }

    private static void AllHalfUnitRoundingCombinationsMapBack()
    {
        foreach (var original in PaletteColorMatcher.Colors)
        {
            double[] components = { (original.R + 248) * 0.5, (original.G + 244) * 0.5,
                                    (original.B + 240) * 0.5 };
            for (int mask = 0; mask < 8; mask++)
            {
                int[] rgb = components.Select((value, channel) =>
                    (int)(((mask >> channel) & 1) == 0 ? Math.Floor(value) : Math.Ceiling(value))).ToArray();
                RequireMatch(Rgb(rgb[0], rgb[1], rgb[2]), original);
            }
        }
    }

    private static void ToleranceIsNotGloballyWidened()
    {
        var gray = Rgb(120, 120, 120);
        var shiftedGray = Rgb(121, 120, 120);
        Require(!PaletteColorMatcher.TryMatch(shiftedGray, 0, out _),
            "Opaque near matches must remain rejected at tolerance 0.");
        RequireMatch(shiftedGray, gray, 1);
        var shiftedDeepRed = Rgb(172, 122, 133);
        Require(!PaletteColorMatcher.TryMatch(shiftedDeepRed, 0, out _),
            "A whole-unit display shift is not a half-unit rounding alternative.");
        Require(!PaletteColorMatcher.TryMatch(shiftedDeepRed, 1, out _),
            "Opaque tolerance must not expand the calculated display aliases.");
    }

    private static void HoverAndBackgroundAreNotExactMatches()
    {
        foreach (var sample in new[] { Rgb(185, 155, 161), Rgb(248, 244, 240),
                                       Rgb(180, 0, 0), Rgb(255, 0, 255) })
        {
            Require(!PaletteColorMatcher.TryMatch(sample, 0, out _),
                $"Unrelated/hover sample {Hex(sample)} was classified as an exact palette color.");
        }
    }

    private static void BackgroundRemainsUnmatchedAtIslandTolerance()
    {
        Require(!PaletteColorMatcher.TryMatch(Rgb(248, 244, 240), 10, out _),
            "The assumed canvas background must not become a white island label at default island tolerance.");
        Require(!PaletteColorMatcher.TryMatch(Rgb(248, 244, 240), 12, out _),
            "The assumed background must also remain outside the color-manager sampling tolerance.");
    }

    private static void ExcludedOriginalAlsoExcludesBlendedSample()
    {
        var deepRed = Rgb(96, 0, 24);
        var rules = new ColorRuleSet(PaletteColorMatcher.Colors);
        rules.AddExcluded(deepRed);
        foreach (var sample in new[] { deepRed, Rgb(172, 122, 132) })
        {
            // Even a large caller tolerance must not reclassify an excluded exact alias
            // into the nearest allowed tan color.
            Require(PaletteColorMatcher.TryMatch(sample, 122, out var canonical) && canonical == deepRed,
                "Classification must precede allowed-color filtering.");
            Require(!rules.GetEffectiveColors().Contains(canonical), "Excluded display alias leaked through.");
        }

        rules.AddWanted(Rgb(156, 132, 107)); // Old raw nearest match for #AC7A84.
        Require(PaletteColorMatcher.TryMatch(Rgb(172, 122, 132), 25, out var actual) && actual == deepRed,
            "An allowed tan must not steal the deep-red display alias.");
        Require(!rules.GetEffectiveColors().Contains(actual), "Wanted-only filtering accepted an excluded alias.");
    }

    private static void WantedListRemainsCanonical()
    {
        var rules = new ColorRuleSet(PaletteColorMatcher.Colors);
        Require(PaletteColorMatcher.TryMatch(Rgb(172, 122, 132), 0, out var deepRed), "Sample did not match.");
        rules.AddWanted(deepRed);
        Require(rules.GetWanted().SequenceEqual(new[] { Rgb(96, 0, 24) }),
            "Color management must store/display the original color, not a second palette entry.");
        rules.AddExcluded(deepRed);
        Require(rules.GetWanted().Count == 0 && rules.GetExcluded().Single() == deepRed,
            "Wanted/excluded mutual exclusion was lost.");
        rules.SelectAllWanted();
        Require(rules.GetWanted().Count == 63 && rules.GetExcluded().Count == 0,
            "Select all should still mean 63 original colors.");
    }

    private static void MixedRenderingsShareGroupsAndPickedColors()
    {
        var groups = new Dictionary<BgrColor, int>();
        var originals = new[] { Rgb(96, 0, 24), Rgb(255, 255, 255), Rgb(0, 0, 0) };
        var picked = new HashSet<BgrColor>(originals);
        foreach (var original in originals)
        {
            foreach (var sample in new[] { original, PaletteColorMatcher.GetHalfTransparentColor(original) })
            {
                Require(PaletteColorMatcher.TryMatch(sample, 0, out var canonical), "Mixed sample did not match.");
                groups[canonical] = groups.GetValueOrDefault(canonical) + 1;
                Require(picked.Contains(canonical), "A rendered sample escaped canonical picked-color detection.");
            }
        }
        Require(groups.Count == 3 && groups.Values.All(count => count == 2),
            "Opaque and rendered variants must share one fill/island color group.");
        RequireMatch(Rgb(251, 249, 247), Rgb(255, 255, 255));
        RequireMatch(Rgb(252, 250, 248), Rgb(255, 255, 255));
    }

    private static void RequireMatch(BgrColor sample, BgrColor expected, int tolerance = 0)
    {
        Require(PaletteColorMatcher.TryMatch(sample, tolerance, out var actual) && actual == expected,
            $"{Hex(sample)} should resolve to {Hex(expected)} at tolerance {tolerance}, got {Hex(actual)}.");
    }

    private static BgrColor Rgb(int r, int g, int b) => new((byte)b, (byte)g, (byte)r);
    private static string Hex(BgrColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
