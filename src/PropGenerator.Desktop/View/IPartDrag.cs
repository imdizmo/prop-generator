using Avalonia;

namespace PropGenerator.Desktop.View;

/// <summary>What a left drag in the view does to the selected part while a part tool (Move, Turn, Resize) is on.</summary>
internal interface IPartDrag
{
    /// <summary>The left button went down: true to take the drag for the part, false to leave it to the camera.</summary>
    /// <param name="at">Where, in the view.</param>
    /// <returns>True when the part takes the drag.</returns>
    bool Begin(Point at);

    /// <summary>The pointer moved during a drag the part took.</summary>
    /// <param name="at">Where it is now.</param>
    /// <param name="from">Where the drag began.</param>
    /// <param name="shift">Whether Shift is held (up and down for Move, about the camera's side for Turn).</param>
    void Move(Point at, Point from, bool shift);

    /// <summary>The drag ended: the part stays where it was dragged, as one undo step.</summary>
    void End();
}
