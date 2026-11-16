namespace PropGenerator.Desktop.View;

/// <summary>What a mouse drag in the view does to the camera.</summary>
internal enum DragKind
{
    /// <summary>Nothing: no button that moves the camera is down.</summary>
    None,

    /// <summary>Turns the camera around its target (left button).</summary>
    Orbit,

    /// <summary>Moves the target across the screen (right button, or Shift with the left).</summary>
    Pan,

    /// <summary>Moves the camera closer or farther (middle button).</summary>
    Zoom,

    /// <summary>Moves, turns or resizes the selected part with the part tool that is on (left button).</summary>
    Part,
}
