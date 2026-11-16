using PropGenerator.Nature.Kernel;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class PropMeshTests
{
    [Fact]
    public void CloneCopiesEveryArray()
    {
        var mesh = new PropMesh
        {
            Position = [0, 1, 2, 3, 4, 5, 6, 7, 8],
            Normal = [0, 0, 1, 0, 0, 1, 0, 0, 1],
            Uv = [0, 0, 1, 0, 0, 1],
        };
        mesh.SetIndex([0, 1, 2]);
        mesh.Groups.Add((0, 3, 0));
        var copy = mesh.Clone();
        mesh.Position[0] = 9;
        mesh.Normal![2] = 9;
        mesh.Uv![0] = 9;
        mesh.Index![0] = 9;
        mesh.Groups.Clear();
        Assert.Equal(0, copy.Position[0]);
        Assert.Equal(1, copy.Normal![2]);
        Assert.Equal(0, copy.Uv![0]);
        Assert.Equal(0u, copy.Index![0]);
        Assert.Null(copy.Color);
        Assert.Null(copy.Uv1);
        Assert.Equal([(0, 3, 0)], copy.Groups);
    }

    [Fact]
    public void SetIndexPicksSixteenBitsBelow65535AsThreeDoes()
    {
        var mesh = new PropMesh { Position = new float[9] };
        mesh.SetIndex([0, 1, 65534]);
        Assert.False(mesh.IndexIsUint32);
        mesh.SetIndex([65535, 1, 2]);
        Assert.True(mesh.IndexIsUint32);
        Assert.Equal([65535u, 1, 2], mesh.Index!);
        mesh.SetIndex([0, 1, 2], wide: true);
        Assert.True(mesh.IndexIsUint32);
        mesh.SetIndex(null);
        Assert.Null(mesh.Index);
        Assert.False(mesh.IndexIsUint32);
    }

    [Fact]
    public void CloneKeepsTheIndexWidth()
    {
        var mesh = new PropMesh { Position = new float[9] };
        mesh.SetIndex([0, 1, 2], wide: true);
        Assert.True(mesh.Clone().IndexIsUint32);
    }

    [Fact]
    public void VertexCountIsPositionsOverThree()
    {
        Assert.Equal(2, new PropMesh { Position = new float[6] }.VertexCount);
    }

    [Fact]
    public void TheIndexIsOnlySetThroughSetIndexSoItsWidthFollows()
    {
        var setter = typeof(PropMesh).GetProperty(nameof(PropMesh.Index))!.SetMethod;
        Assert.True(setter is null || !setter.IsPublic, "PropMesh.Index has a public setter; set it with SetIndex");
    }

    [Fact]
    public void CloneKeepsTheRigidMarksAsACopy()
    {
        var mesh = new PropMesh { Position = [0, 1, 0, 1, 1, 0, 0, 2, 0], Rigid = [1, 0, 1] };
        var copy = mesh.Clone();
        mesh.Rigid![0] = 0;
        Assert.Equal([1f, 0f, 1f], copy.Rigid!);
    }
}
