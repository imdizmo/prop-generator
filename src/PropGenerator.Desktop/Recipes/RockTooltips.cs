// Adapted from easy-props (MIT, © 2026 AHS12).
namespace PropGenerator.Desktop.Recipes;

/// <summary>The panel's hover text for each rock setting, in this app's own words.</summary>
internal static class RockTooltips
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["kind"] = "The overall form the chunks are arranged into: a rounded boulder, a low outcrop that steps along the ground, a tall pillar or a wall-like cliff. The starting points use boulder for the stones and boulders, outcrop for the grassy outcrop and low ledge, pillar for the standing stone and sea stack, and cliff for the overhang, cliff face and mesa.",
        ["width"] = "How far the rock spreads from left to right, in metres, from 0.2 to 40. Stones are about 0.7 to 1.6 m wide, boulders 2.6 to 3.4 m, outcrops 6 to 8 m and cliffs 14 to 16 m.",
        ["height"] = "How tall the rock stands above the ground, in metres, from 0.1 to 40. A stepping stone is 0.14 m, a field stone 0.5 m, a boulder 2 m, a standing stone 3 m and a sea stack 11 m.",
        ["depth"] = "How far the rock runs from front to back, in metres, from 0.2 to 40. A stone is 0.5 to 0.8 m deep, a boulder 2.4 to 3 m, a low ledge 3 m and a red mesa 11 m.",
        ["chunks"] = "How many separate pieces of stone the rock is built from, a whole number from 1 to 40. More chunks give a lumpier, more broken outline. A single stone has 1, a boulder 2 to 4, an outcrop 5 to 7 and a cliff 14 to 18.",
        ["faces"] = "How strongly each chunk is cut into flat faces, from 0 (left rounded) to 1 (fully faceted). The starting points sit between 0.35 for a flat slab and 0.8 for a sharp boulder; 0.6 is a good middle.",
        ["cuts"] = "How deep the cuts go that make those faces, from 0 (corners only grazed) to 1 (deep, sharp cuts). River stones use 0.2, most rocks 0.65 and the sharp boulder 1.",
        ["edges"] = "How soft the edges between faces are, from 0 (hard, crisp edges) to 1 (rounded edges that catch the light). Most starting points use 0.7, the sharp boulder 0.15 and the river stone and round boulder 1.",
        ["bend"] = "How far the flat faces bow outwards, from 0 (ruler flat) to 1 (clearly curved), so the stone looks weathered and carved instead of machined. Every starting point uses 0.5.",
        ["moss"] = "How much green moss grows, from 0 (bare stone) to 1 (heavily capped). It grows on the faces that look up, or on one side when Moss grows is set to the shady side. The sharp boulder, river stone and red mesa have none, a field stone 0.3, a boulder 0.5 to 0.7 and the grassy outcrop 0.85.",
        ["variation"] = "How much the colour of one face differs from the next, from 0 (even) to 1 (patchy). Most starting points use 0.5; the stepping stone uses 0.3 for a plainer slab.",
        ["layers"] = "How much the rock is built up in bands of stone laid one on another, from 0 (none) to 1 (strong courses). A cliff stacks in courses with a ledge on top, and every rock shows faint stripes. The river stone has none, most starting points 0.4 and the red mesa 0.9.",
        ["overhang"] = "How far an outcrop or cliff leans out over its feet on the side that faces you, from 0 (straight up) to 1 (a deep overhang). It is shown only for an outcrop or a cliff. Most starting points use 0.3 and the overhang 1.",
        ["dents"] = "How deep the soft hollows worn into some of the larger faces are, from 0 (none) to 1 (deeply worn). The river stone and sharp boulder have none, most starting points 0.3 and the stepping stone 0.5.",
        ["rubble"] = "How many small stones of the same rock lie round its foot, from 0 (none) to 1 (a heap). Most starting points have none; the mossy rubble uses 0.65 and the riverbank stones 0.9.",
        ["rounded"] = "How the light falls on the rock, from 0 (flat on each face, so every cut shows) to 1 (rolling over the whole stone like a pebble). Only the shading changes: the faces and outline stay as they are. Most starting points use 0.5 and the round boulder 0.9.",
        ["blend"] = "How far the chunks run together into one stone, from 0 (separate chunks with clean cuts between them) to 1 (one stone with soft creases where they meet). Most starting points use 0.6, the sharp boulder 0.2 and the round boulder 1.",
        ["grain"] = "How much fine detail the stone shows up close, from 0 (smooth colour) to 1 (strong speckle, small pits and hairline cracks). It is painted into the picture, so it costs no faces. Every starting point uses 0.6.",
        ["lichen"] = "How many small pale patches of lichen sit on the bare stone, from 0 (none) to 1 (thickly spotted). Like the grain, it is painted in and seen up close. Every starting point uses 0.3.",
        ["cracks"] = "How badly the stone is split, from 0 (whole) to 1 (three open cracks running through it); low values give one fine crack. The cracked slab and cracked boulder use 0.8.",
        ["crumble"] = "How much of one side has broken away, from 0 (whole) to 1 (a big bite gone), with the broken pieces lying at its foot. The cracked boulder uses 0.5.",
        ["palette"] = "The stone's colour range from shadow to highlight: greystone, lavender, slate, granite, sandstone, redrock, basalt or frost, or My own colours to pick the four yourself. Greystone is the default, slate suits the river stone, low ledge and sea stack, granite the sharp boulder, basalt the snowy boulder and redrock the red mesa.",
        ["build"] = "Normal or Low-poly. Normal has soft, carved edges and bent faces; Low-poly is the same rock as one shell of hard, flat faces, each one colour, with nothing hidden inside, so it costs fewer faces. Switch it with the buttons over the view or the keys 1 and 2. Layers, Dents, Edge softness, Rounded light, Blend, Bend, Grain and Lichen do nothing in Low-poly and are hidden.",
        ["stoneShadow"] = "My own colours: the stone's darkest colour, used in the seams between chunks and low down near the ground. It starts as the shadow of the palette that was showing when you picked My own colours.",
        ["stoneDark"] = "My own colours: the stone's second, darker middle colour, for faces turned away from the sky. It starts as the palette's dark colour when you pick My own colours.",
        ["stoneLight"] = "My own colours: the stone's lighter middle colour, for faces that look up and the upper part of the rock. It starts as the palette's light colour when you pick My own colours.",
        ["stoneHighlight"] = "My own colours: the stone's brightest colour, on the edges and the faces that look straight up. It starts as the palette's highlight when you pick My own colours.",
        ["seed"] = "A whole number from 0 to 4294967295 that picks which random rock you get from the other settings. The same seed and settings always give the same rock; change it to roll a different one. The starting points use seeds from 401 to 434.",
    };

    /// <summary>The hover text for one rock setting.</summary>
    /// <param name="key">The setting name, such as "chunks".</param>
    /// <returns>A few sentences ending with a full stop: what the setting does, its unit and range, typical values.</returns>
    internal static string For(string key) => Texts.TryGetValue(key, out string? text)
        ? text
        : throw new ArgumentException($"No rock tooltip for '{key}'.", nameof(key));

    /// <summary>The hover text for one rock setting, or null when the app has none of its own (a setting upstream added later).</summary>
    /// <param name="key">The setting name, such as "chunks".</param>
    /// <returns>The text, or null to use upstream's help.</returns>
    internal static string? TryFor(string key) => Texts.GetValueOrDefault(key);
}
