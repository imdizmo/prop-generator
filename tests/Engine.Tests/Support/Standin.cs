namespace PropGenerator.Engine.Tests.Support;

/// <summary>The stand-in bundle (Fixtures/standin.js): the frozen API's names with canned answers.</summary>
internal static class Standin
{
    public static EngineScript Script { get; } =
        EngineScript.FromFile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "standin.js"));

    public static EngineOptions Options() => new() { Scripts = [Script] };

    public const string Scene = """{"generatorId":"painted-tree","seed":4242,"parameters":{"height":8},"components":[]}""";
}
