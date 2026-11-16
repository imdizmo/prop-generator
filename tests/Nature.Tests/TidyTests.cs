using PropGenerator.Nature.Export;
using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class TidyTests
{
    [Fact]
    public void AFacePointingAgainstAllItsCornersIsTurnedRound()
    {
        // Wound clockwise seen from +z, with every normal on +z.
        var mesh = Mesh([0, 0, 0, 0, 1, 0, 1, 0, 0], [0, 0, 1, 0, 0, 1, 0, 0, 1], [0, 1, 2]);
        Tidy.Wood(mesh);
        Assert.Equal([0u, 2, 1], mesh.Index!);
        Assert.True(mesh.IndexIsUint32);
    }

    [Fact]
    public void AFaceAgreeingWithOneCornerIsKept()
    {
        var mesh = Mesh([0, 0, 0, 0, 1, 0, 1, 0, 0], [0, 0, -1, 0, 0, 1, 0, 0, 1], [0, 1, 2]);
        Tidy.Wood(mesh);
        Assert.Equal([0u, 1, 2], mesh.Index!);
    }

    [Fact]
    public void ACopyOnTheSamePointsFacingTheSameWayIsDropped()
    {
        // Vertex 3 is vertex 0 again, so face 1 is face 0 on the same three points.
        var mesh = Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0], null, [0, 1, 2, 1, 2, 3]);
        Tidy.Wood(mesh);
        Assert.Equal([0u, 1, 2], mesh.Index!);
    }

    [Fact]
    public void TwoFacesBackToBackAreBothKept()
    {
        var mesh = Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], null, [0, 1, 2, 0, 2, 1]);
        Tidy.Wood(mesh);
        Assert.Equal([0u, 1, 2, 0, 2, 1], mesh.Index!);
    }

    [Fact]
    public void PointsAgreeToFiveDecimalsAndMinusZeroIsZero()
    {
        // Face 1's corners differ from face 0's below the fifth decimal, and one is -0 where face 0 has 0.
        var mesh = Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0, -0f, 0.000001f, 0, 1.000004f, 0, 0, 0, 1, 0], null, [0, 1, 2, 3, 4, 5]);
        Tidy.Wood(mesh);
        Assert.Equal([0u, 1, 2], mesh.Index!);
    }

    [Fact]
    public void AMeshWithoutAnIndexIsLeftAlone()
    {
        var mesh = Mesh([0, 0, 0, 0, 1, 0, 1, 0, 0], [0, 0, 1, 0, 0, 1, 0, 0, 1], null);
        Tidy.Wood(mesh);
        Assert.Null(mesh.Index);
    }

    private static PropMesh Mesh(float[] position, float[]? normal, uint[]? index)
    {
        var mesh = new PropMesh { Position = position, Normal = normal };
        mesh.SetIndex(index);
        return mesh;
    }
}
