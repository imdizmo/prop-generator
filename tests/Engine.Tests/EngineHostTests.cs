using System.Globalization;
using PropGenerator.Engine.Tests.Support;
using Xunit;

namespace PropGenerator.Engine.Tests;

/// <summary>The host on its own (one runtime, called on the test's thread), against the stand-in bundle.</summary>
public sealed class EngineHostTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AV8LibraryThatCannotLoadIsAnEngineException()
    {
        // ClearScript says a missing or unloadable native V8 library with a bare TypeLoadException: the engine says it
        // as an EngineException, which every engine-load catch (the launch's fallback to the port, the build thread) takes.
        EngineException e = Assert.Throws<EngineException>(() => EasyPropsEngine.Native<object>(() => throw new TypeLoadException("Cannot load ClearScript V8 library.")));
        Assert.IsType<TypeLoadException>(e.InnerException);
        Assert.Contains("Cannot load ClearScript V8 library", e.Message, StringComparison.Ordinal);
        Assert.Equal(7, EasyPropsEngine.Native(() => 7));
    }

    [Fact]
    public void TheEngineLoadsAndItsInfoNamesTheTag()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineInfo info = engine.Info(Ct);
        Assert.Equal("v0.4.2", info.Tag);
        Assert.Equal("27c6956", info.Commit);
        Assert.Equal([1, 0.4, 0.12], info.LevelShares);
        Assert.Equal(3, info.FarLevel);
        Assert.Equal(["painted-tree", "cut-rock"], info.Generators.Select(g => g.Id));
        Assert.True(info.Generators[0].OffersCollision);
        Assert.True(info.Generators[1].Still);
        Assert.Equal(1, engine.Generation);
    }

    [Fact]
    public void PresetsKeepTheirParametersAndAMissingCategory()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EnginePresets all = engine.Presets(Ct);
        Assert.Equal(["Broadleaf", "Rocks"], all.Categories);
        Assert.Equal(3, all.Presets.Count);
        EnginePreset round = all.Presets.Single(p => p.Id == "round-canopy");
        Assert.Null(round.Category);
        Assert.Equal(12345, round.Seed);
        Assert.Equal(4.2, round.Parameters.GetProperty("height").GetDouble());
        Assert.Equal("tree", round.Family);
        Assert.Equal("wide-canopy", engine.Preset("wide-canopy", Ct)?.Id);
        Assert.Null(engine.Preset("no-such-preset", Ct));
    }

    [Fact]
    public void ParametersComeAsUpstreamSpecsAndColourRows()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineParameters p = engine.Parameters("painted-tree", Ct);
        Assert.Equal("height", p.Specs[0].GetProperty("key").GetString());
        Assert.Equal("barkDeep", p.ColourRows.Single().GetProperty("key").GetString());
        Assert.Empty(engine.Parameters("cut-rock", Ct).ColourRows);
    }

    [Fact]
    public void ABuildComesBackAsPartsWithTheirArrays()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineMesh mesh = engine.Build(Standin.Scene, "low-poly", 0.4, cancel: Ct);

        Assert.Null(mesh.FullFaces);
        Assert.Equal(3, mesh.Parts.Count);

        EnginePart trunk = mesh.Parts[0];
        Assert.Equal("trunk", trunk.ComponentId);
        Assert.Equal("painted-tree", trunk.GeneratorId);
        Assert.Equal("bark", trunk.Material);
        Assert.Equal("wood", trunk.Role);
        Assert.Equal("trunk", trunk.RoleOfPart);
        Assert.False(trunk.FlatShading);
        Assert.False(trunk.Proxy);
        Assert.Equal(1.25, trunk.Growth);
        Assert.Equal([1, 0.5, 0.25], trunk.Tint!);
        Assert.Equal(16, trunk.Matrix.Count);
        Assert.Equal("bark:2:0.5", trunk.Surface.Map);
        Assert.Equal("relief:2", trunk.Surface.NormalMap);
        Assert.True(trunk.Surface.VertexColors);
        Assert.Null(trunk.Surface.AlphaTest);
        Assert.Equal((3, 1), (trunk.VertexCount, trunk.Faces));
        Assert.Equal(["position", "normal", "color", "uv"], trunk.Attributes.Keys);
        Assert.Equal(2, trunk.Attributes["uv"].ItemSize);
        Assert.Equal([0f, 0, 0, 1, 0, 0, 0, 1, 0.4f], trunk.Positions);
        Assert.Equal([0f, 0, 1, 0, 0, 1, 0, 0, 1], trunk.Normals);
        Assert.Equal([1f, 0, 0, 0, 1, 0, 0, 0, 1], trunk.Colors!);
        Assert.Equal([0f, 0, 1, 0, 0, 1], trunk.Uvs!);
        Assert.Null(trunk.Uv1);
        Assert.Null(trunk.Rigid);
        Assert.Equal([0u, 1, 2], trunk.Index!);
        Assert.True(trunk.Index16);

        EnginePart crown = mesh.Parts[1];
        Assert.Null(crown.Role);
        Assert.True(crown.FlatShading);
        Assert.True(crown.Proxy);
        Assert.Equal(0.5, crown.Surface.AlphaTest);
        Assert.Equal("leaf", crown.Surface.Map);
        Assert.Null(crown.Surface.NormalMap);
        Assert.Null(crown.Colors);
        Assert.Null(crown.Uvs);
        Assert.Equal([5f, 5, 5, 6, 6, 6, 7, 7, 7], crown.Positions);
        Assert.Equal([0.5f, 0.5f, 1, 1, 0, 0], crown.Uv1!);
        Assert.Equal([1f, 0, 1], crown.Rigid!);
        Assert.Equal(1, crown.Attributes["rigid"].ItemSize);
        Assert.Equal([2u, 1, 70000], crown.Index!);
        Assert.False(crown.Index16);
        Assert.Equal(1, crown.Matrix[12]);

        EnginePart loose = mesh.Parts[2];
        Assert.Null(loose.Index);
        Assert.Empty(loose.Normals);
        Assert.False(loose.Surface.VertexColors);
        Assert.Null(loose.Surface.Map);

        // The arguments reach the bundle unchanged (the stand-in echoes them in meta).
        System.Text.Json.JsonElement echo = mesh.Meta.GetProperty("echo");
        Assert.Equal("low-poly", echo.GetProperty("style").GetString());
        Assert.Equal(0.4, echo.GetProperty("detail").GetDouble());
        Assert.Equal(4242, echo.GetProperty("scene").GetProperty("seed").GetInt32());
        Assert.Equal(0.5, crown.Meta.GetProperty("growth").GetDouble());
    }

    [Fact]
    public void AFullFaceCountIsAskedForOnlyWhenWanted()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        Assert.Equal(1234, engine.Build(Standin.Scene, "low-poly", 0.12, fullFaces: true, Ct).FullFaces);
    }

    [Fact]
    public void ATextureComesBackAsRgbaPixels()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineTexture t = engine.Texture("bark:2:0.5", Ct);
        Assert.Equal((2, 2), (t.Width, t.Height));
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), t.Rgba);
    }

    [Fact]
    public void AnExportComesBackAsFilesWithPngsFromTheHostEncoder()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        IReadOnlyList<EngineFile> files = engine.ExportPackage(Standin.Scene, "low-poly", """{"name":"oak"}""", Ct);
        Assert.Equal(["oak.glb", "oak_bark.png"], files.Select(f => f.Name));
        Assert.Equal([321, null], files.Select(f => f.Faces));
        Assert.Equal("glTF-standin"u8.ToArray(), files[0].Bytes);

        byte[] rgba = new byte[3 * 2 * 4];
        for (int i = 0; i < rgba.Length; i++)
        {
            rgba[i] = (byte)((i * 37) & 255);
        }

        Assert.Equal(PngEncoder.Encode(rgba, 3, 2), files[1].Bytes);
    }

    [Fact]
    public void CollisionAndPivotAreRead()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineCollision collision = engine.Collision(Standin.Scene, "low-poly", Ct);
        Assert.Empty(collision.Offered);
        EngineCapsule c = Assert.Single(collision.Shapes);
        Assert.Equal([0, 2, 0], c.End);
        Assert.Equal(0.3, c.StartRadius);
        Assert.Equal([0, 0.25, 0], engine.Pivot(Standin.Scene, Ct));
    }

    [Fact]
    public void AHeapBlowUpIsCaughtAndTheEngineRecovers()
    {
        EngineOptions options = Standin.Options() with { MaxHeapBytes = 48L << 20, MaxOldSpaceMiB = 256 };
        using var engine = new EasyPropsEngine(options);
        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);

        Assert.Throws<EngineOutOfMemoryException>(() => engine.Call("Test.blowUp", Ct));

        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);
        Assert.Equal(2, engine.Generation);
    }

    [Fact]
    public void ArrayBuffersPastTheCapFailWithoutKillingTheEngine()
    {
        EngineOptions options = Standin.Options() with { MaxArrayBufferBytes = 16L << 20 };
        using var engine = new EasyPropsEngine(options);
        Assert.Equal(1024.0, Convert.ToDouble(engine.Call("Test.bigBuffer", Ct, 1024.0), CultureInfo.InvariantCulture));

        Assert.ThrowsAny<EngineException>(() => engine.Call("Test.bigBuffer", Ct, (double)(64 << 20)));

        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);
    }

    [Fact]
    public void ACancelledJobRejectsAndTheEngineStaysUsable()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Call("Test.slow", cancel.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);
        Assert.Equal(1, engine.Generation);
    }

    [Fact]
    public void ACancelledLoopThatNeverYieldsIsInterrupted()
    {
        EngineOptions options = Standin.Options() with { CancelGrace = TimeSpan.FromMilliseconds(50) };
        using var engine = new EasyPropsEngine(options);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() => engine.Call("Test.spin", cancel.Token));

        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);
    }

    [Fact]
    public void TimersLeftByACancelledJobDoNotRunInTheNextOne()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.ThrowsAny<OperationCanceledException>(() => engine.Call("Test.leaveTimer", cancel.Token));

        Assert.Equal(false, engine.Call("Test.firedLater", Ct));
    }

    [Fact]
    public void AJobPastItsTimeIsStopped()
    {
        EngineOptions options = Standin.Options() with { JobTimeout = TimeSpan.FromMilliseconds(200) };
        using var engine = new EasyPropsEngine(options);

        Assert.Throws<TimeoutException>(() => engine.Call("Test.hang", Ct));
        Assert.Throws<TimeoutException>(() => engine.Call("Test.spin", Ct));

        Assert.Equal("v0.4.2", engine.Info(Ct).Tag);
    }

    [Fact]
    public void AnEngineGrownPastTheRecycleSizeIsRebuiltBeforeTheNextCall()
    {
        EngineOptions options = Standin.Options() with { RecycleBytes = 8L << 20 };
        using var engine = new EasyPropsEngine(options);
        Assert.Equal(1.0, Convert.ToDouble(engine.Call("Test.grow", Ct, 4.0), CultureInfo.InvariantCulture));
        Assert.Equal(1, engine.Generation);
        Assert.Equal(2.0, Convert.ToDouble(engine.Call("Test.grow", Ct, 16.0), CultureInfo.InvariantCulture));

        // The engine held 20 MiB of buffers, past the 8 MiB mark: the next call runs in a fresh one.
        Assert.Equal(1.0, Convert.ToDouble(engine.Call("Test.grow", Ct, 1.0), CultureInfo.InvariantCulture));
        Assert.Equal(2, engine.Generation);
    }

    [Fact]
    public void ACallThatCrossesTheRecycleSizeStillReturnsItsArrays()
    {
        // Every call leaves the runtime past 1 byte, so each one is recycled right after it: its result must be
        // read out of the runtime before that.
        EngineOptions options = Standin.Options() with { RecycleBytes = 1 };
        using var engine = new EasyPropsEngine(options);

        EngineMesh mesh = engine.Build(Standin.Scene, "low-poly", 1, cancel: Ct);
        EngineTexture texture = engine.Texture("dab", Ct);
        IReadOnlyList<EngineFile> files = engine.ExportPackage(Standin.Scene, "low-poly", "{}", Ct);

        Assert.Equal(3, mesh.Parts.Count);
        Assert.NotEmpty(texture.Rgba);
        Assert.NotEmpty(files);
        Assert.Equal(3, engine.Generation);
    }

    [Fact]
    public void AScriptErrorSaysWhatFailed()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineScriptException e = Assert.Throws<EngineScriptException>(() => engine.Call("Test.noSuchFunction", Ct));
        Assert.Contains("noSuchFunction", e.Message);
    }

    [Fact]
    public void TheHeapIsReported()
    {
        using var engine = new EasyPropsEngine(Standin.Options());
        EngineHeap heap = engine.Heap();
        Assert.True(heap.UsedBytes > 0);
        Assert.True(heap.LimitBytes >= heap.UsedBytes);
    }
}
