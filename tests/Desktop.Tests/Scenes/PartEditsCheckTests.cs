using PropGenerator.Desktop.Scenes;
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Desktop.Tests.Scenes;

/// <summary>
/// Part edits read from a work file are checked before use: a file is shared, so it is not trusted. The parts are made
/// by hand here; the caller's parameter rule (the engine's, in the app) is a stand-in that keeps them as they are.
/// </summary>
public class PartEditsCheckTests
{
    private static readonly IReadOnlyList<SceneComponent> Generated =
    [
        Part("cmp-bush-core", "bush-core", "core"),
        Part("cmp-bush-leaves", "bush-leaves", "leaves"),
    ];

    private static SceneComponent Leaves => Generated.Single(c => c.Id == "cmp-bush-leaves");

    private static SceneComponent Part(string id, string generatorId, string slot)
    {
        var parameters = new ParameterDictionary();
        parameters.Set("height", 1.0);
        return new SceneComponent(
            id, "bush", generatorId, 1, null,
            new Transform(new Vec3d(0, 0, 0), new Vec3d(0, 0, 0), new Vec3d(1, 1, 1)),
            parameters, null, 1, slot, false, null);
    }

    private static SceneComponent Copy(int n) => Leaves with { Id = $"man-bush-leaves-{n}", Slot = null, Edited = true };

    private static ParameterDictionary AsTheyAre(SceneComponent part, IReadOnlyList<SceneComponent> generated) => part.Parameters;

    private static PartEdits Check(PartEdits edits) => PartEditsCheck.Validate(edits, Generated, AsTheyAre);

    private static string Problem(PartEdits edits) => Assert.Throws<FormatException>(() => Check(edits)).Message;

    [Fact]
    public void GoodEditsPass()
    {
        var edits = new PartEdits([Leaves with { Moved = new Vec3d(1, 0, 0), Sized = 2 }, Copy(1)], ["core"]);
        PartEdits checkedEdits = Check(edits);
        Assert.Equal(edits.Kept.Select(c => c.Id), checkedEdits.Kept.Select(c => c.Id));
        Assert.Equal(edits.Removed, checkedEdits.Removed);
    }

    [Fact]
    public void TooManyCopiesAreRefused()
    {
        var edits = new PartEdits([.. Enumerable.Range(0, PartEditsCheck.MaxCopies + 1).Select(Copy)], []);
        Assert.Contains("copies", Problem(edits), StringComparison.Ordinal);
    }

    [Fact]
    public void ASizeOutsideTheResizeLimitsIsRefused()
    {
        Assert.Contains("size", Problem(new PartEdits([Leaves with { Sized = 1e9 }], [])), StringComparison.Ordinal);
        Assert.Contains("size", Problem(new PartEdits([Leaves with { Sized = PartEditsCheck.MinSize / 2 }], [])), StringComparison.Ordinal);
    }

    [Fact]
    public void ATransformThatIsNotFiniteOrTooFarIsRefused()
    {
        Transform t = Leaves.Transform;
        Assert.Contains("not a finite", Problem(new PartEdits([Copy(1) with { Transform = t with { Position = new Vec3d(double.NaN, 0, 0) } }], [])), StringComparison.Ordinal);
        Assert.Contains("not a finite", Problem(new PartEdits([Copy(1) with { Transform = t with { Scale = new Vec3d(double.PositiveInfinity, 1, 1) } }], [])), StringComparison.Ordinal);
        Assert.Contains("not a finite", Problem(new PartEdits([Leaves with { Moved = new Vec3d(0, double.NegativeInfinity, 0) }], [])), StringComparison.Ordinal);
        Assert.Contains("not a finite", Problem(new PartEdits([Leaves with { Turned = new Vec3d(0, double.NaN, 0) }], [])), StringComparison.Ordinal);
        Assert.Contains("too far", Problem(new PartEdits([Copy(1) with { Transform = t with { Position = new Vec3d(1e7, 0, 0) } }], [])), StringComparison.Ordinal);
        Assert.Contains("too large", Problem(new PartEdits([Copy(1) with { Transform = t with { Scale = new Vec3d(1e6, 1, 1) } }], [])), StringComparison.Ordinal);
    }

    [Fact]
    public void ASeedThatIsNotAWholeNumberInRangeIsRefused()
    {
        Assert.Contains("seed", Problem(new PartEdits([Copy(1) with { Seed = double.NaN }], [])), StringComparison.Ordinal);
        Assert.Contains("seed", Problem(new PartEdits([Copy(1) with { Seed = -1 }], [])), StringComparison.Ordinal);
        Assert.Contains("seed", Problem(new PartEdits([Copy(1) with { Seed = 1e12 }], [])), StringComparison.Ordinal);
    }

    [Fact]
    public void AGeneratorThePropDoesNotUseIsRefused()
    {
        Assert.Contains("unknown-thing", Problem(new PartEdits([Copy(1) with { GeneratorId = "unknown-thing" }], [])), StringComparison.Ordinal);
        Assert.Contains("trunk", Problem(new PartEdits([Copy(1) with { GeneratorId = "trunk" }], [])), StringComparison.Ordinal);
    }

    [Fact]
    public void SlotsThePropDoesNotHaveAreRefused()
    {
        Assert.Contains("slot", Problem(new PartEdits([Leaves with { Slot = "nope" }], [])), StringComparison.Ordinal);
        Assert.Contains("slot", Problem(new PartEdits([], ["nope"])), StringComparison.Ordinal);
        Assert.Contains("slot", Problem(new PartEdits([Leaves, Leaves], [])), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryKeptPartsParametersGoThroughTheCallersRule()
    {
        var seen = new List<string>();
        var checkedEdits = PartEditsCheck.Validate(
            new PartEdits([Leaves with { Moved = new Vec3d(1, 0, 0) }, Copy(1)], []),
            Generated,
            (part, generated) =>
            {
                Assert.Same(Generated, generated);
                seen.Add(part.Id);
                var cut = new ParameterDictionary();
                cut.Set("height", 0.5);
                return cut;
            });
        Assert.Equal(["cmp-bush-leaves", "man-bush-leaves-1"], seen);
        Assert.All(checkedEdits.Kept, part => Assert.Equal(0.5, part.Parameters.Number("height")));

        string problem = Assert.Throws<FormatException>(() => PartEditsCheck.Validate(
            new PartEdits([Copy(1)], []), Generated, (_, _) => throw new FormatException("refused by the rule"))).Message;
        Assert.Equal("refused by the rule", problem);
    }
}
