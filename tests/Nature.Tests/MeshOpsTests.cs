using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Tests.Support;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class MeshOpsTests
{
    private const double Pivot0 = 0.123456789;
    private const double Pivot1 = -0.987654321;
    private const double Pivot2 = 0.5;

    public static TheoryData<string> Meshes => ["ico", "icoWelded", "tube", "soup", "quirk", "nearZero"];

    [Theory]
    [MemberData(nameof(Meshes))]
    public void ComputeVertexNormalsMatchesThree(string mesh)
    {
        var geometry = KernelGoldens.RequiredMesh($"{mesh}/input");
        MeshOps.ComputeVertexNormals(geometry);
        Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/computeVertexNormals"), geometry, mesh);
    }

    [Theory]
    [InlineData("icoWelded")]
    [InlineData("tube")]
    [InlineData("quirk")]
    public void ToNonIndexedMatchesThree(string mesh)
    {
        var geometry = KernelGoldens.RequiredMesh($"{mesh}/input");
        Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/toNonIndexed"), MeshOps.ToNonIndexed(geometry), mesh);
        Assert.NotNull(geometry.Index);
    }

    [Fact]
    public void ToNonIndexedOfANonIndexedMeshIsThatMesh()
    {
        var geometry = KernelGoldens.RequiredMesh("ico/input");
        Assert.Same(geometry, MeshOps.ToNonIndexed(geometry));
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void ApplyMatrix4MatchesThree(string mesh)
    {
        foreach (string matrix in new[] { "m1", "m2" })
        {
            var m = new Mat4d().FromArray(KernelGoldens.Doubles("matrices", matrix));
            var geometry = KernelGoldens.RequiredMesh($"{mesh}/input");
            Assert.Same(geometry, MeshOps.ApplyMatrix4(geometry, m));
            Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/applyMatrix4/{matrix}"), geometry, $"{mesh} {matrix}");
        }
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void TranslateAndScaleMatchThree(string mesh)
    {
        var moved = MeshOps.Translate(KernelGoldens.RequiredMesh($"{mesh}/input"), -Pivot0, -Pivot1, -Pivot2);
        Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/translate"), moved, $"{mesh} translate");
        var scaled = MeshOps.Scale(KernelGoldens.RequiredMesh($"{mesh}/input"), 0.8, 1.7, 0.45);
        Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/scale"), scaled, $"{mesh} scale");
    }

    [Theory]
    [MemberData(nameof(Meshes))]
    public void MergeVerticesMatchesThree(string mesh)
    {
        var geometry = KernelGoldens.RequiredMesh($"{mesh}/input");
        Exact.Mesh(KernelGoldens.RequiredMesh($"{mesh}/mergeVertices"), MeshOps.MergeVertices(geometry, 1e-4), mesh);
    }

    [Fact]
    public void MergeMatchesThree()
    {
        var tube = KernelGoldens.RequiredMesh("tube/input");
        var tubeM1 = KernelGoldens.RequiredMesh("tube/applyMatrix4/m1");
        var ico = KernelGoldens.RequiredMesh("ico/input");
        var icoM2 = KernelGoldens.RequiredMesh("ico/applyMatrix4/m2");
        var icoWelded = KernelGoldens.RequiredMesh("icoWelded/input");

        Exact.Mesh(KernelGoldens.RequiredMesh("merge/tubes"), MeshOps.Merge([tube, tubeM1], false)!, "tubes");
        Exact.Mesh(KernelGoldens.RequiredMesh("merge/tubesGroups"), MeshOps.Merge([tube, tubeM1, tube], true)!, "tubes with groups");
        Exact.Mesh(KernelGoldens.RequiredMesh("merge/icosGroups"), MeshOps.Merge([ico, icoM2], true)!, "icos with groups");
        Assert.Null(KernelGoldens.Mesh("merge/indexedMismatch"));
        Assert.Null(MeshOps.Merge([tube, ico], false));
        Assert.Null(KernelGoldens.Mesh("merge/attributeMismatch"));
        Assert.Null(MeshOps.Merge([icoWelded, tube], false));
    }

    [Fact]
    public void MergeTreatsAnEmptyAttributeAsPresent()
    {
        var empty = Indexed(new PropMesh { Position = [], Normal = [] }, []);
        var tri = Indexed(new PropMesh { Position = [0, 0, 0, 1, 0, 0, 0, 1, 0], Normal = [0, 0, 1, 0, 0, 1, 0, 0, 1] }, [0, 1, 2]);
        var merged = MeshOps.Merge([empty, tri], true);
        Assert.NotNull(merged);
        Assert.Equal([0u, 1, 2], merged.Index!);
        Assert.Equal([(0, 0, 0), (0, 3, 1)], merged.Groups);
    }

    [Fact]
    public void MergeAndMergeVerticesPickTheIndexWidthAsThreeDoes()
    {
        var big = Indexed(new PropMesh { Position = new float[65535 * 3] }, [0, 1, 2]);
        var tri = Indexed(new PropMesh { Position = [0, 0, 0, 1, 0, 0, 0, 1, 0] }, [0, 1, 2]);
        Assert.False(MeshOps.Merge([tri, tri], false)!.IndexIsUint32);
        Assert.True(MeshOps.Merge([big, tri], false)!.IndexIsUint32);
        Assert.False(MeshOps.MergeVertices(tri).IndexIsUint32);
    }

    private static PropMesh Indexed(PropMesh mesh, uint[] index)
    {
        mesh.SetIndex(index);
        return mesh;
    }
}
