using System.Text.Json;
using System.Text.Json.Nodes;
using PropGenerator.Desktop.Commands;

namespace PropGenerator.Desktop;

/// <summary>
/// The app's own small memory: the last Groundwork map folder exported to, the last folder a work file was saved or
/// opened in, Claude's connection, the objects drawer (open or closed, its side and sizes, its tab, the favourites and the recent starting
/// points), the view (scene, light and face budget) and the settings panel (its width, whether it is open, and which
/// groups are open for each kind of piece). Kept in one JSON file (normally ~/.config/prop-generator/prefs.json). A missing or broken file reads as
/// nothing remembered; a failed write is not an error (the next start just asks again).
/// </summary>
public sealed class Prefs
{
    private const UnixFileMode PrivateFolder = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Reads the file if there is one.</summary>
    /// <param name="path">The prefs file.</param>
    public Prefs(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        FilePath = path;
        Tighten();
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject o)
            {
                MapFolder = Text(o, "mapFolder");
                SaveFolder = Text(o, "saveFolder");
                SetFolder = Text(o, "setFolder");
                ClaudeEnabled = o["claudeEnabled"] is JsonValue e && e.TryGetValue(out bool on) && on;
                ClaudeToken = Text(o, "claudeToken");
                if (o["claudePort"] is JsonValue p && p.TryGetValue(out int port) && port is >= 1024 and <= 65535)
                {
                    ClaudePort = port;
                }

                if (o["drawerOpen"] is JsonValue d && d.TryGetValue(out bool open))
                {
                    DrawerOpen = open;
                }

                DrawerTab = Text(o, "drawerTab") ?? DrawerTab;
                DrawerLeft = Text(o, "drawerSide") == "left";
                if (o["drawerHeight"] is JsonValue dh && dh.TryGetValue(out double tall) && double.IsFinite(tall))
                {
                    DrawerHeight = Math.Clamp(tall, MinDrawerHeight, MaxDrawerHeight);
                }

                if (o["drawerWidth"] is JsonValue dw && dw.TryGetValue(out double wide) && double.IsFinite(wide))
                {
                    DrawerWidth = Math.Clamp(wide, MinDrawerWidth, MaxDrawerWidth);
                }

                Favourites = Ids(o, "favourites", int.MaxValue);
                Recent = Ids(o, "recent", MaxRecent);
                Scene = Text(o, "scene") is string scene && ViewState.Scenes.Contains(scene) ? scene : Scene;
                Budget = Text(o, "budget") is string budget && ViewState.Budgets.Contains(budget) ? budget : Budget;
                ExportLook = Text(o, "exportLook") is string look && Nature.Export.ExportLook.All.Contains(look) ? look : ExportLook;
                ExportPictureSize = o["exportPictureSize"] is JsonValue ps && ps.TryGetValue(out int size) ? Recipes.ExportOptions.KeptPictureSize(size) : ExportPictureSize;
                Wind = o["wind"] is JsonValue windOn && windOn.TryGetValue(out bool blows) && blows;
                if (o["windStrength"] is JsonValue ws && ws.TryGetValue(out double strength) && double.IsFinite(strength))
                {
                    WindStrength = Math.Clamp(strength, MinWindStrength, MaxWindStrength);
                }

                Lighting = ReadLighting(o["lighting"]);
                GroundSlope = o["groundSlope"] is JsonValue gs && gs.TryGetValue(out double slope) ? ViewState.KeptSlope(slope) : 0;
                GroundDownhill = o["groundDownhill"] is JsonValue gd && gd.TryGetValue(out double downhill) ? ViewState.KeptDownhill(downhill) : 0;
                if (o["panelWidth"] is JsonValue width && width.TryGetValue(out double px) && double.IsFinite(px))
                {
                    PanelWidth = Math.Clamp(px, MinPanelWidth, MaxPanelWidth);
                }

                if (o["panelOpen"] is JsonValue panel && panel.TryGetValue(out bool shown))
                {
                    PanelOpen = shown;
                }

                OpenGroups = ReadGroups(o["openGroups"]);
                PaletteRecent = Ids(o, "paletteRecent", MaxPaletteRecent);
                PaletteUsage = ReadUsage(o["paletteUsage"]);
                PropsSort = Text(o, "propsSort") switch
                {
                    "name" => Props.PropSort.Name,
                    "made" => Props.PropSort.DateMade,
                    _ => Props.PropSort.LastChanged,
                };
                TeamServer = Text(o, "teamServer");
                BackupFolder = Text(o, "backupFolder");
                if (o["backupKeep"] is JsonValue keep && keep.TryGetValue(out int kept))
                {
                    BackupKeep = Math.Clamp(kept, 1, 100);
                }

                if (o["cloud"] is JsonObject cloud)
                {
                    CloudService = Text(cloud, "service");
                    CloudEndpoint = Text(cloud, "endpoint");
                    CloudRegion = Text(cloud, "region");
                    CloudBucket = Text(cloud, "bucket");
                    CloudFolder = Text(cloud, "folder") ?? CloudFolder;
                    if (cloud["keep"] is JsonValue ck && ck.TryGetValue(out int cloudKept))
                    {
                        CloudKeep = Math.Clamp(cloudKept, 1, 100);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Nothing remembered.
        }
    }

    /// <summary>
    /// Gets or sets the team server typed in Settings (host or host:port); null or blank to use the Launcher's
    /// JATIFY_TEAM, and offline when that is unset too.
    /// </summary>
    public string? TeamServer { get; set; }

    /// <summary>Gets or sets the Groundwork map folder last exported to.</summary>
    public string? MapFolder { get; set; }

    /// <summary>Gets or sets the folder a work file was last saved or opened in.</summary>
    public string? SaveFolder { get; set; }

    /// <summary>Gets or sets the folder a set was last exported into (its own folder is made inside it).</summary>
    public string? SetFolder { get; set; }

    /// <summary>Gets or sets a value indicating whether Claude may connect (off until the user turns it on).</summary>
    public bool ClaudeEnabled { get; set; }

    /// <summary>Gets or sets the port Claude's connection listens on (127.0.0.1 only).</summary>
    public int ClaudePort { get; set; } = DefaultClaudePort;

    /// <summary>Gets or sets the secret Claude must send; made once and kept.</summary>
    public string? ClaudeToken { get; set; }

    /// <summary>Gets or sets a value indicating whether the objects drawer is open (it is until closed).</summary>
    public bool DrawerOpen { get; set; } = true;

    /// <summary>Gets or sets the drawer's tab, by its name without the count ("All", "Recent", "Mine", "Boulders").</summary>
    public string DrawerTab { get; set; } = "All";

    /// <summary>Gets or sets a value indicating whether the objects drawer is docked on the left of the view (at the bottom until moved).</summary>
    public bool DrawerLeft { get; set; }

    /// <summary>Gets or sets the drawer's height at the bottom, in pixels, from 160 to 360 (220 until changed).</summary>
    public double DrawerHeight { get; set; } = DefaultDrawerHeight;

    /// <summary>Gets or sets the drawer's width on the left, in pixels, from 220 to 560 (300 until changed).</summary>
    public double DrawerWidth { get; set; } = DefaultDrawerWidth;

    /// <summary>The drawer's height at the bottom until it is changed (the design's 220).</summary>
    public const double DefaultDrawerHeight = 220;

    /// <summary>The lowest the drawer can be made at the bottom.</summary>
    public const double MinDrawerHeight = 160;

    /// <summary>The tallest the drawer can be made at the bottom.</summary>
    public const double MaxDrawerHeight = 360;

    /// <summary>The drawer's width on the left until it is changed (the prototype's 300).</summary>
    public const double DefaultDrawerWidth = 300;

    /// <summary>The narrowest the drawer can be made on the left.</summary>
    public const double MinDrawerWidth = 220;

    /// <summary>The widest the drawer can be made on the left.</summary>
    public const double MaxDrawerWidth = 560;

    /// <summary>Gets or sets the starting points marked as favourites, by id, in the order they were marked.</summary>
    public IReadOnlyList<string> Favourites { get; set; } = [];

    /// <summary>Gets or sets the starting points last used from the drawer, by id, newest first (twelve kept).</summary>
    public IReadOnlyList<string> Recent { get; set; } = [];

    /// <summary>Gets or sets the scene around the piece: "studio", "studio-sky" (the default) or "grey".</summary>
    public string Scene { get; set; } = ViewState.DefaultScene;

    /// <summary>Gets or sets what the piece is for, which sets its face budget: "hero", "standard" (the default), "background" or "mobile".</summary>
    public string Budget { get; set; } = ViewState.DefaultBudget;

    /// <summary>Gets or sets how the game will draw an exported prop: lit, cartoon or cel (Comic). Lit until chosen.</summary>
    public string ExportLook { get; set; } = Nature.Export.ExportLook.Lit;

    /// <summary>
    /// Gets or sets how many pixels across the closest level's pictures are in an export (bark, stone, cut ends): 512,
    /// 1024 or 2048. 512 until chosen; anything else reads 512.
    /// </summary>
    public int ExportPictureSize { get; set; } = Recipes.ExportOptions.DefaultPictureSize;

    /// <summary>The calmest wind the view offers.</summary>
    public const double MinWindStrength = 0.3;

    /// <summary>The stormiest wind the view offers.</summary>
    public const double MaxWindStrength = 2;

    /// <summary>Gets or sets a value indicating whether the view moves the prop in the wind, as the engine kits will. Off until chosen.</summary>
    public bool Wind { get; set; }

    /// <summary>Gets or sets the wind's strength in the view: 1 is how the engine kits start, 0.3 a calm day, 2 a storm.</summary>
    public double WindStrength { get; set; } = 1;

    /// <summary>Gets or sets the sun and the shadows of the view.</summary>
    public Lighting Lighting { get; set; } = Lighting.Default;

    /// <summary>Gets or sets how steep the view's ground is, in degrees (0, level, until chosen; at most 35).</summary>
    public double GroundSlope { get; set; }

    /// <summary>Gets or sets which way the view's sloping ground falls, in degrees around the prop (0 towards the front).</summary>
    public double GroundDownhill { get; set; }

    /// <summary>Gets or sets the settings panel's width in pixels, from 240 to 520 (320 until changed).</summary>
    public double PanelWidth { get; set; } = DefaultPanelWidth;

    /// <summary>Gets or sets a value indicating whether the settings panel is open (it is until hidden at its edge).</summary>
    public bool PanelOpen { get; set; } = true;

    /// <summary>
    /// Gets or sets which groups of the settings panel are open, for each recipe ("cut-rock", "slab-cliff",
    /// "floor_tile"). A recipe not in the list opens its first groups: Shape for a rock, Proportions and Blocks for a
    /// cliff, Wood and Carving for the floor.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> OpenGroups { get; set; } = DefaultOpenGroups;

    /// <summary>Gets the prefs file.</summary>
    public string FilePath { get; }

    /// <summary>Gets or sets the order of Your props: last changed (until chosen), name or date made.</summary>
    internal Props.PropSort PropsSort { get; set; } = Props.PropSort.LastChanged;

    /// <summary>Gets or sets the folder the daily backup goes into; null while it is off.</summary>
    public string? BackupFolder { get; set; }

    /// <summary>Gets or sets how many daily backups the folder keeps, from 1 to 100 (10 until chosen).</summary>
    public int BackupKeep { get; set; } = 10;

    /// <summary>Gets or sets the cloud backup's service, by id ("r2", "b2", "aws", "wasabi", "spaces", "minio", "other"); null while it is off.</summary>
    public string? CloudService { get; set; }

    /// <summary>Gets or sets the cloud storage's address (a host, with a port if needed), or null for Amazon S3's own.</summary>
    public string? CloudEndpoint { get; set; }

    /// <summary>Gets or sets the cloud storage's region, or null for the service's usual one.</summary>
    public string? CloudRegion { get; set; }

    /// <summary>Gets or sets the bucket the cloud backups go into.</summary>
    public string? CloudBucket { get; set; }

    /// <summary>Gets or sets the folder in the bucket ("prop-generator" until chosen).</summary>
    public string CloudFolder { get; set; } = "prop-generator";

    /// <summary>Gets or sets how many cloud backups the bucket keeps, from 1 to 100 (10 until chosen).</summary>
    public int CloudKeep { get; set; } = 10;

    /// <summary>Gets or sets the palette's last commands run, by id, newest first (five kept).</summary>
    public IReadOnlyList<string> PaletteRecent { get; set; } = [];

    /// <summary>Gets or sets how often each palette entry was run, by id; a count stops at 20, and at most 200 ids are kept.</summary>
    public IReadOnlyDictionary<string, int> PaletteUsage { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>How many recent palette commands are kept.</summary>
    public const int MaxPaletteRecent = 5;

    /// <summary>The count a palette entry's use stops at.</summary>
    public const int MaxPaletteUse = 20;

    private const int MaxPaletteIds = 200;

    /// <summary>
    /// Notes that a palette entry was run: it goes to the front of the recent five, and its use count goes up by one
    /// (to 20 at most). The ids with the fewest uses go first when more than 200 are kept. Not saved: call <see cref="Save"/>.
    /// </summary>
    /// <param name="id">The entry's id.</param>
    public void NotePaletteUsed(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        PaletteRecent = [.. new[] { id }.Concat(PaletteRecent.Where(x => x != id)).Take(MaxPaletteRecent)];
        var usage = new Dictionary<string, int>(PaletteUsage, StringComparer.Ordinal)
        {
            [id] = Math.Min(MaxPaletteUse, PaletteUsage.GetValueOrDefault(id) + 1),
        };
        while (usage.Count > MaxPaletteIds)
        {
            usage.Remove(usage.Where(kv => !PaletteRecent.Contains(kv.Key)).MinBy(kv => kv.Value).Key);
        }

        PaletteUsage = usage;
    }

    /// <summary>The settings panel's width until it is changed.</summary>
    public const double DefaultPanelWidth = 320;

    /// <summary>The narrowest the settings panel can be made.</summary>
    public const double MinPanelWidth = 240;

    /// <summary>The widest the settings panel can be made.</summary>
    public const double MaxPanelWidth = 520;

    /// <summary>
    /// Gets the groups open at first for each recipe: Shape for a rock (easy-props' default), Proportions and Blocks for
    /// a cliff, Wood and Carving for the floor.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultOpenGroups { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["cut-rock"] = ["Shape"],
            ["slab-cliff"] = ["Proportions", "Blocks"],
            ["stylized-rock"] = ["Shape"],
            ["floor_tile"] = ["Wood", "Carving"],
        };

    /// <summary>How many recent starting points are kept.</summary>
    public const int MaxRecent = 12;

    /// <summary>The port used until another is chosen.</summary>
    public const int DefaultClaudePort = 47120;

    /// <summary>The default prefs file, in the user's config folder.</summary>
    /// <returns>The path.</returns>
    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "prop-generator", "prefs.json");

