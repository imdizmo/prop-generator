namespace PropGenerator.Desktop.Backups;

/// <summary>What a restore did.</summary>
/// <param name="Added">Props that were missing and are back where they were (or in the restored folder).</param>
/// <param name="Same">Props already here exactly as in the backup, left alone.</param>
/// <param name="Copies">Props here with other contents: the backup's comes back beside it as "&lt;name&gt; (from backup)".</param>
/// <param name="Refused">Entries that were not let in (outside props/, not a work file, too big).</param>
/// <param name="Extras">The backup's other files (colours, palettes, presets), by name, for their owners to merge.</param>
internal sealed record BackupRestore(int Added, int Same, int Copies, int Refused, IReadOnlyDictionary<string, byte[]> Extras);
