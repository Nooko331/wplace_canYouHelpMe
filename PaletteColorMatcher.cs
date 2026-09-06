using System;
using System.Collections.Generic;

namespace WplaceColorWatch
{

/// <summary>
/// The 63 canonical paint colors and their display aliases. A display alias always
/// resolves back to a canonical color before wanted/excluded rules are applied.
/// </summary>
public static class PaletteColorMatcher
{
    // Compatibility assumption calibrated from #600018 -> #AC7A84.
    // This is a fixed 50% opacity profile over #F8F4F0, not automatic opacity detection.
    public static BgrColor BlendBackground { get; } = new BgrColor(240, 244, 248);

    public static IReadOnlyList<BgrColor> Colors { get; } = Array.AsReadOnly(new[]
    {
        new BgrColor(0, 0, 0),       // Black
        new BgrColor(60, 60, 60),    // Dark Gray
        new BgrColor(120, 120, 120), // Gray
        new BgrColor(170, 170, 170), // Medium Gray
        new BgrColor(210, 210, 210), // Light Gray
        new BgrColor(255, 255, 255), // White
        new BgrColor(24, 0, 96),     // Deep Red (BGR: 24, 0, 96) -> RGB: 96, 0, 24
        new BgrColor(30, 14, 165),   // Dark Red (BGR: 30, 14, 165) -> RGB: 165, 14, 30
        new BgrColor(36, 28, 237),   // Red (BGR: 36, 28, 237) -> RGB: 237, 28, 36
        new BgrColor(114, 128, 250), // Light Red (BGR: 114, 128, 250) -> RGB: 250, 128, 114
        new BgrColor(26, 92, 228),   // Dark Orange (BGR: 26, 92, 228) -> RGB: 228, 92, 26
        new BgrColor(39, 127, 255),  // Orange (BGR: 39, 127, 255) -> RGB: 255, 127, 39
        new BgrColor(9, 170, 246),   // Gold (BGR: 9, 170, 246) -> RGB: 246, 170, 9
        new BgrColor(59, 221, 249),  // Yellow (BGR: 59, 221, 249) -> RGB: 249, 221, 59
        new BgrColor(188, 250, 255), // Light Yellow (BGR: 188, 250, 255) -> RGB: 255, 250, 188
        new BgrColor(49, 132, 156),  // Dark Goldenrod (BGR: 49, 132, 156) -> RGB: 156, 132, 49
        new BgrColor(49, 173, 197),  // Goldenrod (BGR: 49, 173, 197) -> RGB: 197, 173, 49
        new BgrColor(95, 212, 232),  // Light Goldenrod (BGR: 95, 212, 232) -> RGB: 232, 212, 95
        new BgrColor(58, 107, 74),   // Dark Olive (BGR: 58, 107, 74) -> RGB: 74, 107, 58
        new BgrColor(74, 148, 90),   // Olive (BGR: 74, 148, 90) -> RGB: 90, 148, 74
        new BgrColor(115, 197, 132), // Light Olive (BGR: 115, 197, 132) -> RGB: 132, 197, 115
        new BgrColor(104, 185, 14),  // Dark Green (BGR: 104, 185, 14) -> RGB: 14, 185, 104
        new BgrColor(123, 230, 19),  // Green (BGR: 123, 230, 19) -> RGB: 19, 230, 123
        new BgrColor(94, 255, 135),  // Light Green (BGR: 94, 255, 135) -> RGB: 135, 255, 94
        new BgrColor(110, 129, 12),  // Dark Teal (BGR: 110, 129, 12) -> RGB: 12, 129, 110
        new BgrColor(166, 174, 16),  // Teal (BGR: 166, 174, 16) -> RGB: 16, 174, 166
        new BgrColor(190, 225, 19),  // Light Teal (BGR: 190, 225, 19) -> RGB: 19, 225, 190
        new BgrColor(159, 121, 15),  // Dark Cyan (BGR: 159, 121, 15) -> RGB: 15, 121, 159
        new BgrColor(242, 247, 96),  // Cyan (BGR: 242, 247, 96) -> RGB: 96, 247, 242
        new BgrColor(242, 250, 187), // Light Cyan (BGR: 242, 250, 187) -> RGB: 187, 250, 242
        new BgrColor(158, 80, 40),   // Dark Blue (BGR: 158, 80, 40) -> RGB: 40, 80, 158
        new BgrColor(228, 147, 64),  // Blue (BGR: 228, 147, 64) -> RGB: 64, 147, 228
        new BgrColor(255, 199, 125), // Light Blue (BGR: 255, 199, 125) -> RGB: 125, 199, 255
        new BgrColor(184, 49, 77),   // Dark Indigo (BGR: 184, 49, 77) -> RGB: 77, 49, 184
        new BgrColor(246, 80, 107),  // Indigo (BGR: 246, 80, 107) -> RGB: 107, 80, 246
        new BgrColor(251, 177, 153), // Light Indigo (BGR: 251, 177, 153) -> RGB: 153, 177, 251
        new BgrColor(132, 66, 74),   // Dark Slate Blue (BGR: 132, 66, 74) -> RGB: 74, 66, 132
        new BgrColor(196, 113, 122), // Slate Blue (BGR: 196, 113, 122) -> RGB: 122, 113, 196
        new BgrColor(241, 174, 181), // Light Slate Blue (BGR: 241, 174, 181) -> RGB: 181, 174, 241
        new BgrColor(153, 12, 120),  // Dark Purple (BGR: 153, 12, 120) -> RGB: 120, 12, 153
        new BgrColor(185, 56, 170),  // Purple (BGR: 185, 56, 170) -> RGB: 170, 56, 185
        new BgrColor(249, 159, 224), // Light Purple (BGR: 249, 159, 224) -> RGB: 224, 159, 249
        new BgrColor(122, 0, 203),   // Dark Pink (BGR: 122, 0, 203) -> RGB: 203, 0, 122
        new BgrColor(128, 31, 236),  // Pink (BGR: 128, 31, 236) -> RGB: 236, 31, 128
        new BgrColor(169, 141, 243), // Light Pink (BGR: 169, 141, 243) -> RGB: 243, 141, 169
        new BgrColor(73, 82, 155),   // Dark Peach (BGR: 73, 82, 155) -> RGB: 155, 82, 73
        new BgrColor(120, 128, 209), // Peach (BGR: 120, 128, 209) -> RGB: 209, 128, 120
        new BgrColor(164, 182, 250), // Light Peach (BGR: 164, 182, 250) -> RGB: 250, 182, 164
        new BgrColor(52, 70, 104),   // Dark Brown (BGR: 52, 70, 104) -> RGB: 104, 70, 52
        new BgrColor(42, 104, 149),  // Brown (BGR: 42, 104, 149) -> RGB: 149, 104, 42
        new BgrColor(99, 164, 219),  // Light Brown (BGR: 99, 164, 219) -> RGB: 219, 164, 99
        new BgrColor(82, 99, 123),   // Dark Tan (BGR: 82, 99, 123) -> RGB: 123, 99, 82
        new BgrColor(107, 132, 156), // Tan (BGR: 107, 132, 156) -> RGB: 156, 132, 107
        new BgrColor(148, 181, 214), // Light Tan (BGR: 148, 181, 214) -> RGB: 214, 181, 148
        new BgrColor(81, 128, 209),  // Dark Beige (BGR: 81, 128, 209) -> RGB: 209, 128, 81
        new BgrColor(119, 178, 248), // Beige (BGR: 119, 178, 248) -> RGB: 248, 178, 119
        new BgrColor(165, 197, 255), // Light Beige (BGR: 165, 197, 255) -> RGB: 255, 197, 165
        new BgrColor(63, 100, 109),  // Dark Stone (BGR: 63, 100, 109) -> RGB: 109, 100, 63
        new BgrColor(107, 140, 148), // Stone (BGR: 107, 140, 148) -> RGB: 148, 140, 107
        new BgrColor(158, 197, 205), // Light Stone (BGR: 158, 197, 205) -> RGB: 205, 197, 158
        new BgrColor(65, 57, 51),    // Dark Slate (BGR: 65, 57, 51) -> RGB: 51, 57, 65
        new BgrColor(141, 117, 109), // Slate (BGR: 141, 117, 109) -> RGB: 109, 117, 141
        new BgrColor(209, 185, 179)  // Light Slate (BGR: 209, 185, 179) -> RGB: 179, 185, 209
    });