    /// <summary>Writes the file (the old one stays whole until the new one is complete).</summary>
    public void Save()
    {
        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (dir is not null && !Directory.Exists(dir))
            {
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(dir);
                }
                else
                {
                    Directory.CreateDirectory(dir, PrivateFolder);
                }
            }

            var o = new JsonObject { ["mapFolder"] = MapFolder, ["saveFolder"] = SaveFolder, ["setFolder"] = SetFolder };
            o["claudeEnabled"] = ClaudeEnabled;
            o["claudePort"] = ClaudePort;
            o["claudeToken"] = ClaudeToken;
            o["drawerOpen"] = DrawerOpen;
            o["drawerTab"] = DrawerTab;
            o["drawerSide"] = DrawerLeft ? "left" : "bottom";
            o["drawerHeight"] = DrawerHeight;
            o["drawerWidth"] = DrawerWidth;
            o["favourites"] = new JsonArray([.. Favourites.Select(id => (JsonNode?)JsonValue.Create(id))]);
            o["recent"] = new JsonArray([.. Recent.Take(MaxRecent).Select(id => (JsonNode?)JsonValue.Create(id))]);
            o["scene"] = Scene;
            o["budget"] = Budget;
            o["exportLook"] = ExportLook;
            o["exportPictureSize"] = Recipes.ExportOptions.KeptPictureSize(ExportPictureSize);
            o["wind"] = Wind;
            o["windStrength"] = WindStrength;
            o["groundSlope"] = GroundSlope;
            o["groundDownhill"] = GroundDownhill;
            o["lighting"] = new JsonObject
            {
                ["azimuth"] = Lighting.Azimuth,
                ["elevation"] = Lighting.Elevation,
                ["warmth"] = Lighting.Warmth,
                ["shadow"] = Lighting.Shadow,
            };
            o["panelWidth"] = PanelWidth;
            o["panelOpen"] = PanelOpen;
            var groups = new JsonObject();
            foreach ((string recipe, IReadOnlyList<string> open) in OpenGroups)
            {
                groups[recipe] = new JsonArray([.. open.Select(g => (JsonNode?)JsonValue.Create(g))]);
            }

