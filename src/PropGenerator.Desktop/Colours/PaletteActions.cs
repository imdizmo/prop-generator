using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.Colours;

/// <summary>
/// Saved palettes for each kind, as easy-props' palette menu: the own-colour rows of a choice on My own colours are saved
/// under a name (the same name again updates it), offered to every kind with the same colours, and used as one undo step
/// that also switches the choice to My own colours.
/// </summary>
/// <param name="w">The window.</param>
internal sealed class PaletteActions(MainWindow w)
{
    /// <summary>Gets the own-colour groups of the kind open now.</summary>
    internal IReadOnlyList<OwnColourGroup> Groups => MeshRecipes.Find(w.Files.Recipe)?.OwnColourGroups ?? [];

    /// <summary>Says why the colours cannot be saved as a palette now, or null when they can.</summary>
    /// <returns>The reason, or null.</returns>
    internal string? SaveUnavailable() =>
        Groups.Count == 0 ? "This kind has no own colours"
        : OnOwn().Any() ? null : "Choose My own colours first";

    /// <summary>The palettes that fit the kind open now, each with the group it fills.</summary>
    /// <returns>The palettes.</returns>
    internal IReadOnlyList<(SavedPalette Palette, OwnColourGroup Group)> Offered() =>
        [.. w.Colours.Palettes
            .Select(p => (Palette: p, Group: Groups.FirstOrDefault(g => g.Set == p.Set)))
            .Where(x => x.Group is not null)
            .Select(x => (x.Palette, x.Group!))];

    /// <summary>Asks for a name, then saves the colours of every choice on My own colours under it.</summary>
    /// <returns>Done when saved or cancelled.</returns>
    internal async Task SaveAsync()
    {
        string? typed = await w.Files.Dialogs.AskTextAsync("Save these colours as a palette", "Name, like Our forest game", string.Empty);
        string name = (typed ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (name.Length > Props.YourProps.MaxName)
        {
            name = name[..Props.YourProps.MaxName];
        }

        bool added = false;
        string shown = name;
        foreach (OwnColourGroup g in OnOwn())
        {
            string[] colours = [.. g.Keys.Select(k => Hex(w.Panel.Current.GetColour(k)))];
            SavedPalette? same = w.Colours.Palettes.FirstOrDefault(p => p.Set == g.Set && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            shown = same?.Name ?? name;
            added |= w.Colours.SavePalette(g.Set, name, colours);
        }

        w.Say(added ? $"Saved \"{shown}\"" : $"Updated \"{shown}\"");
    }

    /// <summary>Uses a palette on the prop open now: its choice on My own colours with the palette's colours, one undo step.</summary>
    /// <param name="p">The palette.</param>
    internal void Apply(SavedPalette p)
    {
        if (Groups.FirstOrDefault(g => g.Set == p.Set) is not { } g)
        {
            return;
        }

        Settings target = w.Panel.Current.With(g.ChoiceKey, "custom");
        for (int i = 0; i < g.Keys.Count && i < p.Colours.Count; i++)
        {
            target = target.With(g.Keys[i], Rgb.FromHex(p.Colours[i]));
        }

        w.Files.ChangeAll(target, $"Use palette {p.Name}");
        w.Say($"Using \"{p.Name}\"");
    }

    /// <summary>Removes a palette; props using its colours keep them.</summary>
    /// <param name="p">The palette.</param>
    internal void Remove(SavedPalette p)
    {
        w.Colours.RemovePalette(p.Id);
        w.Say($"Removed \"{p.Name}\". Props using it keep their colours.");
    }

    private static string Hex(Rgb c)
    {
        static int B(float x) => (int)Math.Round(Math.Clamp(x, 0, 1) * 255);
        return $"#{B(c.R):x2}{B(c.G):x2}{B(c.B):x2}";
    }

    private IEnumerable<OwnColourGroup> OnOwn() =>
        Groups.Where(g => w.Panel.Current.Values.TryGetValue(g.ChoiceKey, out System.Text.Json.JsonElement v)
            && v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() == "custom");
}
