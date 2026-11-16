namespace PropGenerator.Desktop.Recipes;

/// <summary>
/// How the panel shows one setting for the values it has now, when that differs by values (easy-props' <c>shown()</c>:
/// a palm-form painted tree shows its branch rows under Leaves).
/// </summary>
/// <param name="Key">The setting's key.</param>
/// <param name="Label">What its row is called now.</param>
/// <param name="Group">The group its row sits in now; null keeps the setting's own group.</param>
internal sealed record ShownRow(string Key, string Label, string? Group);
