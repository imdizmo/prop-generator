// Adapted from easy-props frontend/src/core/commands/commands.ts (the part commands and why each is greyed),
// frontend/src/store/editor-store.ts (the part tools, the copy's selection, Esc backing out a step at a time, Break) and
// frontend/src/panels/Details.tsx (Break offered on a heavy limb) (MIT, © 2026 AHS12).
using System.Numerics;
using Avalonia;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Desktop.View;
using PropGenerator.Engine;
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.Parts;

/// <summary>
/// Picking a part of the prop in the view and changing it: Move, Turn and Resize with a drag in the view (the tool
/// that is on), Copy, Remove, Restore and Break. Each change is one of easy-props' own scene operations run on its
/// engine (<see cref="SceneOp"/>), one undo step, and is saved in the work file, as the edits to the prop's parts
/// (<see cref="SettingsPanel.PartEditsKey"/>), or for a painted tree's limbs and clumps as the tree's own edits
/// (<see cref="SettingsPanel.TreeEditsKey"/>), as easy-props keeps them. When the engine fails or does not answer, the
/// prop is left as it was and the status line says so. The selected part is drawn lit up. Only the kinds made of
/// easy-props' components (<see cref="EngineRecipes.WithParts"/>) have parts.
/// </summary>
internal sealed class PartEditing : IPartDrag
{
    /// <summary>The Select tool: a click picks a part, a drag turns the camera.</summary>
    internal const string SelectTool = "select";

    /// <summary>The Move tool: a drag moves the selected part over the ground (up and down with Shift).</summary>
    internal const string MoveTool = "move";

    /// <summary>The Turn tool: a drag turns the selected part about the upright (about the camera's side with Shift).</summary>
    internal const string TurnTool = "turn";

    /// <summary>The Resize tool: a drag up makes the selected part larger, down smaller.</summary>
    internal const string ResizeTool = "resize";

    /// <summary>The smallest and largest a part may be made, as a share of the size its generator gives it.</summary>
    internal const double MinSize = PartEditsCheck.MinSize;

    /// <summary>The largest a part may be made.</summary>
    internal const double MaxSize = PartEditsCheck.MaxSize;

    // Parts that stand for the whole prop, so they cannot be moved or removed (easy-props' FIXED_PARTS).
    private static readonly string[] FixedParts = ["painted-bark", "rock-body"];

    // How the selected part is lit up: its colours mixed this far toward a warm highlight.
    private static readonly Vector3 Highlight = new(1f, 0.62f, 0.18f);
    private const float HighlightShare = 0.45f;

    private readonly MainWindow _w;
    private Settings? _dragBefore;
    private Settings? _dragNow;
    private Scene? _dragScene;
    private SceneComponent? _dragPart;
    private string? _dragLabel;
    private bool _dragFailed;

    /// <summary>Follows the window's prop.</summary>
    /// <param name="window">The window: its settings, view, rock and history.</param>
    internal PartEditing(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _w = window;
    }

    /// <summary>The selection or the tool changed: the toolbar and the menus follow.</summary>
    internal event Action? Changed;

    /// <summary>Gets the id of the selected part, or null when none is.</summary>
    internal string? Selected { get; private set; }

    /// <summary>Gets the tool that is on: <see cref="SelectTool"/>, <see cref="MoveTool"/>, <see cref="TurnTool"/> or <see cref="ResizeTool"/>.</summary>
    internal string Tool { get; private set; } = SelectTool;

    /// <summary>Gets the scenes of the prop's kind, when it is made of parts.</summary>
    internal EngineRecipe? Recipe => EngineRecipes.WithParts(_w.Files.Recipe);

    /// <summary>Gets the prop's parts as they are now, or null for a kind without parts (or while the engine cannot say).</summary>
    internal Scene? Scene
    {
        get
        {
            try
            {
                return Recipe?.SceneOf(_w.Panel.Current);
            }
            catch (Exception e) when (EnginePartScenes.IsEngineFailure(e))
            {
                return null;
            }
        }
    }

    /// <summary>Gets the selected part as it is now, or null when none is (or it is gone, undone or removed).</summary>
    internal SceneComponent? SelectedPart => Selected is null ? null : Scene?.Components.FirstOrDefault(c => c.Id == Selected);

    /// <summary>Why nothing can be picked, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNoParts() => Recipe is null ? "This kind of piece has no parts to pick" : null;

