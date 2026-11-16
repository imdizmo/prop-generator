using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace PropGenerator.Desktop.Look;

/// <summary>
/// The app's names for the shared Jatify Tools tokens (<see cref="Tokens"/>, design v2): every colour, size and radius
/// here is a token, under the name the views already use. New code may take <see cref="Tokens"/> directly.
/// </summary>
public static class Theme
{
    /// <summary>The window behind everything, and the 3D view's surround (Bg).</summary>
    public static readonly IBrush SurfacePage = Tokens.Bg;

    /// <summary>Docked panels: the top bar, the settings panel, the status bar (Panel).</summary>
    public static readonly IBrush SurfacePanel = Tokens.Panel;

    /// <summary>Input wells: fields and key chips (Bg).</summary>
    public static readonly IBrush SurfaceSunken = Tokens.Bg;

    /// <summary>Cards, buttons, menus, tooltips and dialogs (Raised).</summary>
    public static readonly IBrush SurfaceRaised = Tokens.Raised;

    /// <summary>A raised control under the pointer, and nothing else (Raised2).</summary>
    public static readonly IBrush SurfaceRaised2 = Tokens.Raised2;

    /// <summary>A flat control under the pointer (Hover).</summary>
    public static readonly IBrush SurfaceHover = Tokens.Hover;

    /// <summary>A pressed control, or a menu that is open (Active).</summary>
    public static readonly IBrush SurfaceActive = Tokens.Active;

    /// <summary>A selected row, the chosen tool, a toggled view button (Selected: the accent at 17%).</summary>
    public static readonly IBrush SurfaceSelected = Tokens.Selected;

    /// <summary>Hairlines between areas, control borders at rest (Line).</summary>
    public static readonly IBrush BorderSubtle = Tokens.Line;

    /// <summary>Hover borders, popover borders, slider tracks (LineStrong).</summary>
    public static readonly IBrush BorderStrong = Tokens.LineStrong;

    /// <summary>Main text (Text).</summary>
    public static readonly IBrush TextPrimary = Tokens.Text;

    /// <summary>Secondary text, labels, resting icons (Dim).</summary>
    public static readonly IBrush TextSecondary = Tokens.Dim;

    /// <summary>Tertiary text: counts, meta, captions, placeholders (Mute).</summary>
    public static readonly IBrush TextTertiary = Tokens.Mute;

    /// <summary>Disabled text and key hints, decorative only (Faint).</summary>
    public static readonly IBrush TextFaint = Tokens.Faint;

    /// <summary>The Jatify orange: the primary button, focus, selection, the chosen state (Accent).</summary>
    public static readonly IBrush Accent = Tokens.Accent;

    /// <summary>The accent under the pointer (AccentHover).</summary>
    public static readonly IBrush AccentHover = Tokens.AccentHover;

    /// <summary>The accent pressed (AccentPressed).</summary>
    public static readonly IBrush AccentPressed = Tokens.AccentPressed;

    /// <summary>Icons and text in the accent colour: a chosen tool's icon (Accent).</summary>
    public static readonly IBrush AccentText = Tokens.Accent;

    /// <summary>A wash of the accent behind a chosen segment or chip (AccentSoft).</summary>
    public static readonly IBrush AccentSoft = Tokens.AccentSoft;

    /// <summary>The border of a chosen segment or chip (AccentLine).</summary>
    public static readonly IBrush AccentLine = Tokens.AccentLine;

    /// <summary>Text and icons on the accent: a primary button's label (AccentInk).</summary>
    public static readonly IBrush AccentInk = Tokens.AccentInk;

    /// <summary>The orange of a selection and of "unsaved" (Accent).</summary>
    public static readonly IBrush Selection = Tokens.Accent;

    /// <summary>Error text, icons and borders, destructive menu items (Danger, the text red).</summary>
    public static readonly IBrush Danger = Tokens.Danger;

    /// <summary>A soft red wash (DangerSoft).</summary>
    public static readonly IBrush DangerSoft = Tokens.DangerSoft;

    /// <summary>A filled destructive button, white text on it (DangerFill).</summary>
    public static readonly IBrush DangerFill = Tokens.DangerFill;

    /// <summary>A filled destructive button under the pointer (DangerFillHover).</summary>
    public static readonly IBrush DangerFillHover = Tokens.DangerFillHover;

    /// <summary>Needs a look, not broken (Warning).</summary>
    public static readonly IBrush Warning = Tokens.Warning;

    /// <summary>The wash of a warning chip (WarningSoft).</summary>
    public static readonly IBrush WarningSoft = Tokens.WarningSoft;

    /// <summary>
    /// The border of the button inside a warning chip: the warning colour at 45%, as the design's status bar chip has it
    /// (no token of its own; made from <see cref="Tokens.Warning"/>).
    /// </summary>
    public static readonly IBrush WarningLine = WarningAt(0.45);

    /// <summary>Done, running, live (Success).</summary>
    public static readonly IBrush Success = Tokens.Success;

    /// <summary>The outer ring of the keyboard focus ring (the panel colour, 2 px).</summary>
    public static readonly IBrush FocusRingInner = Tokens.Panel;

    /// <summary>The keyboard focus ring (Focus, 2 px).</summary>
    public static readonly IBrush FocusRing = Tokens.Focus;

    /// <summary>The scrollbar thumb (ScrollThumb).</summary>
    public static readonly IBrush ScrollbarThumb = Tokens.ScrollThumb;

    /// <summary>The scrollbar thumb under the pointer (ScrollThumbHover).</summary>
    public static readonly IBrush ScrollbarThumbHover = Tokens.ScrollThumbHover;

    /// <summary>Chrome floating over the 3D view: toolbars and cards (Overlay).</summary>
    public static readonly IBrush OverlayBg = Tokens.Overlay;

