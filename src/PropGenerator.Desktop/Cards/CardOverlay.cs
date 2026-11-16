// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PropGenerator.Desktop.Cards;

/// <summary>
/// Shows one small card at a time over the window (the keyboard shortcuts, About), on a dimmed backdrop. A click on
/// the backdrop closes it, and so do Esc and the card's own close button.
/// </summary>
internal sealed class CardOverlay
{
    private static readonly IBrush Backdrop = new ImmutableSolidColorBrush(Color.FromArgb(128, 8, 9, 10));

    private readonly Panel _overlay;
    private Grid? _shade;

    /// <summary>Uses a panel that lies over the whole window.</summary>
    /// <param name="overlay">The panel the card goes in.</param>
    internal CardOverlay(Panel overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        _overlay = overlay;
    }

    /// <summary>Gets the card shown now, or null when none is.</summary>
    internal Control? Shown { get; private set; }

    /// <summary>Shows a card in the middle of the window, in place of any card shown before.</summary>
    /// <param name="card">The card.</param>
    /// <param name="nearTop">True to put it 11% of the window's height from the top, no taller than 66% of it (the command palette), instead of in the middle.</param>
    internal void Show(Control card, bool nearTop = false)
    {
        ArgumentNullException.ThrowIfNull(card);
        Close();
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = nearTop ? VerticalAlignment.Top : VerticalAlignment.Center;
        if (nearTop)
        {
            card.Margin = new Avalonia.Thickness(0, _overlay.Bounds.Height * 0.11, 0, 0);
            card.MaxHeight = Math.Max(120, _overlay.Bounds.Height * 0.66);
        }

        var shade = new Grid { Background = Backdrop };
        shade.Children.Add(card);
        shade.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, shade))
            {
                CloseUnlessBusy();
                e.Handled = true;
            }
        };
        _shade = shade;
        Shown = card;
        _overlay.Children.Add(shade);
    }

    /// <summary>
    /// Closes the card shown as Esc or a click beside it does: a card that is working (<see cref="IBusyCard"/>) stays,
    /// as its own Cancel is greyed meanwhile.
    /// </summary>
    /// <returns>True when no card is shown now.</returns>
    internal bool CloseUnlessBusy()
    {
        if (Shown is IBusyCard { Busy: true })
        {
            return false;
        }

        Close();
        return true;
    }

    /// <summary>Closes the card shown, if any.</summary>
    internal void Close()
    {
        if (_shade is not null)
        {
            _overlay.Children.Remove(_shade);

            // The card may be shown again (the palette is one card kept for good): it must be free of the old backdrop.
            _shade.Children.Clear();
        }

        _shade = null;
        Shown = null;
    }
}

/// <summary>A card that can be working (making something), when Esc and a click beside it must not close it.</summary>
internal interface IBusyCard
{
    /// <summary>Gets a value indicating whether the card is working.</summary>
    bool Busy { get; }
}