    /// <summary>Why the part commands cannot run with nothing picked, or null when a part is.</summary>
    /// <returns>The reason.</returns>
    internal string? NeedsPart() => WhyNoParts() ?? (SelectedPart is null ? "Select a part first" : null);

    /// <summary>Why Copy part cannot run, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotCopy() =>
        NeedsPart() ?? (Painted(SelectedPart!) ? "Parts of a painted tree cannot be copied" : null);

    /// <summary>Why Remove part cannot run, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotRemove()
    {
        if (NeedsPart() is string reason)
        {
            return reason;
        }

        SceneComponent part = SelectedPart!;

        // Ours: the last part is the whole prop too, and a prop of no parts shows nothing.
        return part.Type == "trunk" || FixedParts.Contains(part.GeneratorId) || Scene!.Components.Count == 1 ? "This part is the whole prop" : null;
    }

    /// <summary>Why Move part cannot run, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotMove()
    {
        if (NeedsPart() is string reason)
        {
            return reason;
        }

        SceneComponent part = SelectedPart!;
        if (part.GeneratorId == "painted-limb" || FixedParts.Contains(part.GeneratorId))
        {
            return "Reshape this part with its handles instead";
        }

        return part.Type == "trunk" ? "This part is the whole prop. Use Lean to tip it" : null;
    }

    /// <summary>
    /// Why Turn part cannot run, or null when it can. easy-props turns only a painted tree's branches and roots, through
    /// its plan; ours turns any part that moves, through its transform.
    /// </summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotTurn()
    {
        if (NeedsPart() is string reason)
        {
            return reason;
        }

        SceneComponent part = SelectedPart!;
        if (part.GeneratorId == "painted-limb")
        {
            return part.Parameters.TryGetValue("key", out object? key) && key is "t" ? "This part is the whole prop. Use Lean to tip it" : null;
        }

        // A painted tree's parts turn as easy-props turns them: only its branches and roots.
        return WhyNotMove() is null && !Painted(part) ? null : "Only branches and roots turn";
    }

    /// <summary>Why Resize part cannot run, or null when it can (as Turn part: ours resizes any part that moves).</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotResize()
    {
        if (NeedsPart() is string reason)
        {
            return reason;
        }

        SceneComponent part = SelectedPart!;
        if (part.GeneratorId is "painted-limb" or "painted-clump")
        {
            return part.Parameters.TryGetValue("key", out object? key) && key is "t" ? "This part is the whole prop. Use Height to size it" : null;
        }

        return WhyNotMove() is null ? null : "Only branches, roots and leaf clumps resize";
    }

    /// <summary>Why Break branch cannot run, or null when it can.</summary>
    /// <returns>The reason.</returns>
    internal string? WhyNotBreak()
    {
        if (NeedsPart() is string reason)
        {
            return reason;
        }

        EnginePartFacts facts;
        try
        {
            facts = Recipe!.Facts(_w.Panel.Current, SelectedPart!.Id);
        }
        catch (Exception e) when (EnginePartScenes.IsEngineFailure(e))
        {
            return "easy-props' engine could not say whether this part breaks";
        }

        // As easy-props' Details offers Break: a heavy limb that is not broken yet (canBreakPart).
        return facts.CanBreak ? null : facts.Heavy ? "This limb is broken already" : "Only a tree's heavy limbs break";
    }

    /// <summary>Breaks the selected heavy limb partway, leaving a jagged stub (easy-props' breakLimb). One undo step.</summary>
    internal void Break()
    {
        if (WhyNotBreak() is not null || Recipe is not { } recipe || SelectedPart is not { } part)
        {
            return;
        }

        if (Commit(recipe, new SceneOp.Break(part.Id), "Break branch"))
        {
            _w.Say("Broke the branch");
        }
    }

    /// <summary>Picks the part under a point of the view, or lets go of the selection when there is none there.</summary>
    /// <param name="at">The point, in the view's own coordinates.</param>
    internal void Pick(Point at)
    {
        if (Recipe is null || _w.Rocks.Mesh is not { Parts: { } parts } mesh || _w.View.RayAt(at) is not var (from, direction))
        {
            return;
        }

        // What the pointer lands on: the nearest drawn part, or a never-drawn proxy in front of it (a painted tree's
        // limb, just outside its bark).
        (int triangle, float distance) = Nearest(mesh, from, direction);
        string? id = triangle < 0 ? null : parts.FirstOrDefault(p => triangle >= p.FirstTriangle && triangle < p.FirstTriangle + p.Triangles)?.Id;
        if (mesh.Proxies is { Parts: { } proxies } hidden)
        {
            (int hit, float near) = Nearest(hidden, from, direction);
            if (hit >= 0 && near < distance)
            {
                id = proxies.FirstOrDefault(p => hit >= p.FirstTriangle && hit < p.FirstTriangle + p.Triangles)?.Id;
            }
        }

        Select(id);
    }

