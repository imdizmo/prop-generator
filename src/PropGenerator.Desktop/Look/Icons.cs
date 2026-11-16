using Avalonia.Media;

namespace PropGenerator.Desktop.Look;

/// <summary>
/// The lucide icons the app draws, as path data on a 24 by 24 grid (stroked 2 wide with round caps and joins).
/// The table is made by <c>tools/lucide_icons.py</c> from lucide-react 1.65.0 (ISC licence, see NOTICE.md); do not edit
/// it by hand. A shape of an icon is one part, parts are separated by <c>|</c>.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Data = new(StringComparer.Ordinal)
    {
        ["trees"] = "M10 10v.2A3 3 0 0 1 8.9 16H5a3 3 0 0 1-1-5.8V10a3 3 0 0 1 6 0Z|M7 16v6|M13 19v3|M12 19h8.3a1 1 0 0 0 .7-1.7L18 14h.3a1 1 0 0 0 .7-1.7L16 9h.2a1 1 0 0 0 .8-1.7L13 3l-1.4 1.5",
        ["search"] = "m21 21-4.34-4.34|M3 11a8 8 0 1 0 16 0a8 8 0 1 0 -16 0Z",
        ["save"] = "M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z|M17 21v-7a1 1 0 0 0-1-1H8a1 1 0 0 0-1 1v7|M7 3v4a1 1 0 0 0 1 1h7",
        ["download"] = "M12 15V3|M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4|m7 10 5 5 5-5",
        ["check"] = "M20 6 9 17l-5-5",
        ["chevron-right"] = "m9 18 6-6-6-6",
        ["chevron-down"] = "m6 9 6 6 6-6",
        ["chevron-left"] = "m15 18-6-6 6-6",
        ["chevron-up"] = "m18 15-6-6-6 6",
        ["x"] = "M18 6 6 18|m6 6 12 12",
        ["dices"] = "M4 10h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2v-8a2 2 0 0 1 2 -2Z|m17.92 14 3.5-3.5a2.24 2.24 0 0 0 0-3l-5-4.92a2.24 2.24 0 0 0-3 0L10 6|M6 18h.01|M10 14h.01|M15 6h.01|M18 9h.01",
        ["layout-grid"] = "M4 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z|M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z|M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z|M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z",
        ["rotate-ccw"] = "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8|M3 3v5h5",
        ["triangle-alert"] = "m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3|M12 9v4|M12 17h.01",
        ["mouse-pointer-2"] = "M4.037 4.688a.495.495 0 0 1 .651-.651l16 6.5a.5.5 0 0 1-.063.947l-6.124 1.58a2 2 0 0 0-1.438 1.435l-1.579 6.126a.5.5 0 0 1-.947.063z",
        ["move"] = "M12 2v20|m15 19-3 3-3-3|m19 9 3 3-3 3|M2 12h20|m5 9-3 3 3 3|m9 5 3-3 3 3",
        ["scale-3d"] = "M5 7v11a1 1 0 0 0 1 1h11|M5.293 18.707 11 13|M17 19a2 2 0 1 0 4 0a2 2 0 1 0 -4 0|M3 5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0",
        ["shrink"] = "m15 15 6 6m-6-6v4.8m0-4.8h4.8|M9 19.8V15m0 0H4.2M9 15l-6 6|M15 4.2V9m0 0h4.8M15 9l6-6|M9 4.2V9m0 0H4.2M9 9 3 3",
        ["rotate-3d"] = "m15.194 13.707 3.814 1.86-1.86 3.814|M16.47214 7.52786 A 5 10 0 1 0 13 21.79796|M21.79796 11 A 10 5 0 1 0 19 15.57071",
        ["sun"] = "M8 12a4 4 0 1 0 8 0a4 4 0 1 0 -8 0Z|M12 2v2|M12 20v2|m4.93 4.93 1.41 1.41|m17.66 17.66 1.41 1.41|M2 12h2|M20 12h2|m6.34 17.66-1.41 1.41|m19.07 4.93-1.41 1.41",
        ["box"] = "M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z|m3.3 7 8.7 5 8.7-5|M12 22V12",
        ["grid-2x2"] = "M12 3v18|M3 12h18|M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z",
        ["wind"] = "M12.8 19.6A2 2 0 1 0 14 16H2|M17.5 8a2.5 2.5 0 1 1 2 4H2|M9.8 4.4A2 2 0 1 1 11 8H2",
        ["layers"] = "M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z|M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12|M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17",
        ["tree-pine"] = "m17 14 3 3.3a1 1 0 0 1-.7 1.7H4.7a1 1 0 0 1-.7-1.7L7 14h-.3a1 1 0 0 1-.7-1.7L9 9h-.2A1 1 0 0 1 8 7.3L12 3l4 4.3a1 1 0 0 1-.8 1.7H15l3 3.3a1 1 0 0 1-.7 1.7H17Z|M12 22v-3",
        ["camera"] = "M13.997 4a2 2 0 0 1 1.76 1.05l.486.9A2 2 0 0 0 18.003 7H20a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V9a2 2 0 0 1 2-2h1.997a2 2 0 0 0 1.759-1.048l.489-.904A2 2 0 0 1 10.004 4z|M9 13a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z",
        ["frame"] = "M22 6L2 6|M22 18L2 18|M6 2L6 22|M18 2L18 22",
        ["info"] = "M2 12a10 10 0 1 0 20 0a10 10 0 1 0 -20 0Z|M12 16v-4|M12 8h.01",
        ["person-standing"] = "M11 5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0Z|m9 20 3-6 3 6|m6 8 6 2 6-2|M12 10v4",
        ["brick-wall"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z|M12 9v6|M16 15v6|M16 3v6|M3 15h18|M3 9h18|M8 15v6|M8 3v6",
        ["maximize"] = "M8 3H5a2 2 0 0 0-2 2v3|M21 8V5a2 2 0 0 0-2-2h-3|M3 16v3a2 2 0 0 0 2 2h3|M16 21h3a2 2 0 0 0 2-2v-3",
        ["minimize"] = "M8 3v3a2 2 0 0 1-2 2H3|M21 8h-3a2 2 0 0 1-2-2V3|M3 16h3a2 2 0 0 1 2 2v3|M16 21v-3a2 2 0 0 1 2-2h3",
        ["sliders-horizontal"] = "M10 5H3|M12 19H3|M14 3v4|M16 17v4|M21 12h-9|M21 19h-5|M21 5h-7|M8 10v4|M8 12H3",
        ["shapes"] = "M8.3 10a.7.7 0 0 1-.626-1.079L11.4 3a.7.7 0 0 1 1.198-.043L16.3 8.9a.7.7 0 0 1-.572 1.1Z|M4 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z|M14 17.5a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0 -7 0Z",
        ["settings-2"] = "M14 17H5|M19 7h-9|M14 17a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z|M4 7a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z",
        ["copy"] = "M10 8h10a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2v-10a2 2 0 0 1 2 -2Z|M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2",
        ["trash-2"] = "M10 11v6|M14 11v6|M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6|M3 6h18|M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2",
        ["keyboard"] = "M10 8h.01|M12 12h.01|M14 8h.01|M16 12h.01|M18 8h.01|M6 8h.01|M7 16h10|M8 12h.01|M4 4h16a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-16a2 2 0 0 1 -2 -2v-12a2 2 0 0 1 2 -2Z",
        ["undo-2"] = "M9 14 4 9l5-5|M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11",
        ["redo-2"] = "m15 14 5-5-5-5|M20 9H9.5A5.5 5.5 0 0 0 4 14.5A5.5 5.5 0 0 0 9.5 20H13",
        ["folder-open"] = "m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2",
        ["file-down"] = "M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z|M14 2v5a1 1 0 0 0 1 1h5|M12 18v-6|m9 15 3 3 3-3",
        ["settings"] = "M9.671 4.136a2.34 2.34 0 0 1 4.659 0 2.34 2.34 0 0 0 3.319 1.915 2.34 2.34 0 0 1 2.33 4.033 2.34 2.34 0 0 0 0 3.831 2.34 2.34 0 0 1-2.33 4.033 2.34 2.34 0 0 0-3.319 1.915 2.34 2.34 0 0 1-4.659 0 2.34 2.34 0 0 0-3.32-1.915 2.34 2.34 0 0 1-2.33-4.033 2.34 2.34 0 0 0 0-3.831A2.34 2.34 0 0 1 6.35 6.051a2.34 2.34 0 0 0 3.319-1.915|M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0Z",
        ["clipboard-copy"] = "M9 2h6a1 1 0 0 1 1 1v2a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1v-2a1 1 0 0 1 1 -1Z|M8 4H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2|M16 4h2a2 2 0 0 1 2 2v4|M21 14H11|m15 10-4 4 4 4",
        ["house"] = "M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8|M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
        ["hash"] = "M4 9L20 9|M4 15L20 15|M10 3L8 21|M16 3L14 21",
        ["panel-left-close"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z|M9 3v18|m16 15-3-3 3-3",
        ["layout-panel-left"] = "M4 3h5a1 1 0 0 1 1 1v16a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-16a1 1 0 0 1 1 -1Z|M15 3h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z|M15 14h5a1 1 0 0 1 1 1v5a1 1 0 0 1 -1 1h-5a1 1 0 0 1 -1 -1v-5a1 1 0 0 1 1 -1Z",
        ["send"] = "M14.536 21.686a.5.5 0 0 0 .937-.024l6.5-19a.496.496 0 0 0-.635-.635l-19 6.5a.5.5 0 0 0-.024.937l7.93 3.18a2 2 0 0 1 1.112 1.11z|m21.854 2.147-10.94 10.939",
        ["folder"] = "M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z",
        ["grid-3x3"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2Z|M3 9h18|M3 15h18|M9 3v18|M15 3v18",
        ["arrow-left"] = "m12 19-7-7 7-7|M19 12H5",
        ["sprout"] = "M14 9.536V7a4 4 0 0 1 4-4h1.5a.5.5 0 0 1 .5.5V5a4 4 0 0 1-4 4 4 4 0 0 0-4 4c0 2 1 3 1 5a5 5 0 0 1-1 3|M4 9a5 5 0 0 1 8 4 5 5 0 0 1-8-4|M5 21h14",
        ["zap"] = "M15.914 4a1.5 1.5 0 00-2.474-1.561l-9 9A1.5 1.5 0 005.5 14h4.002a.5.5 0 01.471.666L8.086 20a1.5 1.5 0 002.475 1.56l9-9A1.5 1.5 0 0018.5 10h-3.997a.5.5 0 01-.472-.667z",
        ["axe"] = "m14 12-8.381 8.38a1 1 0 0 1-3.001-3L11 9|M15 15.5a.5.5 0 0 0 .5.5A6.5 6.5 0 0 0 22 9.5a.5.5 0 0 0-.5-.5h-1.672a2 2 0 0 1-1.414-.586l-5.062-5.062a1.205 1.205 0 0 0-1.704 0L9.352 5.648a1.205 1.205 0 0 0 0 1.704l5.062 5.062A2 2 0 0 1 15 13.828z",
        ["triangle-right"] = "M22 18a2 2 0 0 1-2 2H3c-1.1 0-1.3-.6-.4-1.3L20.4 4.3c.9-.7 1.6-.4 1.6.7Z",
    };

    private static readonly Dictionary<string, Geometry> Cache = new(StringComparer.Ordinal);

    /// <summary>Every icon name the app can draw (lucide names such as <c>trees</c> or <c>chevron-down</c>).</summary>
    public static IEnumerable<string> Names => Data.Keys;

    /// <summary>The icon's outline on the 24 by 24 grid; throws for a name that is not in the table.</summary>
    /// <param name="lucideName">The lucide name, for example <c>search</c>.</param>
    public static Geometry Get(string lucideName)
    {
        if (Cache.TryGetValue(lucideName, out Geometry? cached))
        {
            return cached;
        }

        if (!Data.TryGetValue(lucideName, out string? data))
        {
            throw new ArgumentException($"There is no icon called '{lucideName}'.", nameof(lucideName));
        }

        string[] parts = data.Split('|');
        Geometry made;
        if (parts.Length == 1)
        {
            made = Geometry.Parse(parts[0]);
        }
        else
        {
            var group = new GeometryGroup { FillRule = FillRule.NonZero };
            foreach (string part in parts)
            {
                group.Children.Add(Geometry.Parse(part));
            }

            made = group;
        }

        Cache[lucideName] = made;
        return made;
    }
}
