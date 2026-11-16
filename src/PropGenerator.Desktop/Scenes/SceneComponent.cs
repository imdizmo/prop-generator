// Ported from easy-props frontend/src/assets/shared/component-types.ts (MIT, © 2026 AHS12).
using PropGenerator.Nature.Kernel;
using PropGenerator.Nature.Parameters;

namespace PropGenerator.Desktop.Scenes;

/// <summary>
/// One part of a prop as the scene holds it: which part generator builds it, with what parameters and seed, and where
/// it sits. A prop generator makes the list; each part generator then builds its own component's geometry.
/// </summary>
/// <param name="Id">Unique within the prop, such as <c>cmp-rock-0</c>.</param>
/// <param name="Type">The kind of part, such as "rock" or "trunk" (easy-props' <c>ComponentType</c>).</param>
/// <param name="GeneratorId">The part generator that builds it, such as "cut-rock-body".</param>
/// <param name="GeneratorVersion">That generator's version.</param>
/// <param name="ParentId">What it is attached to; null for a root of its prop.</param>
/// <param name="Transform">Where it sits.</param>
/// <param name="Parameters">The part generator's parameters, in its key order; a value may be a nested dictionary.</param>
/// <param name="StyleOverrides">Per-component overrides of the style; null for none.</param>
/// <param name="Seed">The component's own seed.</param>
/// <param name="Slot">The generator slot it came from; null for a part added by hand.</param>
/// <param name="Edited">True once the user changed this part's own parameters.</param>
/// <param name="Moved">How far the user moved it from where its generator put it, in metres; null if not moved.</param>
/// <param name="Turned">
/// Ours, not easy-props': how the user turned it from where its generator turned it, as an XYZ Euler in radians of a
/// turn applied before the generator's (about the prop's own axes); null if not turned.
/// </param>
/// <param name="Sized">Ours, not easy-props': how many times its generator's size the user made it; null if not resized.</param>
public sealed record SceneComponent(
    string Id,
    string Type,
    string GeneratorId,
    int GeneratorVersion,
    string? ParentId,
    Transform Transform,
    ParameterDictionary Parameters,
    ParameterDictionary? StyleOverrides,
    double Seed,
    string? Slot,
    bool Edited,
    Vec3d? Moved,
    Vec3d? Turned = null,
    double? Sized = null);