    /// <summary>Selects a part by its id (null to let go), drawing it lit up.</summary>
    /// <param name="id">The part's id, or null.</param>
    internal void Select(string? id)
    {
        if (id == Selected)
        {
            return;
        }

        Selected = id;
        if (id is null)
        {
            Tool = SelectTool;
        }

        _w.Rocks.Redecorate();
        Changed?.Invoke();
    }

    /// <summary>Turns a part tool on, or off when it is on already (easy-props' setMoveTool and the others).</summary>
    /// <param name="tool">The tool.</param>
    internal void Toggle(string tool)
    {
        Tool = Tool == tool ? SelectTool : tool;
        Changed?.Invoke();
    }

    /// <summary>Esc: puts the tool down first, then lets go of the selection, a step at a time.</summary>
    internal void Cancel()
    {
        if (Tool != SelectTool)
        {
            Tool = SelectTool;
            Changed?.Invoke();
            return;
        }

        Select(null);
    }

    /// <summary>The part tools put down and nothing selected: a new prop was started or opened.</summary>
    internal void Reset()
    {
        Tool = SelectTool;
        Selected = null;
        Changed?.Invoke();
    }

    /// <summary>Copies the selected part and what hangs off it, and selects the copy. One undo step.</summary>
    internal void Copy()
    {
        if (WhyNotCopy() is not null || Recipe is not { } recipe || Scene is not { } scene || SelectedPart is not { } source)
        {
            return;
        }

        // Upstream names the copies (man-<type>-<n>, as its editor does); the first new id is the copy of the part.
        HashSet<string> before = [.. scene.Components.Select(c => c.Id)];
        if (Run(recipe, new SceneOp.Copy(source.Id)) is { } copied && Commit(recipe, copied, "Copy part"))
        {
            if (copied.Components.FirstOrDefault(c => !before.Contains(c.Id)) is { } copy)
            {
                Select(copy.Id);
                _w.Say("Copied part");
            }
        }
    }

    /// <summary>Removes the selected part and what hangs off it. One undo step.</summary>
    internal void Remove()
    {
        if (WhyNotRemove() is not null || Recipe is not { } recipe || SelectedPart is not { } part)
        {
            return;
        }

        if (Commit(recipe, new SceneOp.Remove(part.Id), "Remove part"))
        {
            Select(null);
            _w.Say($"Removed {part.Type}. Undo brings it back.");
        }
    }

    /// <summary>Puts the selected part's own settings back to its generator's defaults. One undo step.</summary>
    internal void Restore()
    {
        if (NeedsPart() is not null || Recipe is not { } recipe || SelectedPart is not { } part)
        {
            return;
        }

        _ = Commit(recipe, new SceneOp.Restore(part.Id), "Restore part");
    }

    /// <summary>Moves the selected part by an offset, in metres, Z-up (as the view's axes). One undo step.</summary>
    /// <param name="by">The offset.</param>
    internal void MoveBy(Vector3 by)
    {
        if (WhyNotMove() is not null || Recipe is not { } recipe || SelectedPart is not { } part)
        {
            return;
        }

        _ = Commit(recipe, Moved(part, by), "Move part");
    }

    /// <summary>Turns the selected part about an axis through its own origin, Z-up (as the view's axes). One undo step.</summary>
    /// <param name="axis">The axis.</param>
    /// <param name="degrees">How far.</param>
    internal void TurnBy(Vector3 axis, double degrees)
    {
        if (WhyNotTurn() is not null || Recipe is not { } recipe || SelectedPart is not { } part)
        {
            return;
        }

        _ = Commit(recipe, Turned(part, YUp(axis), degrees * Math.PI / 180), "Turn part");
    }

    /// <summary>Makes the selected part a number of times larger (smaller below 1), within its limits. One undo step.</summary>
    /// <param name="factor">How many times larger.</param>
    internal void ResizeBy(double factor)
    {
        if (WhyNotResize() is not null || Recipe is not { } recipe || SelectedPart is not { } part || factor <= 0)
        {
            return;
        }

        if (Resized(part, factor) is { } op)
        {
            _ = Commit(recipe, op, "Resize part");
        }
    }

