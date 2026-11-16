// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace PropGenerator.Desktop.View;

/// <summary>
/// Makes a tool open and close its popup. A press on the tool while the popup is open dismisses the popup at once (it
/// is a press outside it), before or after the tool hears of the press; the click that follows must then not open it
/// again. Esc closes it.
/// </summary>
internal static class PopoverToggle
{
    // A popup closed this recently, by the same press, counts as open when the press reaches the tool.
    private const long SamePressMs = 150;

    /// <summary>Wires the tool's clicks to its popup.</summary>
    /// <param name="trigger">The tool.</param>
    /// <param name="popup">The popup it opens.</param>
    /// <param name="open">Opens it.</param>
    /// <param name="close">Closes it.</param>
    internal static void Wire(Button trigger, Popup popup, Action open, Action close)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(popup);
        bool pressedWhileOpen = false;
        long closedAt = long.MinValue / 2;
        popup.Closed += (_, _) => closedAt = Environment.TickCount64;
        trigger.AddHandler(
            InputElement.PointerPressedEvent,
            (_, _) => pressedWhileOpen = popup.IsOpen || Environment.TickCount64 - closedAt < SamePressMs,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        // Esc inside the popup closes it, as easy-props' popovers and menus do.
        popup.AddHandler(
            InputElement.KeyDownEvent,
            (_, e) =>
            {
                if (e.Key == Key.Escape && popup.IsOpen)
                {
                    close();
                    e.Handled = true;
                }
            },
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        trigger.Click += (_, _) =>
        {
            bool wasOpen = pressedWhileOpen || popup.IsOpen;
            pressedWhileOpen = false;
            if (wasOpen)
            {
                close();
            }
            else
            {
                open();
            }
        };
    }
}
