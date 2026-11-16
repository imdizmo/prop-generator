// Ported from easy-props frontend/src/core/export/pipeline/types.ts (ExportLook) (MIT, © 2026 AHS12).
namespace PropGenerator.Nature.Export;

/// <summary>
/// How the game will draw the prop. Lit is the usual look. Cartoon and Comic need the engine kit's toon materials, and
/// Comic (easy-props' <c>cel</c>) also bakes its flat colours and round leaf light into the file.
/// </summary>
public static class ExportLook
{
    /// <summary>The usual look.</summary>
    public const string Lit = "lit";

    /// <summary>Soft bands of light and faint lines; the file is the same as Lit.</summary>
    public const string Cartoon = "cartoon";

    /// <summary>The Comic look: flat colours, hard shadows and ink lines.</summary>
    public const string Cel = "cel";

    /// <summary>Gets every look, in easy-props' order.</summary>
    public static IReadOnlyList<string> All { get; } = [Lit, Cartoon, Cel];
}
