namespace PropGenerator;

/// <summary>Paints a piece's tone layers. The window depends only on this, so tests can paint without Python.</summary>
public interface IPainter
{
    /// <summary>Raised with a plain message when painting failed. It may be raised on any thread.</summary>
    event Action<string>? Failed;

    /// <summary>
    /// Paints the layers for these settings. Only the newest request matters: a call made while another is still
    /// waiting replaces it, and the replaced call gets null.
    /// </summary>
    /// <param name="size">The texture size: 256, 512 or 1024.</param>
    /// <param name="settings">Every setting's value.</param>
    /// <param name="ct">Cancels this request.</param>
    /// <returns>The layers, or null when the request was replaced by a newer one or painting failed.</returns>
    Task<Layers?> PaintAsync(int size, Settings settings, CancellationToken ct);
}