    /// <summary>The prop's mesh with the selected part lit up, for the view; the mesh itself when no part of it is selected.</summary>
    /// <param name="mesh">The mesh as built.</param>
    /// <returns>The mesh to draw.</returns>
    internal ColouredMesh Decorate(ColouredMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (Selected is not null && mesh.Proxies is { Parts: { } proxies } hidden && proxies.FirstOrDefault(p => p.Id == Selected) is { Triangles: > 0 } proxy)
        {
            return WithProxy(mesh, hidden, proxy);
        }

        if (Selected is null || mesh.Parts?.FirstOrDefault(p => p.Id == Selected) is not { } part || part.Triangles == 0)
        {
            return mesh;
        }

        float[] colours = (float[])mesh.Colours.Clone();
        var lit = new HashSet<int>();
        for (int i = part.FirstTriangle * 3; i < (part.FirstTriangle + part.Triangles) * 3; i++)
        {
            int v = mesh.Indices[i];
            if (lit.Add(v))
            {
                for (int c = 0; c < 3; c++)
                {
                    colours[(v * 3) + c] += (Highlight[c] - colours[(v * 3) + c]) * HighlightShare;
                }
            }
        }

        return mesh with { Colours = colours };
    }

    /// <inheritdoc/>
    public bool Begin(Point at)
    {
        string? why = Tool switch
        {
            MoveTool => WhyNotMove(),
            TurnTool => WhyNotTurn(),
            ResizeTool => WhyNotResize(),
            _ => "The Select tool is on",
        };
        if (why is not null || Recipe is null || Scene is not { } scene || SelectedPart is not { } part)
        {
            return false;
        }

        _dragBefore = _dragNow = _w.Panel.Current;
        _dragFailed = false;
        _dragScene = scene;
        _dragPart = part;
        _dragLabel = Tool switch
        {
            MoveTool => "Move part",
            TurnTool => "Turn part",
            _ => "Resize part",
        };
        return true;
    }

    /// <inheritdoc/>
    public void Move(Point at, Point from, bool shift)
    {
        if (_dragScene is null || _dragPart is not { } part || _dragBefore is not { } before || Recipe is not { } recipe || _dragFailed)
        {
            return;
        }

        // Each tick is one op on the part as it was when the drag began, by how far the pointer has gone.
        double dx = at.X - from.X, dy = at.Y - from.Y;
        SceneOp? op;
        switch (Tool)
        {
            case MoveTool:
                OrbitCamera orbit = _w.View.Orbit;
                float perPixel = (float)(2 * orbit.Distance * Math.Tan(OrbitCamera.FieldOfView / 2) / _w.View.ViewHeight);
                Vector3 back = orbit.Back;
                Vector3 ahead = new Vector3(-back.X, -back.Y, 0).LengthSquared() > 1e-8f ? Vector3.Normalize(new Vector3(-back.X, -back.Y, 0)) : Vector3.UnitY;
                var side = new Vector3(ahead.Y, -ahead.X, 0);
                Vector3 by = shift
                    ? new Vector3(0, 0, (float)-dy * perPixel)
                    : (side * (float)dx * perPixel) + (ahead * (float)-dy * perPixel);
                op = Moved(part, by);
                break;
            case TurnTool:
                Vector3 axis = shift ? Side(_w.View.Orbit) : Vector3.UnitZ;
                double angle = (shift ? dy : dx) * 0.01;
                op = Turned(part, YUp(axis), angle);
                break;
            default:
                op = Resized(part, Math.Exp(-dy * 0.005));
                break;
        }

        if (op is null)
        {
            _dragNow = before;
            _w.Files.Preview(before);
            return;
        }

        // A failure leaves the prop as it was before the drag and says so once; the rest of the drag does nothing.
        Settings? now = SettingsAfter(recipe, before, op, out bool failed);
        _dragFailed = failed;
        if (now is not null)
        {
            _dragNow = now;
            _w.Files.Preview(now);
        }
    }

    /// <inheritdoc/>
    public void End()
    {
        if (_dragBefore is { } before && _dragNow is { } now && _dragLabel is { } label)
        {
            _w.Files.ChangeFrom(before, now, label);
        }

        _dragBefore = _dragNow = null;
        _dragFailed = false;
        _dragScene = null;
        _dragPart = null;
        _dragLabel = null;
    }