            o["openGroups"] = groups;
            o["paletteRecent"] = new JsonArray([.. PaletteRecent.Take(MaxPaletteRecent).Select(id => (JsonNode?)JsonValue.Create(id))]);
            var uses = new JsonObject();
            foreach ((string id, int count) in PaletteUsage)
            {
                uses[id] = count;
            }

            o["paletteUsage"] = uses;
            o["propsSort"] = PropsSort switch
            {
                Props.PropSort.Name => "name",
                Props.PropSort.DateMade => "made",
                _ => "changed",
            };
            o["teamServer"] = TeamServer;
            o["backupFolder"] = BackupFolder;
            o["backupKeep"] = BackupKeep;
            o["cloud"] = new JsonObject
            {
                ["service"] = CloudService,
                ["endpoint"] = CloudEndpoint,
                ["region"] = CloudRegion,
                ["bucket"] = CloudBucket,
                ["folder"] = CloudFolder,
                ["keep"] = CloudKeep,
            };
            string temp = FilePath + ".tmp";

            // The file holds Claude's token: only its owner may read it, from the moment it exists.
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = PrivateFile;
            }

            using (var stream = new FileStream(temp, options))
            {
                using var writer = new StreamWriter(stream);
                writer.Write(o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not remembered this time.
        }
    }

    // An older file and folder (made before the token was kept here) may be open to others: closed on load.
    private void Tighten()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(FilePath));
            if (dir is not null && Directory.Exists(dir) && File.GetUnixFileMode(dir) != PrivateFolder)
            {
                File.SetUnixFileMode(dir, PrivateFolder);
            }

            if (File.Exists(FilePath) && File.GetUnixFileMode(FilePath) != PrivateFile)
            {
                File.SetUnixFileMode(FilePath, PrivateFile);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not ours to change; the file is still read.
        }
    }

    // A list of ids: anything that is not a non-empty string is left out, and so is a repeat.
    private static string[] Ids(JsonObject o, string key, int most) =>
        o[key] is JsonArray a
            ? [.. a.OfType<JsonValue>()
                .Select(v => v.TryGetValue(out string? s) ? s : null)
                .OfType<string>()
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Take(most)]
            : [];

    // The light, when every part is a number inside its range; else the default.
    private static Lighting ReadLighting(JsonNode? node)
    {
        static double? Number(JsonObject o, string key) =>
            o[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : null;

        if (node is JsonObject o
            && Number(o, "azimuth") is double azimuth && Number(o, "elevation") is double elevation
            && Number(o, "warmth") is double warmth && Number(o, "shadow") is double shadow)
        {
            var read = new Lighting(azimuth, elevation, warmth, shadow);
            return read.IsValid ? read : Lighting.Default;
        }

        return Lighting.Default;
    }

    // The open groups per recipe, over the defaults: a recipe whose list is not a list of names keeps its default.
    private static Dictionary<string, IReadOnlyList<string>> ReadGroups(JsonNode? node)
    {
        var groups = new Dictionary<string, IReadOnlyList<string>>(DefaultOpenGroups, StringComparer.Ordinal);
        if (node is JsonObject o)
        {
            foreach ((string recipe, JsonNode? list) in o)
            {
                if (list is JsonArray)
                {
                    groups[recipe] = Ids(o, recipe, int.MaxValue);
                }
            }
        }

        return groups;
    }

    // The use counts: a count that is not a whole number above 0 is left out, and none passes 20.
    private static Dictionary<string, int> ReadUsage(JsonNode? node)
    {
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);
        if (node is JsonObject o)
        {
            foreach ((string id, JsonNode? count) in o)
            {
                if (id.Length > 0 && count is JsonValue v && v.TryGetValue(out int n) && n > 0 && uses.Count < MaxPaletteIds)
                {
                    uses[id] = Math.Min(n, MaxPaletteUse);
                }
            }
        }

        return uses;
    }

    private static string? Text(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s) ? s : null;
}
