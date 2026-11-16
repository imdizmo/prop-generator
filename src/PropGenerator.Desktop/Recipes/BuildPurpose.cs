namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// What a build is for, so an engine-backed recipe puts it in the right queue: the view's builds never wait behind the
/// drawer's pictures, and an export never drops the view's build.
/// </summary>
internal enum BuildPurpose
{
    /// <summary>The view's own build at the detail level shown (with its shapes, when they are wanted).</summary>
    View,

    /// <summary>The view's count of the full-detail faces, after a drag let go.</summary>
    Count,

    /// <summary>The shapes you can walk into of the prop shown, worked out after it was shown.</summary>
    Shapes,

    /// <summary>An export: a file written or sent to Blender.</summary>
    Export,

    /// <summary>A picture of one version in Show me more.</summary>
    ShowMore,

    /// <summary>A picture in the objects drawer.</summary>
    Picture,
}
