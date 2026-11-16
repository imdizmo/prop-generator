using Avalonia.Controls;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>The panel's own controls, without template parts.</summary>
internal static class PanelWalk
{
    public static List<Control> GetLogicalDescendantsOfPanel(this SettingsPanel panel) =>
        [.. Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(panel).OfType<Control>()];
}
