namespace PropGenerator;

/// <summary>One model sent to Groundwork's inbox: its GLB is <c>&lt;Model&gt;.glb</c>.</summary>
/// <param name="Model">The model's name, which is also its file's name without ".glb": a plain name.</param>
/// <param name="Label">The name shown in Groundwork's drawer (cut to <see cref="GroundworkInbox.MaxLabel"/>).</param>
/// <param name="Category">The drawer's group: one of <see cref="GroundworkInbox.Categories"/>.</param>
/// <param name="Scale">The size range, two numbers, or null for [1, 1].</param>
public sealed record InboxKind(string Model, string Label, string Category, float[]? Scale = null);
