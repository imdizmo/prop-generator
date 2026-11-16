using Xunit;

namespace PropGenerator.Engine.Tests.Support;

/// <summary>
/// One engine on the embedded bundle for a whole test class (its tests run one at a time), loaded on first use, with
/// the largest runtime size seen after a call. A class whose tests need the bundle skips them while it is not built.
/// </summary>
public abstract class EngineFixture : IDisposable
{
    private readonly Lazy<EasyPropsEngine> _engine;
    private readonly Lazy<EnginePresets> _presets;

    /// <summary>Initializes a new instance of the <see cref="EngineFixture"/> class: it loads nothing until a test asks.</summary>
    protected EngineFixture()
    {
        _engine = new(() => new EasyPropsEngine());
        _presets = new(() => Engine.Presets());
    }

    /// <summary>The engine, loaded on first use; skips the test when the bundle is not embedded.</summary>
    public EasyPropsEngine Engine
    {
        get
        {
            Assert.SkipUnless(EngineOptions.HasEmbeddedBundle, "Resources/engine.js is not built yet (tools/engine).");
            return _engine.Value;
        }
    }

    /// <summary>The engine's presets, read once.</summary>
    public EnginePresets Presets => _presets.Value;

    /// <summary>The largest heap plus external memory seen after a call, in bytes.</summary>
    public long PeakBytes { get; private set; }

    /// <summary>Notes the runtime's size now; returns it.</summary>
    /// <returns>Heap plus external bytes.</returns>
    public long Sample()
    {
        EngineHeap heap = Engine.Heap();
        long bytes = heap.TotalBytes + heap.ExternalBytes;
        PeakBytes = Math.Max(PeakBytes, bytes);
        return bytes;
    }

    /// <summary>A preset's scene as easy-props makes it from the preset (components made by the engine).</summary>
    /// <param name="preset">The preset.</param>
    /// <param name="build">A build to switch to ('low-poly'), or null for the preset's own.</param>
    /// <returns>The scene JSON.</returns>
    public static string SceneOf(EnginePreset preset, string? build = null)
    {
        string parameters = preset.Parameters.GetRawText();
        if (build is not null)
        {
            var values = System.Text.Json.Nodes.JsonNode.Parse(parameters)!.AsObject();
            values["build"] = build;
            parameters = values.ToJsonString();
        }

        return $$"""{"generatorId":"{{preset.GeneratorId}}","seed":{{preset.Seed}},"parameters":{{parameters}}}""";
    }

    /// <summary>Frees the runtime.</summary>
    public void Dispose()
    {
        if (_engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>The engine as the app loads it: the shims and the bundle only, V8's own Math.</summary>
public sealed class AppEngine() : EngineFixture();
