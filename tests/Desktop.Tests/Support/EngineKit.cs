using System.Text.Json;
using PropGenerator.Desktop.Engine;
using PropGenerator.Desktop.Recipes;
using PropGenerator.Engine;

namespace PropGenerator.Desktop.Tests.Support;

/// <summary>
/// What the tests of easy-props' kinds assert against: the app's engine recipes, the engine's own parts and shapes for
/// a recipe's settings (built on the app's engine host, each call with a replace key of its own so it drops nothing),
/// and the named colours of an own-colour row. Every call that waits on the engine is made off the UI thread.
/// </summary>
internal static class EngineKit
{
    private const string Style = "low-poly";

    /// <summary>The app's recipe of one of easy-props' kinds, as the app finds it.</summary>
    /// <param name="id">The kind's id, such as "bush".</param>
    /// <returns>The engine recipe.</returns>
    internal static EngineRecipe Recipe(string id) =>
        MeshRecipes.Find(id) as EngineRecipe ?? throw new InvalidOperationException($"'{id}' is not an engine recipe in the app.");

    /// <summary>The engine's parts for these settings at a share of full detail, as the recipe's build reads them.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="s">The settings.</param>
    /// <param name="detail">The share of full detail.</param>
    /// <returns>The engine's build.</returns>
    internal static EngineMesh Parts(EngineRecipe recipe, Settings s, double detail) =>
        Off(() => EngineHost.Default.Builds.BuildAsync(recipe.SceneJson(s), Style, detail, EngineJobKind.Live, replaces: Key(), cancel: Ct));

    /// <summary>
    /// The drawn (not proxy) parts' positions, each placed by its matrix and turned from easy-props' Y-up to the app's
    /// Z-up as (x, -z, y), in the engine's part order: what the view's mesh should hold.
    /// </summary>
    /// <param name="mesh">The engine's build.</param>
    /// <returns>xyz per vertex.</returns>
    internal static float[] PlacedZUp(EngineMesh mesh)
    {
        var all = new List<float>();
        foreach (EnginePart part in mesh.Parts.Where(p => !p.Proxy))
        {
            IReadOnlyList<double> m = part.Matrix;
            float[] p = part.Positions;
            for (int i = 0; i < p.Length; i += 3)
            {
                double x = p[i], y = p[i + 1], z = p[i + 2];
                all.Add((float)((m[0] * x) + (m[4] * y) + (m[8] * z) + m[12]));
                all.Add((float)-((m[2] * x) + (m[6] * y) + (m[10] * z) + m[14]));
                all.Add((float)((m[1] * x) + (m[5] * y) + (m[9] * z) + m[13]));
            }
        }

        return [.. all];
    }

    /// <summary>Checks two lists of floats are the same length and equal within a tolerance.</summary>
    /// <param name="want">The expected values.</param>
    /// <param name="got">The values.</param>
    /// <param name="tolerance">The largest difference allowed.</param>
    internal static void Close(float[] want, float[] got, double tolerance = 1e-5)
    {
        Xunit.Assert.Equal(want.Length, got.Length);
        for (int i = 0; i < want.Length; i++)
        {
            if (Math.Abs(want[i] - got[i]) > tolerance)
            {
                Xunit.Assert.Fail($"value {i}: {got[i]}, expected {want[i]}");
            }
        }
    }

    /// <summary>The engine's collision for these settings: what the generator offers and what the export writes.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="s">The settings.</param>
    /// <returns>The engine's collision.</returns>
    internal static EngineCollision Collision(EngineRecipe recipe, Settings s) =>
        Off(() => EngineHost.Default.Builds.CollisionAsync(recipe.SceneJson(s), Style, EngineJobKind.Live, Key(), Ct));

    /// <summary>The engine's export package for these settings, as the recipe's GLB asks for it (one level, no collision).</summary>
    /// <param name="recipe">The recipe.</param>
    /// <param name="s">The settings.</param>
    /// <param name="modelName">The model's name.</param>
    /// <returns>The package's files.</returns>
    internal static IReadOnlyList<EngineFile> Package(EngineRecipe recipe, Settings s, string modelName) =>
        Off(() => EngineHost.Default.Builds.ExportAsync(recipe.SceneJson(s), Style, EngineMeshes.ExportSettings(modelName, ExportOptions.Default), cancel: Ct));

    /// <summary>A texture's pixels as the app's texture cache holds them.</summary>
    /// <param name="key">The texture's key, such as "bark:oak:0.5".</param>
    /// <returns>The texture.</returns>
    internal static MeshTexture Texture(string key) =>
        Task.Run(() => EngineHost.Default.Textures.Get(key, EngineJobKind.Live, Ct), Ct).GetAwaiter().GetResult();

    /// <summary>
    /// An own-colour row's colour for one option of its choice, as upstream names it (the row's <c>named</c>), such as
    /// the leaf middle of the Forest green palette.
    /// </summary>
    /// <param name="kind">The kind's id.</param>
    /// <param name="key">The colour row's key.</param>
    /// <param name="option">The choice's option.</param>
    /// <returns>The colour.</returns>
    internal static Rgb Named(string kind, string key, string option)
    {
        EngineParameters p = EngineHost.Default.Catalog.Parameters(kind);
        JsonElement row = p.Specs.Concat(p.ColourRows).Single(spec => spec.GetProperty("key").GetString() == key);
        return Rgb.FromHex(row.GetProperty("named").GetProperty(option).GetString()!);
    }

    /// <summary>
    /// Runs engine work on the pool while the UI thread keeps pumping, for an <c>[AvaloniaFact]</c>: the engine recipes
    /// refuse to wait on the UI thread.
    /// </summary>
    /// <typeparam name="T">What the work gives.</typeparam>
    /// <param name="work">The work.</param>
    /// <returns>What it gave.</returns>
    internal static T OnThePool<T>(Func<T> work) => ClaudeKit.Run(Task.Run(work, Ct), 120000);

    private static CancellationToken Ct => Xunit.TestContext.Current.CancellationToken;

    private static string Key() => $"test-{Guid.NewGuid():N}";

    // Waits on the pool, never inline on the calling thread (a UI thread's wait would run the engine call there).
    private static T Off<T>(Func<Task<T>> job) => Task.Run(job, Ct).GetAwaiter().GetResult();
}
