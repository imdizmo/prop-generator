using PropGenerator.Desktop.Pictures;
using PropGenerator.Desktop.Recipes;

namespace PropGenerator.Desktop.Props;

/// <summary>
/// Your props, as easy-props' library page has them: every work file in the library with its details, found by name,
/// kind or starting point, in one of three orders, and what can be done to one or several (copy, rename, remove into
/// Recently removed and put back). Opening and exporting go through the window, which knows the open piece.
/// </summary>
internal sealed class YourProps
{
    /// <summary>The longest name a prop can be given (easy-props' limit).</summary>
    internal const int MaxName = 60;

    private const int CacheSize = 512;

    private readonly Library _library;
    // Read and filled on the window's thread and by LoadAsync off it: used under its own lock.
    private readonly LruCache<string, (long Size, DateTime Written, PropSummary Summary)> _cache = new(CacheSize);

    /// <summary>Uses the library and its bin.</summary>
    /// <param name="library">The work files.</param>
    /// <param name="bin">Where removed props wait.</param>
    internal YourProps(Library library, RemovedBin bin)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(bin);
        _library = library;
        Bin = bin;
    }

    /// <summary>Gets Recently removed.</summary>
    internal RemovedBin Bin { get; }

    /// <summary>Gets or sets where the drawer's pictures are kept, so a copy or a new name keeps its picture; null for none.</summary>
    internal ThumbCache? Thumbs { get; set; }

    /// <summary>The kind a recipe makes, as the panel's header calls it.</summary>
    /// <param name="recipe">The recipe.</param>
    /// <returns>
    /// The label (easy-props' kinds by their own label while its engine is down); "Unknown kind (id)" for a kind this
    /// build does not know (a newer build's).
    /// </returns>
    internal static string KindOf(string recipe) =>
        recipe == FloorRecipe.Id ? "Floor tile" : MeshRecipes.LabelOf(recipe) ?? PropSummary.UnknownKind(recipe);

    /// <summary>Lists the props.</summary>
    /// <param name="search">Part of a name, a kind or a starting point's name, case not minded; null or blank for all.</param>
    /// <param name="sort">The order.</param>
    /// <param name="kind">Only this kind (a <see cref="PropSummary.KindLabel"/>); null for every kind.</param>
    /// <returns>The props.</returns>
    internal IReadOnlyList<PropSummary> List(string? search = null, PropSort sort = PropSort.LastChanged, string? kind = null)
    {
        string q = search?.Trim() ?? string.Empty;
        IEnumerable<PropSummary> all = _library.List().Select(e => Summary(e.Path, e.ColourHex)).OfType<PropSummary>();
        if (kind is not null)
        {
            all = all.Where(p => p.KindLabel == kind);
        }

        if (q.Length > 0)
        {
            all = all.Where(p => Has(p.Name, q) || Has(p.KindLabel, q) || (p.StartedFrom is { } from && Has(from, q)));
        }

        return sort switch
        {
            PropSort.Name => [.. all.OrderBy(p => p.Name, NaturalOrder.Instance)],
            PropSort.DateMade => [.. all.OrderByDescending(p => p.Made).ThenBy(p => p.Name, NaturalOrder.Instance)],
            _ => [.. all.OrderByDescending(p => p.Changed).ThenBy(p => p.Name, NaturalOrder.Instance)],
        };
    }

    /// <summary>
    /// Reads, off the window's thread, the details of every prop not read yet (or changed since), so a list made after
    /// it reads nothing on the window's thread.
    /// </summary>
    /// <returns>Done when they are read.</returns>
    internal Task LoadAsync() => Task.Run(() =>
    {
        foreach (LibraryEntry e in _library.List())
        {
            _ = Summary(e.Path, e.ColourHex);
        }
    });

    /// <summary>The kinds that have props, in the drawer's order.</summary>
    /// <returns>The kind labels.</returns>
    internal IReadOnlyList<string> Kinds()
    {
        string[] order = [.. ((IEnumerable<string>)["Floor tile", .. MeshRecipes.All.Select(r => r.Label), .. EngineKinds.All.Select(k => k.Label)]).Distinct(StringComparer.Ordinal)];
        HashSet<string> present = [.. List().Select(p => p.KindLabel)];
        return [.. order.Where(present.Contains), .. present.Where(k => !order.Contains(k)).Order(StringComparer.Ordinal)];
    }

    /// <summary>The summary of one prop, read again only when its file changed.</summary>
    /// <param name="path">The work file.</param>
    /// <returns>The summary, or null when the file is gone.</returns>
    internal PropSummary? Find(string path) => Summary(Path.GetFullPath(path), null);

    /// <summary>Copies a prop beside it as "&lt;name&gt; copy" (then " copy 2", " copy 3"…), with its picture, and lists it.</summary>
    /// <param name="path">The work file.</param>
    /// <returns>The copy's path.</returns>
    internal string Duplicate(string path)
    {
        string full = Path.GetFullPath(path);
        string folder = Path.GetDirectoryName(full)!;
        string name = Path.GetFileNameWithoutExtension(full) + " copy";
        string target = Path.Combine(folder, name + ".propgen");
        for (int n = 2; File.Exists(target); n++)
        {
            target = Path.Combine(folder, $"{name} {n}.propgen");
        }

        File.Copy(full, target);
        File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
        CopyPicture(full, target);
        _library.Add(target);
        return target;
    }

    /// <summary>
    /// Saves a new prop in a folder under a free name, as <see cref="Duplicate"/> picks one: "&lt;name&gt;", then
    /// "&lt;name&gt; 2", "&lt;name&gt; 3"…, never over a file already there. It is not listed yet (<see cref="Add"/>
    /// does that on the window's thread), so this may run off it.
    /// </summary>
    /// <param name="folder">Where it goes.</param>
    /// <param name="name">Its name, without .propgen.</param>
    /// <param name="piece">What the work file holds besides the settings.</param>
    /// <param name="settings">Its settings.</param>
    /// <param name="history">Its undo steps.</param>
    /// <param name="engine">The engine that made it, for an easy-props kind; else null.</param>
    /// <returns>The work file's full path.</returns>
    internal static string SaveNew(string folder, string name, Piece piece, Settings settings, History history, EngineStamp? engine)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(name);
        string full = Path.GetFullPath(folder);
        string target = Path.Combine(full, name + ".propgen");
        for (int n = 2; File.Exists(target) || Directory.Exists(target); n++)
        {
            target = Path.Combine(full, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{name} {n}.propgen"));
        }

        WorkFile.Save(target, piece, settings, history, engine);
        return target;
    }

    /// <summary>Lists a work file saved outside the window's own saves (a piece made from a tree).</summary>
    /// <param name="path">The work file.</param>
    internal void Add(string path) => _library.Add(path);

    /// <summary>
    /// Renames a prop in its folder (and in the library and the pictures). A blank name or the same name changes
    /// nothing; a name over 60 characters, with a mark a file name cannot have, or of another file there is refused.
    /// </summary>
    /// <param name="path">The work file.</param>
    /// <param name="newName">The new name, with or without .propgen.</param>
    /// <returns>The path it has now, or why it was not renamed.</returns>
    internal (string? Path, string? Error) Rename(string path, string newName)
    {
        string full = Path.GetFullPath(path);
        string? problem = NameProblem(newName, out string name);
        if (name.Length == 0 || name == Path.GetFileNameWithoutExtension(full))
        {
            return (full, null);
        }

        if (problem is not null)
        {
            return (null, problem);
        }

        string folder = Path.GetDirectoryName(full)!;
        string target = Path.Combine(folder, name + ".propgen");
        if (!string.Equals(target, full, StringComparison.OrdinalIgnoreCase) && (File.Exists(target) || Directory.Exists(target)))
        {
            return (null, $"There is already a prop called {name} in {folder}: the name was not changed.");
        }

        try
        {
            File.Move(full, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, $"{Path.GetFileName(full)} could not be renamed: {e.Message}");
        }

        _library.Rename(full, target);
        CopyPicture(full, target);
        return (Path.GetFullPath(target), null);
    }

    /// <summary>Says what is wrong with a name for a prop, if anything.</summary>
    /// <param name="newName">The name typed.</param>
    /// <param name="name">The name trimmed, without .propgen.</param>
    /// <returns>The problem, or null when the name can be used (or is blank).</returns>
    internal static string? NameProblem(string newName, out string name)
    {
        ArgumentNullException.ThrowIfNull(newName);
        name = newName.Trim();
        if (name.EndsWith(".propgen", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".propgen".Length].TrimEnd();
        }

        if (name.Length > MaxName)
        {
            return $"A name can be at most {MaxName} characters long: the name was not changed.";
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('\\', StringComparison.Ordinal))
        {
            return "A name cannot have / or \\ (or a few other marks) in it: the name was not changed.";
        }

        return null;
    }

    /// <summary>Moves props into Recently removed; the library forgets them.</summary>
    /// <param name="paths">The work files.</param>
    /// <returns>What the bin holds of them, to put back with Undo.</returns>
    internal IReadOnlyList<RemovedProp> Remove(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var gone = new List<RemovedProp>();
        foreach (string p in paths.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal))
        {
            gone.Add(Bin.Remove(p));
            _library.Forget(p);
        }

        return gone;
    }

    /// <summary>Puts removed props back where they were, into the library again.</summary>
    /// <param name="removed">The props.</param>
    /// <returns>Their paths.</returns>
    /// <exception cref="IOException">One could not go back (another file is in its place); those before it are back.</exception>
    internal IReadOnlyList<string> PutBack(IEnumerable<RemovedProp> removed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        var back = new List<string>();
        foreach (RemovedProp r in removed)
        {
            string path = Bin.PutBack(r.Id);
            _library.Add(path);
            back.Add(path);
        }

        return back;
    }

    private static bool Has(string text, string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static PropSummary Read(string path, FileInfo info, string? colourHex)
    {
        DateTimeOffset changed = new(info.LastWriteTimeUtc, TimeSpan.Zero);
        DateTimeOffset made = new(info.CreationTimeUtc, TimeSpan.Zero);
        if (made > changed || made.Year < 1980)
        {
            made = changed;
        }

        string name = Path.GetFileNameWithoutExtension(path);
        try
        {
            (Piece piece, Settings settings, History history) = WorkFile.Load(path);
            long? seed = settings.Values.TryGetValue("seed", out System.Text.Json.JsonElement s)
                && s.ValueKind == System.Text.Json.JsonValueKind.Number && s.TryGetDouble(out double d) ? (long)d : null;
            string? from = history.Steps.Count > 0 && history.Steps[0].Label != "Opened" ? history.Steps[0].Label : null;
            return new PropSummary(path, name, piece.Recipe, KindOf(piece.Recipe), from, seed, made, changed, info.Length, colourHex);
        }
        catch (Exception e) when (e is WorkFileException or IOException or UnauthorizedAccessException)
        {
            return new PropSummary(path, name, null, PropSummary.Unreadable, null, null, made, changed, info.Length, colourHex);
        }
    }

    private PropSummary? Summary(string path, string? colourHex)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return null;
        }

        lock (_cache)
        {
            if (_cache.TryGet(path, out var kept) && kept.Size == info.Length && kept.Written == info.LastWriteTimeUtc)
            {
                return kept.Summary;
            }
        }

        PropSummary read = Read(path, info, colourHex);
        lock (_cache)
        {
            _cache.Put(path, (info.Length, info.LastWriteTimeUtc, read));
        }

        return read;
    }

    private void CopyPicture(string from, string to)
    {
        try
        {
            if (Thumbs?.Get(ThumbCache.FileKey(from)) is byte[] png)
            {
                Thumbs.Put(ThumbCache.FileKey(Path.GetFullPath(to)), png);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TraceLog.Write($"no picture kept of {Path.GetFileName(to)}: {e.Message}");
        }
    }
}
