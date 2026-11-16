// Adapted from easy-props (MIT, © 2026 AHS12).
using System.Globalization;

namespace PropGenerator.Desktop.Status;

/// <summary>What a piece is for and how many faces that allows (easy-props' budget targets). Going over warns and never blocks anything.</summary>
internal static class FaceBudget
{
    /// <summary>The budgets in the picker's order: id, name and the faces they allow.</summary>
    internal static IReadOnlyList<(string Id, string Label, int Faces)> Targets { get; } =
    [
        ("hero", "Hero", 30000),
        ("standard", "Standard", 14000),
        ("background", "Background", 5000),
        ("mobile", "Mobile", 2000),
    ];

    /// <summary>Gets the budget with this id (Standard for one that is not known).</summary>
    /// <param name="id">The id, such as "mobile".</param>
    /// <returns>The budget.</returns>
    internal static (string Id, string Label, int Faces) Find(string id) =>
        Targets.FirstOrDefault(t => t.Id == id) is { Id: not null } found ? found : Targets[1];

    /// <summary>Tells whether a piece with this many faces is over a budget.</summary>
    /// <param name="faces">The faces at full detail.</param>
    /// <param name="id">The budget's id.</param>
    /// <returns>True when there are more faces than the budget allows.</returns>
    internal static bool IsOver(int faces, string id) => faces > Find(id).Faces;

    /// <summary>The warning's tooltip for a piece that is over.</summary>
    /// <param name="faces">The faces at full detail.</param>
    /// <param name="id">The budget's id.</param>
    /// <returns>The sentence.</returns>
    internal static string Warning(int faces, string id)
    {
        (_, string label, int allowed) = Find(id);
        return string.Create(CultureInfo.InvariantCulture, $"Over budget. {faces:N0} faces is more than the {label} budget of {allowed:N0}. Lower the detail of the piece.");
    }
}
