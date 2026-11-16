// Structure adapted from easy-props (MIT, © 2026 AHS12); every colour, size and radius is a shared token.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;

namespace PropGenerator.Desktop.Look;

/// <summary>
/// The application-wide styles of the look, from the shared tokens (<see cref="Tokens"/>): Fluent's accent ramp and
/// resource keys, tooltips, the scrollbar thumb, the <see cref="Chrome"/> buttons, the tools over the 3D view and the
/// segmented control.
/// </summary>
public static class LookStyles
{
    // Fluent's accent ramp, as the design's resource table gives it (Groundwork's ramp): light steps for hover, dark
    // steps for pressed. The accent itself is Tokens.AccentColor.
    private static readonly (string Key, string Hex)[] AccentRamp =
    [
        ("SystemAccentColorLight1", "#F0953E"),
        ("SystemAccentColorLight2", "#F4A85C"),
        ("SystemAccentColorLight3", "#F8C08A"),
        ("SystemAccentColorDark1", "#D27214"),
        ("SystemAccentColorDark2", "#B05F10"),
        ("SystemAccentColorDark3", "#7A4410"),
    ];

    // Raised2 with the Active wash (white at 11%) over it, opaque: a plain button pressed.
    private static readonly IBrush RaisedPressed = new ImmutableSolidColorBrush(Color.Parse("#3B3B40"));

    // Selected over Hover, opaque, as the design's resource table gives it: a selected row under the pointer.
    private static readonly IBrush SelectedHover = new ImmutableSolidColorBrush(Color.Parse("#4A301A"));

    // The second pixel of a focused field's 2 px accent border, drawn just outside its 1 px border.
    private static readonly BoxShadows FocusShadow = new(new BoxShadow { Spread = 1, Color = Tokens.AccentColor });

    // One token under several Fluent keys.
    private static void Set(Application app, object value, params string[] keys)
    {
        foreach (string key in keys)
        {
            app.Resources[key] = value;
        }
    }

