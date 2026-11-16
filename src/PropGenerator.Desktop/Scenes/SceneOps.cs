// Ported from easy-props frontend/src/core/scene/scene-ops.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// What the app keeps of the scene operations for easy-props' kinds, whose own ops run on its engine: a part and what
/// hangs off it, and ours, a part's turn and size folded onto it after easy-props' <c>mergeGenerated</c>
/// (<see cref="SceneComponent.Turned"/>, <see cref="SceneComponent.Sized"/>).
/// </summary>
public static class SceneOps
{
    /// <summary>The component and everything hanging off it, in the list's order.</summary>
    /// <param name="components">The prop's components.</param>
    /// <param name="componentId">The component.</param>
    /// <returns>It and its descendants; empty when it is not there.</returns>
    public static IReadOnlyList<SceneComponent> WithDescendants(IReadOnlyList<SceneComponent> components, string componentId)
    {
        ArgumentNullException.ThrowIfNull(components);
        var collected = new HashSet<string>(StringComparer.Ordinal) { componentId };
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (SceneComponent component in components)
            {
                if (component.ParentId is { Length: > 0 } parent && collected.Contains(parent) && !collected.Contains(component.Id))
                {
                    collected.Add(component.Id);
                    changed = true;
                }
            }
        }

        return [.. components.Where(component => collected.Contains(component.Id))];
    }

    /// <summary>
    /// Ours (easy-props has neither): a part as merged, turned and resized as the user turned and resized it before. A
    /// turn and a size go with the part as its move does; easy-props' <c>mergeGenerated</c> folds in the move, and this
    /// adds the rest.
    /// </summary>
    /// <param name="merged">The part as merged from what its generator makes now.</param>
    /// <param name="previous">The part as the user left it (its kept edit).</param>
    /// <returns>The part, turned and sized.</returns>
    public static SceneComponent TurnedAndSized(SceneComponent merged, SceneComponent previous)
    {
        ArgumentNullException.ThrowIfNull(merged);
        ArgumentNullException.ThrowIfNull(previous);
        SceneComponent placed = previous.Turned is { } turned ? TurnedBy(merged, turned) : merged;
        return previous.Sized is double sized ? SizedBy(placed, sized) : placed;
    }

    // Ours: a fresh part, turned as the user turned it.
    private static SceneComponent TurnedBy(SceneComponent fresh, Vec3d turned)
    {
        Quatd rotation = QuaternionOf(turned).Multiply(QuaternionOf(fresh.Transform.Rotation));
        return fresh with { Transform = fresh.Transform with { Rotation = EulerOf(rotation) }, Turned = turned };
    }

    // Ours: a fresh part, sized as the user sized it.
    private static SceneComponent SizedBy(SceneComponent fresh, double sized) =>
        fresh with { Transform = fresh.Transform with { Scale = fresh.Transform.Scale.Clone().MultiplyScalar(sized) }, Sized = sized };

    private static Quatd QuaternionOf(Vec3d euler) => new Quatd().SetFromEuler(new Euler(euler.X, euler.Y, euler.Z));

    private static Vec3d EulerOf(Quatd q)
    {
        Euler e = new Euler().SetFromQuaternion(q);
        return new Vec3d(e.X, e.Y, e.Z);
    }
}
