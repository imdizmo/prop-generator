
namespace PropGenerator.Desktop.Tests.Support;

/// <summary>Helpers for the Blender bridge tests: the scratch inbox and outbox, a window on them, a mesh dropped in.</summary>
internal static class BridgeKit
{
    internal static string Inbox(Scratch s) => s.P("bridge", "inbox");

    internal static string Outbox(Scratch s) => s.P("bridge", "outbox");

    internal static MainWindow Open(Scratch s, FakeDialogs? dialogs = null) =>
        Ui.Open(new FakePainter(), dialogs ?? new FakeDialogs(), s, s.P("bridge"));

    // A GLB as Blender's add-on writes it: the mesh, with the face number of each triangle in its second UV set.
    internal static int[] DropMesh(Scratch s, string name, int firstId)
    {
        Directory.CreateDirectory(Inbox(s));
        int triangles = TileMesh.Square().Indices.Length / 3;
        int[] ids = [.. Enumerable.Range(0, triangles).Select(t => firstId + (t / 2))];
        BlenderGlb.Write(Path.Combine(Inbox(s), name + ".glb"), TileMesh.Square(), ids);
        return ids;
    }

    // Runs the menu command with this label (as choosing it from the top bar's menus does), without waiting for it.
    internal static void RunCommand(MainWindow w, string label) =>
        _ = w.Commands.SelectMany(c => (IEnumerable<Commands.Command>)[c, .. c.Children ?? []]).Single(c => c.Label == label).Run();
}
