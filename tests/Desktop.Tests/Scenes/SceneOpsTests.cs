using System.Text.Json.Nodes;
using PropGenerator.Desktop.Scenes;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Desktop.Tests.Scenes;

/// <summary>
/// The scene code the app keeps for easy-props' kinds: a part and what hangs off it, our turn and size folded onto a
/// merged part, and the edits a work file keeps. The parts are made by hand; the merge is a stand-in for easy-props' own
/// (the engine's, in the app).
/// </summary>
public class SceneOpsTests
{
    private static SceneComponent Part(string id, string? parent, string? slot) =>
        new(id, "branch", "branch", 1, parent, Transform.Identity(), new ParameterDictionary(), null, 7, slot, false, null);

    // A stand-in for easy-props' mergeGenerated: each fresh part keeps its slot's kept move, turn and size; the parts
    // added by hand follow.
    private static IReadOnlyList<SceneComponent> Merge(IReadOnlyList<SceneComponent> kept, IReadOnlyList<SceneComponent> generated)
    {
        var bySlot = kept.Where(c => c.Slot is not null).ToDictionary(c => c.Slot!, StringComparer.Ordinal);
        return
        [
            .. generated.Select(fresh => bySlot.TryGetValue(fresh.Slot ?? string.Empty, out SceneComponent? previous)
                ? SceneOps.TurnedAndSized(previous.Moved is { } m ? fresh with { Transform = fresh.Transform with { Position = new Vec3d(m.X, m.Y, m.Z) }, Moved = m } : fresh, previous)
                : fresh),
            .. kept.Where(c => c.Slot is null),
        ];
    }

    [Fact]
    public void WithDescendantsTakesThePartAndAllThatHangsOffItInListOrder()
    {
        SceneComponent[] parts = [Part("c", "b", "3"), Part("a", null, "1"), Part("b", "a", "2"), Part("d", null, "4")];
        Assert.Equal(["c", "a", "b"], SceneOps.WithDescendants(parts, "a").Select(p => p.Id));
        Assert.Equal(["c", "b"], SceneOps.WithDescendants(parts, "b").Select(p => p.Id));
        Assert.Empty(SceneOps.WithDescendants(parts, "nope"));
    }

    [Fact]
    public void ATurnAndASizeGoOnTopOfTheMergedPart()
    {
        SceneComponent fresh = Part("a", null, "1") with
        {
            Transform = new Transform(new Vec3d(1, 2, 3), new Vec3d(0.3, 0, 0), new Vec3d(2, 2, 2)),
        };
        SceneComponent previous = fresh with { Turned = new Vec3d(0, Math.PI / 2, 0), Sized = 1.5 };
        SceneComponent kept = SceneOps.TurnedAndSized(fresh, previous);

        Assert.Equal(1.5, kept.Sized);
        Assert.Equal((3.0, 3.0, 3.0), (kept.Transform.Scale.X, kept.Transform.Scale.Y, kept.Transform.Scale.Z));
        Assert.Equal((1.0, 2.0, 3.0), (kept.Transform.Position.X, kept.Transform.Position.Y, kept.Transform.Position.Z));
        Quatd expected = new Quatd().SetFromAxisAngle(new Vec3d(0, 1, 0), Math.PI / 2)
            .Multiply(new Quatd().SetFromEuler(new Euler(0.3, 0, 0)));
        Quatd actual = new Quatd().SetFromEuler(new Euler(kept.Transform.Rotation.X, kept.Transform.Rotation.Y, kept.Transform.Rotation.Z));
        Assert.Equal(1, Math.Abs((expected.X * actual.X) + (expected.Y * actual.Y) + (expected.Z * actual.Z) + (expected.W * actual.W)), 12);

        // A part neither turned nor sized is the merged part as it is.
        Assert.Same(fresh, SceneOps.TurnedAndSized(fresh, fresh));
    }

    [Fact]
    public void EditsKeepOnlyWhatDiffersAndGiveThePartsBackThroughJson()
    {
        SceneComponent[] generated = [Part("a", null, "1"), Part("b", null, "2"), Part("b1", "b", "3"), Part("c", null, "4"), Part("d", null, "5")];
        SceneComponent moved = generated[0] with { Transform = generated[0].Transform with { Position = new Vec3d(0.25, 0, 0) }, Moved = new Vec3d(0.25, 0, 0) };
        SceneComponent sized = SceneOps.TurnedAndSized(generated[3], generated[3] with { Sized = 0.5 });
        SceneComponent copy = generated[4] with { Id = "man-branch-1", Slot = null, Edited = true };
        SceneComponent[] edited = [moved, sized, generated[4], copy];

        var edits = PartEdits.Of(generated, edited);
        Assert.Equal(["2", "3"], edits.Removed);
        Assert.Equal(["a", "c", "man-branch-1"], edits.Kept.Select(c => c.Id));

        JsonObject json = edits.ToJson();
        var back = PartEdits.FromJson(JsonNode.Parse(json.ToJsonString()));
        IReadOnlyList<SceneComponent> applied = back.Apply(generated, Merge);
        Assert.Equal(edited.Select(c => c.Id), applied.Select(c => c.Id));
        for (int i = 0; i < applied.Count; i++)
        {
            Assert.Equal(edited[i].Transform.Position.X, applied[i].Transform.Position.X, 12);
            Assert.Equal(edited[i].Transform.Scale.X, applied[i].Transform.Scale.X, 12);
            Assert.Equal(ComponentJson.Write(edited[i]).ToJsonString(), ComponentJson.Write(applied[i]).ToJsonString());
        }

        Assert.Same(generated, PartEdits.None.Apply(generated, Merge));
        Assert.Same(PartEdits.None, PartEdits.FromJson(null));
    }

    [Theory]
    [InlineData("position", null)]
    [InlineData("rotation", "[1, 2]")]
    [InlineData("scale", "\"big\"")]
    public void APartWithoutAWholeTransformIsNotReadable(string field, string? value)
    {
        JsonObject o = ComponentJson.Write(Part("a", null, "1"));
        var transform = (JsonObject)o["transform"]!;
        transform.Remove(field);
        if (value is not null)
        {
            transform[field] = JsonNode.Parse(value);
        }

        Assert.Throws<FormatException>(() => ComponentJson.Read(o));
        Assert.Throws<FormatException>(() => ComponentJson.Read(new JsonObject { ["id"] = "x" }));
    }
}