    // The mesh with a selected proxy drawn over it in the highlight: a never-drawn part (a painted tree's limb) shows
    // as a sleeve just outside its bark, as easy-props outlines it.
    private static ColouredMesh WithProxy(ColouredMesh mesh, ColouredMesh hidden, MeshPart proxy)
    {
        var positions = new List<float>(mesh.Positions);
        var normals = new List<float>(mesh.Normals);
        var colours = new List<float>(mesh.Colours);
        var indices = new List<int>(mesh.Indices);
        var map = new Dictionary<int, int>();
        for (int i = proxy.FirstTriangle * 3; i < (proxy.FirstTriangle + proxy.Triangles) * 3; i++)
        {
            int v = hidden.Indices[i];
            if (!map.TryGetValue(v, out int at))
            {
                at = positions.Count / 3;
                map.Add(v, at);
                for (int c = 0; c < 3; c++)
                {
                    positions.Add(hidden.Positions[(v * 3) + c]);
                    normals.Add(hidden.Normals[(v * 3) + c]);
                    colours.Add(Highlight[c]);
                }
            }

            indices.Add(at);
        }

        int added = map.Count;
        return mesh with
        {
            Positions = [.. positions],
            Normals = [.. normals],
            Colours = [.. colours],
            Indices = [.. indices],
            TexCoords = mesh.TexCoords is null ? null : [.. mesh.TexCoords, .. new float[added * 3]],
            Flutter = mesh.Flutter is null ? null : [.. mesh.Flutter, .. new float[added]],
            Roughness = mesh.Roughness is null ? null : [.. mesh.Roughness, .. Enumerable.Repeat(1f, added)],
        };
    }

    // The nearest triangle the ray hits (Möller–Trumbore, both faces) and how far along the ray, or -1.
    private static (int Triangle, float Distance) Nearest(ColouredMesh mesh, Vector3 from, Vector3 direction)
    {
        float best = float.MaxValue;
        int hit = -1;
        float[] p = mesh.Positions;
        for (int t = 0; t < mesh.Indices.Length / 3; t++)
        {
            int a = mesh.Indices[t * 3] * 3, b = mesh.Indices[(t * 3) + 1] * 3, c = mesh.Indices[(t * 3) + 2] * 3;
            var v0 = new Vector3(p[a], p[a + 1], p[a + 2]);
            Vector3 e1 = new Vector3(p[b], p[b + 1], p[b + 2]) - v0;
            Vector3 e2 = new Vector3(p[c], p[c + 1], p[c + 2]) - v0;
            var h = Vector3.Cross(direction, e2);
            float det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-12f)
            {
                continue;
            }

            float inv = 1 / det;
            Vector3 s = from - v0;
            float u = inv * Vector3.Dot(s, h);
            if (u is < 0 or > 1)
            {
                continue;
            }

            var q = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(direction, q);
            if (v < 0 || u + v > 1)
            {
                continue;
            }

            float distance = inv * Vector3.Dot(e2, q);
            if (distance > 0 && distance < best)
            {
                best = distance;
                hit = t;
            }
        }