    /// <summary>Adds the look's styles and resource overrides to the application.</summary>
    /// <param name="app">The application.</param>
    public static void Apply(Application app)
    {
        // The Jatify look: the orange accent for Fluent (switches, check boxes, sliders, selections), Space Grotesk for
        // all text, and Fluent's keys set from the tokens.
        app.Resources["SystemAccentColor"] = Tokens.AccentColor;
        foreach ((string key, string hex) in AccentRamp)
        {
            app.Resources[key] = Color.Parse(hex);
        }

        app.Resources["ContentControlThemeFontFamily"] = Theme.Font;
        app.Resources["ControlCornerRadius"] = new CornerRadius(Tokens.RSm);
        app.Resources["OverlayCornerRadius"] = new CornerRadius(Tokens.RMd);
        app.Resources["ScrollBarThumbFill"] = Tokens.ScrollThumb;
        app.Resources["ScrollBarThumbFillPointerOver"] = Tokens.ScrollThumbHover;
        app.Resources["ScrollBarThumbFillPressed"] = Tokens.ScrollThumbHover;
        app.Resources["ScrollBarTrackFill"] = Brushes.Transparent;
        app.Resources["ToolTipBackground"] = Tokens.Raised;
        app.Resources["ToolTipBorderBrush"] = Tokens.LineStrong;
        app.Resources["ToolTipForeground"] = Tokens.Text;
        app.Resources["TextControlPlaceholderForeground"] = Tokens.Mute;
        app.Resources["TextControlPlaceholderForegroundPointerOver"] = Tokens.Mute;
        app.Resources["TextControlPlaceholderForegroundFocused"] = Tokens.Mute;
        app.Resources["ComboBoxDropDownBackground"] = Tokens.Raised;
        app.Resources["ComboBoxDropDownBorderBrush"] = Tokens.LineStrong;
        app.Resources["ComboBoxItemBackgroundSelected"] = Tokens.Selected;
        app.Resources["ComboBoxItemBackgroundPointerOver"] = Tokens.Hover;
        app.Resources["MenuFlyoutPresenterBackground"] = Tokens.Raised;
        app.Resources["MenuFlyoutPresenterBorderBrush"] = Tokens.LineStrong;
        app.Resources["MenuFlyoutItemBackgroundPointerOver"] = Tokens.Hover;
        app.Resources["SliderTrackFill"] = Tokens.LineStrong;
        app.Resources["SliderTrackValueFill"] = Tokens.Accent;
        Set(app, Tokens.Text, "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed");

        // Text fields are wells (Bg, a Line border; LineStrong under the pointer, the accent when focused, plus a 1 px
        // accent shadow below: 2 px in all). Plain buttons are secondary (Raised, a Line border; Raised2 and LineStrong
        // under the pointer; pressed adds the Active wash: Raised2 + Active, opaque).
        Set(app, Tokens.Bg, "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused");
        app.Resources["TextControlBorderBrush"] = Tokens.Line;
        app.Resources["TextControlBorderBrushPointerOver"] = Tokens.LineStrong;
        app.Resources["TextControlBorderBrushFocused"] = Tokens.Accent;
        Set(app, Tokens.Raised, "ButtonBackground", "ButtonBackgroundDisabled", "ComboBoxBackground", "ComboBoxBackgroundDisabled", "ComboBoxBackgroundUnfocused");
        Set(app, Tokens.Raised2, "ButtonBackgroundPointerOver", "ComboBoxBackgroundPointerOver");
        Set(app, RaisedPressed, "ButtonBackgroundPressed", "ComboBoxBackgroundPressed");
        Set(app, Tokens.Line, "ButtonBorderBrush", "ButtonBorderBrushDisabled", "ComboBoxBorderBrush", "ComboBoxBorderBrushDisabled");
        Set(app, Tokens.LineStrong, "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed");

        // Lists: the selected row on the Selected wash (opaque Selected-over-Hover under the pointer), Hover under it.
        Set(app, Tokens.Selected, "ListBoxItemBackgroundSelected", "SystemControlHighlightListAccentLowBrush");
        Set(app, SelectedHover, "ListBoxItemBackgroundSelectedPointerOver", "ComboBoxItemBackgroundSelectedPointerOver", "SystemControlHighlightListAccentMediumBrush");
        Set(app, Tokens.Hover, "ListBoxItemBackgroundPointerOver", "SystemControlHighlightListLowBrush");

        // Check boxes: the accent with an ink tick when checked (lighter under the pointer, darker pressed).
        Set(app, Tokens.Accent, "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundStrokeChecked");
        Set(app, Tokens.AccentHover, "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundStrokeCheckedPointerOver");
        Set(app, Tokens.AccentPressed, "CheckBoxCheckBackgroundFillCheckedPressed", "CheckBoxCheckBackgroundStrokeCheckedPressed");
        Set(app, Tokens.AccentInk, "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed");
        app.Resources["ScrollBarSize"] = 10.0;

        // Keyboard focus: a 2 px ring in the focus colour, 2 px outside the control, the gap in the panel colour (Fluent
        // draws its focus adorner from these keys; on keyboard focus only).
        app.Resources["SystemControlFocusVisualPrimaryBrush"] = Theme.FocusRing;
        app.Resources["SystemControlFocusVisualSecondaryBrush"] = Theme.FocusRingInner;
        app.Resources["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2);
        app.Resources["SystemControlFocusVisualSecondaryThickness"] = new Thickness(2);
        app.Resources["SystemControlFocusVisualMargin"] = new Thickness(-4);
        app.Styles.Add(new Style(x => x.OfType<TextBox>().Class(":focus").Template().OfType<Border>().Name("PART_BorderElement"))
        {
            Setters = { new Setter(Border.BoxShadowProperty, FocusShadow) },
        });

        app.Styles.Add(new Style(x => x.OfType<ToolTip>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, Theme.SurfaceRaised),
                new Setter(TemplatedControl.BorderBrushProperty, Theme.BorderStrong),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(Theme.Radius2)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(8, 5)),
                new Setter(TemplatedControl.ForegroundProperty, Theme.TextPrimary),
                new Setter(TemplatedControl.FontSizeProperty, Theme.Label),
                new Setter(Layoutable.MaxWidthProperty, Theme.TooltipMaxWidth),
            },
        });

        // Ghost (no fill, quiet text, a wash under the pointer), secondary (raised, a hairline; raised-2 and a strong
        // line under the pointer) and primary (the accent with ink; lighter under the pointer, darker pressed).
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 0, Theme.SurfaceHover, Theme.TextPrimary));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-raised"), Theme.SurfaceRaised, Theme.TextPrimary, Theme.BorderSubtle, 1, Theme.SurfaceRaised2, Theme.TextPrimary, Theme.BorderStrong));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-primary"), Theme.Accent, Theme.AccentInk, Theme.Accent, 1, Theme.AccentHover, Theme.AccentInk, Theme.AccentHover));
        app.Styles.Add(Pressed(r => r.OfType<Button>().Class("ep").Class("ep-primary"), Theme.AccentPressed));

        // The top bar: an open menu's title, the menu rows, the prop name and the command search (a well).
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-open"), Theme.SurfaceActive, Theme.TextPrimary, Brushes.Transparent, 0, Theme.SurfaceActive, Theme.TextPrimary));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-menu-item"), Brushes.Transparent, Theme.TextPrimary, Brushes.Transparent, 0, Theme.SurfaceHover, Theme.TextPrimary, radius: Theme.Radius1));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-name"), Brushes.Transparent, Theme.TextPrimary, Brushes.Transparent, 1, Brushes.Transparent, Theme.TextPrimary, Theme.BorderStrong));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-palette"), Theme.SurfaceSunken, Theme.TextTertiary, Theme.BorderSubtle, 1, Theme.SurfaceSunken, Theme.TextSecondary, Theme.BorderStrong));

        // The controls panel: a group's header, the filter's clear button, Surprise me, and Restore to preset.
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-group"), Brushes.Transparent, Theme.TextPrimary, Brushes.Transparent, 0, Theme.SurfaceHover, Theme.TextPrimary, radius: 0));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-clear"), Brushes.Transparent, Theme.TextTertiary, Brushes.Transparent, 0, Theme.SurfaceActive, Theme.TextPrimary, radius: Theme.RadiusPill));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-outline"), Brushes.Transparent, Theme.TextSecondary, Theme.BorderSubtle, 1, Brushes.Transparent, Theme.TextPrimary, Theme.BorderStrong));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-text"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 0, Theme.SurfaceHover, Theme.TextPrimary, radius: Theme.Radius1));

        // The one action inside a status bar warning chip (Make it fit): a small pill outlined in the warning colour.
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-chip-action"), Brushes.Transparent, Theme.Warning, Theme.WarningLine, 1, Theme.WarningSoft, Theme.Warning, radius: Theme.RadiusPill));

        // The tools over the 3D view: ghost buttons; the chosen tool and a toggled view button on the selected wash with
        // the icon (and label) in the accent; a tool that cannot be used yet faint. Then the sun popover's times of day
        // as chips (the chosen one on the selected wash with the accent line).
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-tool"), Brushes.Transparent, Theme.OverlayMuted, Brushes.Transparent, 0, Theme.ToolHover, Theme.OverlayText));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-tool").Class("ep-pressed"), Theme.ToolPressed, Theme.AccentText, Brushes.Transparent, 0, Theme.ToolPressed, Theme.AccentText));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-tool").Class("ep-unavailable"), Brushes.Transparent, Theme.ToolUnavailable, Brushes.Transparent, 0, Brushes.Transparent, Theme.ToolUnavailable));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-time"), Brushes.Transparent, Theme.TextSecondary, Theme.BorderSubtle, 1, Brushes.Transparent, Theme.TextPrimary, Theme.BorderStrong, Theme.RadiusPill));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-time").Class("ep-pressed"), Theme.SurfaceSelected, Theme.TextPrimary, Theme.AccentLine, 1, Theme.SurfaceSelected, Theme.TextPrimary, Theme.AccentLine, Theme.RadiusPill));

        // The segmented control (Normal / Low-poly): pills, the chosen one on the accent wash with the accent line and
        // accent text in semibold.
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-seg"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 1, Theme.SurfaceHover, Theme.TextPrimary, radius: Theme.RadiusPill));
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-seg").Class("ep-pressed"), Theme.AccentSoft, Theme.AccentText, Theme.AccentLine, 1, Theme.AccentSoft, Theme.AccentText, Theme.AccentLine, Theme.RadiusPill));
        app.Styles.Add(new Style(x => x.OfType<Button>().Class("ep-seg").Class("ep-pressed").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.FontWeightProperty, FontWeight.SemiBold) },
        });

        // The drawer's tabs and its Objects toggle: quiet as a menu's title, the chosen tab (and the open drawer's
        // toggle) on the active wash in the primary text colour, as an open menu's title. No accent colour.
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-toggle"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 0, Theme.SurfaceActive, Theme.TextPrimary));
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-toggle").Class(":checked"), Theme.SurfaceActive, Theme.TextPrimary, Brushes.Transparent, 0, Theme.SurfaceActive, Theme.TextPrimary));

        // The open Objects toggle is selected: the selected wash with the accent text.
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-objects").Class(":checked"), Theme.SurfaceSelected, Theme.AccentText, Brushes.Transparent, 0, Theme.SurfaceSelected, Theme.AccentText));

        // Underline tabs (the drawer's kinds, and More): 13 in the dim text, the text colour under the pointer; the chosen
        // one in the text colour, semibold, its accent underline drawn by the tab itself (TabRow). No wash.
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-tab"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 0, Brushes.Transparent, Theme.TextPrimary, radius: Theme.Radius1));
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-tab").Class(":checked"), Brushes.Transparent, Theme.TextPrimary, Brushes.Transparent, 0, Brushes.Transparent, Theme.TextPrimary, radius: Theme.Radius1));
        app.Styles.Add(Pressed(r => r.OfType<ToggleButton>().Class("ep-tab"), Brushes.Transparent));
        app.Styles.Add(new Style(x => x.OfType<ToggleButton>().Class("ep-tab").Class(":checked").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.FontWeightProperty, FontWeight.SemiBold) },
        });
        app.Styles.Add(ButtonLook(r => r.OfType<Button>().Class("ep").Class("ep-tab-more"), Brushes.Transparent, Theme.TextSecondary, Brushes.Transparent, 0, Brushes.Transparent, Theme.TextPrimary, radius: Theme.Radius1));

        // Chips (the drawer's groups under a kind): pills with a hairline; the chosen one on the selected wash with the
        // accent line.
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-chip"), Brushes.Transparent, Theme.TextSecondary, Theme.BorderSubtle, 1, Brushes.Transparent, Theme.TextPrimary, Theme.BorderStrong, Theme.RadiusPill));
        app.Styles.Add(ButtonLook(r => r.OfType<ToggleButton>().Class("ep-chip").Class(":checked"), Theme.SurfaceSelected, Theme.TextPrimary, Theme.AccentLine, 1, Theme.SurfaceSelected, Theme.TextPrimary, Theme.AccentLine, Theme.RadiusPill));
        app.Styles.Add(Pressed(r => r.OfType<ToggleButton>().Class("ep-chip"), Theme.SurfaceSelected));
    }

    // A button's face while pressed.
    private static Style Pressed(Func<Selector?, Selector> button, IBrush back) =>
        new(x => button(x).Class(":pressed").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, back),
                new Setter(ContentPresenter.BorderBrushProperty, back),
            },
        };

    // One button look: resting colours, and the colours while the pointer is over it (the border too, when given).
    private static Styles ButtonLook(
        Func<Selector?, Selector> button, IBrush back, IBrush fore, IBrush border, double borderWidth, IBrush hoverBack, IBrush hoverFore,
        IBrush? hoverBorder = null, double radius = Theme.Radius2)
    {
        var rest = new Style(x => button(x).Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, back),
                new Setter(ContentPresenter.ForegroundProperty, fore),
                new Setter(ContentPresenter.BorderBrushProperty, border),
                new Setter(ContentPresenter.BorderThicknessProperty, new Thickness(borderWidth)),
                new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(radius)),
            },
        };
        var hover = new Style(x => button(x).Class(":pointerover").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, hoverBack),
                new Setter(ContentPresenter.ForegroundProperty, hoverFore),
                new Setter(ContentPresenter.BorderBrushProperty, hoverBorder ?? border),
            },
        };
        return [rest, hover];
    }
}