    private static readonly DisplayRange[] HalfTransparentRanges = CreateDisplayRanges();

    /// <summary>Rounded-up representative for the built-in 50% display color.</summary>
    public static BgrColor GetHalfTransparentColor(BgrColor color)
    {
        return BlendHalf(color, roundUp: true);
    }

    /// <summary>
    /// Resolves exact opaque colors or calculated display aliases to canonical colors.
    /// Otherwise returns the nearest opaque color for the caller's existing tolerance.
    /// Do not extend that tolerance around display aliases: the compressed palette would
    /// turn the modeled background into white at the default island tolerance of 10.
    /// </summary>
    public static (BgrColor color, int diff) FindNearest(BgrColor sample)
    {
        var best = Colors[0];
        int minDiff = int.MaxValue;
        foreach (var color in Colors)
        {
            int diff = sample.MaxDiff(color);
            if (diff < minDiff)
            {
                minDiff = diff;
                best = color;
                if (diff == 0) return (best, 0);
            }
        }

        foreach (var range in HalfTransparentRanges)
        {
            if (range.Contains(sample))
            {
                return (range.Original, 0);
            }
        }
        return (best, minDiff);
    }

    public static bool TryMatch(BgrColor sample, int tolerance, out BgrColor color)
    {
        var match = FindNearest(sample);
        color = match.color;
        return match.diff <= tolerance;
    }

    private static BgrColor BlendHalf(BgrColor color, bool roundUp)
    {
        int rounding = roundUp ? 1 : 0;
        return new BgrColor(
            (byte)((color.B + BlendBackground.B + rounding) / 2),
            (byte)((color.G + BlendBackground.G + rounding) / 2),
            (byte)((color.R + BlendBackground.R + rounding) / 2));
    }

    private static DisplayRange[] CreateDisplayRanges()
    {
        var ranges = new DisplayRange[Colors.Count];
        for (int i = 0; i < Colors.Count; i++)
        {
            var color = Colors[i];
            ranges[i] = new DisplayRange(color, BlendHalf(color, false), BlendHalf(color, true));
        }
        return ranges;
    }

    private readonly struct DisplayRange
    {
        public BgrColor Original { get; }
        private readonly BgrColor _lower;
        private readonly BgrColor _upper;

        public DisplayRange(BgrColor original, BgrColor lower, BgrColor upper)
        {
            Original = original;
            _lower = lower;
            _upper = upper;
        }

        public bool Contains(BgrColor sample)
        {
            return sample.R >= _lower.R && sample.R <= _upper.R &&
                   sample.G >= _lower.G && sample.G <= _upper.G &&
                   sample.B >= _lower.B && sample.B <= _upper.B;
        }
    }
}
}