    /// <summary>The border of chrome floating over the 3D view (LineOverlay).</summary>
    public static readonly IBrush OverlayBorder = Tokens.LineOverlay;

    /// <summary>The text of chrome drawn over the 3D view (Text).</summary>
    public static readonly IBrush OverlayText = Tokens.Text;

    /// <summary>The quieter text and resting icons of chrome drawn over the 3D view (Dim).</summary>
    public static readonly IBrush OverlayMuted = Tokens.Dim;

    /// <summary>A tool over the 3D view under the pointer (Hover).</summary>
    public static readonly IBrush ToolHover = Tokens.Hover;

    /// <summary>A tool over the 3D view that is on: the chosen tool, a toggled view button (Selected, its icon in the accent).</summary>
    public static readonly IBrush ToolPressed = Tokens.Selected;

    /// <summary>The icon and label of a tool over the 3D view that cannot be used yet (Faint).</summary>
    public static readonly IBrush ToolUnavailable = Tokens.Faint;

    /// <summary>The shadow of a card floating over the 3D view (Shadow1).</summary>
    public static BoxShadows Shadow1 => Tokens.Shadow1;

    /// <summary>The shadow of a menu, popover, tooltip or toast (Shadow2).</summary>
    public static BoxShadows Shadow2 => Tokens.Shadow2;

    /// <summary>The shadow of a dialog (Shadow3).</summary>
    public static BoxShadows Shadow3 => Tokens.Shadow3;

    /// <summary>Gets the font all text is set in: Space Grotesk, jatify.com's typeface (SIL Open Font License; see NOTICE.md).</summary>
    public static FontFamily Font { get; } = new("avares://PropGenerator/Assets/Fonts#Space Grotesk");

    /// <summary>Spacing step 1 (S1).</summary>
    public const double Space1 = Tokens.S1;

    /// <summary>Spacing step 2 (S2).</summary>
    public const double Space2 = Tokens.S2;

    /// <summary>Spacing step 3 (S3).</summary>
    public const double Space3 = Tokens.S3;

    /// <summary>Spacing step 4 (S4).</summary>
    public const double Space4 = Tokens.S4;

    /// <summary>Spacing step 5 (S5).</summary>
    public const double Space5 = Tokens.S5;

    /// <summary>Spacing step 6 (S6).</summary>
    public const double Space6 = Tokens.S6;

    /// <summary>Check boxes, key chips, list rows, menu items, small icon buttons (RXs).</summary>
    public const double Radius1 = Tokens.RXs;

    /// <summary>Buttons, fields, selects, tooltips (RSm).</summary>
    public const double Radius2 = Tokens.RSm;

    /// <summary>Menus, popovers, small cards over the view (RMd).</summary>
    public const double Radius3 = Tokens.RMd;

    /// <summary>Dialogs and the cards shown over the window like dialogs (RXl).</summary>
    public const double Radius4 = Tokens.RXl;

    /// <summary>Cards and toolbars floating over the 3D view (RLg).</summary>
    public const double RadiusCard = Tokens.RLg;

    /// <summary>A radius that makes any control a pill: chips, segmented controls, badges, switches (RPill).</summary>
    public const double RadiusPill = Tokens.RPill;

    /// <summary>Height of the top bar (TopBar).</summary>
    public const double MenuBarHeight = Tokens.TopBar;

    /// <summary>Height of the status bar (StatusBar).</summary>
    public const double StatusBarHeight = Tokens.StatusBar;

    /// <summary>Height of a row of the controls panel (Row).</summary>
    public const double RowHeight = Tokens.Row;

    /// <summary>Height of a button, field or tool (Control).</summary>
    public const double ControlHeight = Tokens.Control;

    /// <summary>Height of a small control: a value field in a dense row, the status bar's controls (ControlSm).</summary>
    public const double ControlSmallHeight = Tokens.ControlSm;

    /// <summary>
    /// Height of a menu's title in the top bar and of a menu's item: 26, as the design's top bar and context menu
    /// components give them (between ControlSm and Control; no token of its own).
    /// </summary>
    public const double MenuItemHeight = 26;

    /// <summary>Height of an inspector section header: a settings group's title (Tab, 32).</summary>
    public const double SectionHeight = Tokens.Tab;

    /// <summary>The narrowest the label column of a settings row gets (LabelMin).</summary>
    public const double LabelMin = Tokens.LabelMin;

    /// <summary>The narrowest the settings panel gets (PanelMin).</summary>
    public const double PanelMin = Tokens.PanelMin;

    /// <summary>The widest the settings panel gets (PanelMax).</summary>
    public const double PanelMax = Tokens.PanelMax;

    /// <summary>Body text size (FsBody).</summary>
    public const double Body = Tokens.FsBody;

    /// <summary>Label text size: field labels, the status bar, tooltips, small buttons (FsLabel).</summary>
    public const double Label = Tokens.FsLabel;

    /// <summary>Caption text size (FsCaption).</summary>
    public const double Caption = Tokens.FsCaption;

    /// <summary>Key chip text size (FsCaps).</summary>
    public const double Keys = Tokens.FsCaps;

    /// <summary>Title text size: the settings panel's title (FsTitle).</summary>
    public const double Title = Tokens.FsTitle;

    /// <summary>The longest a tooltip gets before it wraps.</summary>
    public const double TooltipMaxWidth = 260;

    /// <summary>How long the pointer rests before a tooltip shows, in milliseconds (TooltipDelayMs).</summary>
    public const int TooltipDelayMs = Tokens.TooltipDelayMs;

    // The warning colour at this opacity.
    private static ImmutableSolidColorBrush WarningAt(double alpha)
    {
        Color c = ((ISolidColorBrush)Tokens.Warning).Color;
        return new(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));
    }
}
