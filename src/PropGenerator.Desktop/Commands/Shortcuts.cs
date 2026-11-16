// Adapted from easy-props (MIT, © 2026 AHS12).
using Avalonia.Input;

namespace PropGenerator.Desktop.Commands;

/// <summary>
/// The keyboard shortcuts, read from the command list's keys: a key pressed runs the command shown with it. As in
/// easy-props, no shortcut runs while a text box has the focus (the box gets the key, Ctrl+Z included), except the
/// palette's. Esc and Delete, which other controls use too (a drop-down closes on Esc), are left to them while their
/// command is greyed; any other greyed shortcut says why on the status line.
/// </summary>
internal sealed class Shortcuts
{
    // Keys shown on more than one item or on none: Ctrl+G is the drawer's (My props shows it too), Ctrl+Y the old redo,
    // Ctrl+Shift+P easy-props' other key for the palette.
    private static readonly Dictionary<string, string> Extra = new(StringComparer.Ordinal)
    {
        ["Ctrl+G"] = "drawer",
        ["Ctrl+Y"] = "redo",
        ["Ctrl+Shift+P"] = "palette",
    };

    // Keys other controls use for themselves: a greyed command leaves them alone.
    private static readonly string[] SharedKeys = ["Esc", "Delete"];

    private readonly Dictionary<string, Command> _byKeys = new(StringComparer.Ordinal);

    /// <summary>Reads the keys of every command and of the commands in sub-lists.</summary>
    /// <param name="commands">The command list.</param>
    internal Shortcuts(IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (Command c in commands)
        {
            if (c.Keys is string keys)
            {
                _byKeys.TryAdd(keys, c);
            }
        }

        foreach ((string keys, string id) in Extra)
        {
            _byKeys[keys] = CommandList.Find(commands, id) ?? throw new ArgumentException($"There is no command '{id}'.", nameof(commands));
        }
    }

    /// <summary>The command these keys run, if any.</summary>
    /// <param name="keys">The keys as written, such as "Ctrl+Shift+S".</param>
    /// <returns>The command, or null.</returns>
    internal Command? For(string keys) => _byKeys.GetValueOrDefault(keys);

    /// <summary>
    /// Runs the command of a key press, if it has one. While typing only the palette's keys run; a greyed command on
    /// Esc or Delete is left to the control that has the focus.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held.</param>
    /// <param name="typing">True while a text box has the focus.</param>
    /// <returns>True when a command was run (the key is then handled).</returns>
    internal bool TryRun(Key key, KeyModifiers modifiers, bool typing)
    {
        string? keys = KeysOf(key, modifiers);
        if (keys is null || For(keys) is not Command c || (typing && c.Id != "palette")
            || (SharedKeys.Contains(keys) && c.Unavailable() is not null))
        {
            return false;
        }

        _ = c.Run();
        return true;
    }

    /// <summary>
    /// Writes a key press the way the shortcuts are written: "Ctrl+", "Alt+" and, for letters, "Shift+", then the key
    /// ("S", "1", "Space", "Esc", "Delete", ",", "/", "\").
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held.</param>
    /// <returns>The keys, or null for a key no shortcut uses.</returns>
    internal static string? KeysOf(Key key, KeyModifiers modifiers)
    {
        string? name = key switch
        {
            >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
            Key.Space => "Space",
            Key.Escape => "Esc",
            Key.Delete => "Delete",
            Key.OemComma => ",",
            Key.OemQuestion or Key.Divide => "/",
            Key.OemPipe or Key.OemBackslash => "\\",
            _ => null,
        };
        if (name is null)
        {
            return null;
        }

        string text = string.Empty;
        if (modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta))
        {
            text += "Ctrl+";
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            text += "Alt+";
        }

        if (modifiers.HasFlag(KeyModifiers.Shift) && key is >= Key.A and <= Key.Z)
        {
            text += "Shift+";
        }

        return text + name;
    }
}