        return (hit, best);
    }

    // A part moved by an offset in the view's Z-up axes: upstream's setComponentPosition (a painted part through its
    // tree's edits).
    private static SceneOp.Position Moved(SceneComponent part, Vector3 by)
    {
        Vec3d at = part.Transform.Position;
        Vec3d offset = YUp(by);
        return new SceneOp.Position(part.Id, new Vec3d(at.X + offset.X, at.Y + offset.Y, at.Z + offset.Z));
    }

    // A part turned about an axis through its own origin (Y-up). A painted part turns through its tree's edits, about
    // its base: its turn turned further about the axis (easy-props' turnedAbout), patched in as easy-props' Turn tool
    // does. Ours turns any other part through its transform, the turn kept beside it (Turned) so a rebuild keeps it.
    private static SceneOp Turned(SceneComponent part, Vec3d axis, double radians)
    {
        if (Painted(part))
        {
            return new SceneOp.Part(part.Id, "patch", new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["turn"] = PartTurning.TurnedAbout(PartTurning.TurnOf(part), axis, radians).Cast<object>().ToList(),
            });
        }

        Quatd by = new Quatd().SetFromAxisAngle(axis.Clone().Normalize(), radians);
        Quatd turned = new Quatd(by.X, by.Y, by.Z, by.W).Multiply(QuaternionOf(part.Turned ?? new Vec3d(0, 0, 0)));
        Quatd rotation = new Quatd(by.X, by.Y, by.Z, by.W).Multiply(QuaternionOf(part.Transform.Rotation));
        return new SceneOp.Transform(part.Id, part.Transform with { Rotation = EulerOf(rotation) }, Turned: EulerOf(turned));
    }

    // A part resized by a factor, its size kept within the limits; null when that leaves it as it is. A painted tree's
    // part takes its Size within its control's limits, to a hundredth, as easy-props' Resize tool sets it (resizedBy).
    // Ours resizes any other part through its transform, the size kept beside it (Sized).
    private static SceneOp? Resized(SceneComponent part, double factor)
    {
        if (PartResizing.CanResize(part))
        {
            double start = PartResizing.SizeOf(part);
            double sized = PartResizing.ResizedBy(start, factor, PartResizing.SizeLimits(part.GeneratorId), snap: false);
            return sized == start
                ? null
                : new SceneOp.Part(part.Id, "patch", new Dictionary<string, object>(StringComparer.Ordinal) { ["sizeScale"] = sized });
        }

        double size = part.Sized ?? 1;
        double next = Math.Clamp(size * factor, MinSize, MaxSize);
        return next == size
            ? null
            : new SceneOp.Transform(part.Id, part.Transform with { Scale = part.Transform.Scale.Clone().MultiplyScalar(next / size) }, Sized: next);
    }

    private static Quatd QuaternionOf(Vec3d euler) => new Quatd().SetFromEuler(new Euler(euler.X, euler.Y, euler.Z));

    private static Vec3d EulerOf(Quatd q)
    {
        Euler e = new Euler().SetFromQuaternion(q);
        return new Vec3d(e.X, e.Y, e.Z);
    }

    // The camera's side, flat on the ground, Z-up.
    private static Vector3 Side(OrbitCamera orbit)
    {
        Vector3 back = orbit.Back;
        var ahead = new Vector3(-back.X, -back.Y, 0);
        if (ahead.LengthSquared() < 1e-8f)
        {
            return Vector3.UnitX;
        }

        ahead = Vector3.Normalize(ahead);
        return new Vector3(ahead.Y, -ahead.X, 0);
    }

    // The view's Z-up axes in the prop's own Y-up space: (x, y, z) becomes (x, z, -y).
    private static Vec3d YUp(Vector3 v) => new(v.X, v.Z, -v.Y);

    // A painted tree's part, as upstream's isPaintedPart says.
    private static bool Painted(SceneComponent part) => part.GeneratorId.StartsWith("painted-", StringComparison.Ordinal);

    // Runs an op on the prop as it is now: the scene after it, or null when upstream refused or the engine failed (the
    // status line says which).
    private Scene? Run(EngineRecipe recipe, SceneOp op)
    {
        try
        {
            if (recipe.Apply(_w.Panel.Current, op) is { } scene)
            {
                return scene;
            }

            _w.Say("easy-props left the part as it was: it cannot be changed that way");
            return null;
        }
        catch (Exception e) when (EnginePartScenes.IsEngineFailure(e))
        {
            _w.Say($"The part was left as it was: easy-props' engine did not answer ({e.Message})", error: true);
            return null;
        }
    }

    // The settings an op on these settings makes, or null when upstream refused or the engine failed (said once).
    private Settings? SettingsAfter(EngineRecipe recipe, Settings before, SceneOp op, out bool failed)
    {
        failed = false;
        try
        {
            return recipe.Apply(before, op) is { } scene ? recipe.WithScene(before, scene) : null;
        }
        catch (Exception e) when (EnginePartScenes.IsEngineFailure(e))
        {
            failed = true;
            _w.Say($"The part was left as it was: easy-props' engine did not answer ({e.Message})", error: true);
            return null;
        }
    }

    // One undo step of an op's result; false when nothing was changed.
    private bool Commit(EngineRecipe recipe, SceneOp op, string label) =>
        Run(recipe, op) is { } scene && Commit(recipe, scene, label);

    private bool Commit(EngineRecipe recipe, Scene changed, string label)
    {
        try
        {
            _w.Files.ChangeAll(recipe.WithScene(_w.Panel.Current, changed), label);
            return true;
        }
        catch (Exception e) when (EnginePartScenes.IsEngineFailure(e))
        {
            _w.Say($"The part was left as it was: easy-props' engine did not answer ({e.Message})", error: true);
            return false;
        }
    }
}
