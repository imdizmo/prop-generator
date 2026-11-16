using System.Diagnostics;

namespace PropGenerator.Desktop;

/// <summary>
/// When the floor is repainted, and at what detail, as the settings change:
/// <list type="bullet">
/// <item>a colour, contrast or drift change only sets the view's colours (the painting stays);</item>
/// <item>a painting setting dragged with the mouse asks for a quick 256 px paint at every step of the drag;</item>
/// <item>full detail (1024 px) is asked for as soon as a change is final (the slider let go, a toggle flipped, an
/// up-down step, a typed value), or once a drag has been held still for <see cref="HeldStill"/>. When a preview of
/// the same settings is still on its way, full detail is asked for once it has come, so the value picked shows at once
/// (blurred) and then sharpens;</item>
/// <item>what arrives is shown unless something asked for later has already been shown (a late preview never
/// replaces the full paint that followed it), and unless the settings have moved on since it was asked and a paint of
/// the newer ones is on its way: a paint of a value the slider has left is never shown. The one exception is a drag's
/// previews, which are shown while the drag goes on, so the floor follows the hand.</item>
/// </list>
/// The painter itself keeps only the newest request waiting, so asking at every step never queues up work. Painting
/// full detail on the CPU takes about a second, during which the drag's previews wait: so a held drag paints full
/// detail only after a pause longer than the short ones of fine adjustment.
/// </summary>
public sealed class LiveRepaint : IDisposable
{
    /// <summary>The size painted while a slider is dragged.</summary>
    public const int PreviewSize = 256;

    /// <summary>The full-detail size.</summary>
    public const int FullSize = 1024;

    /// <summary>How long a drag must be held still, the mouse button down, before full detail is painted.</summary>
    public static readonly TimeSpan HeldStill = TimeSpan.FromMilliseconds(600);

    private readonly SettingsPanel _panel;
    private readonly IPainter _painter;
    private readonly TileView _view;
    private readonly Debounce _quiet;
    private readonly CancellationTokenSource _stop = new();
    private readonly HashSet<string> _toneKeys;
    private long _asked;
    private int _wantedSize;
    private Settings? _wanted;
    private long _previewOnItsWay;
    private int _fullsOnTheirWay;
    private bool _fullAfterPreview;
    private long _shown;
    private long _askedFull;
    private long _shownFull;
    private bool _dirty;
    private bool _disposed;

    /// <summary>Follows the panel's changes and shows the paints in the view.</summary>
    /// <param name="panel">The settings.</param>
    /// <param name="painter">Paints the layers.</param>
    /// <param name="view">Shows them.</param>
    public LiveRepaint(SettingsPanel panel, IPainter painter, TileView view)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(painter);
        ArgumentNullException.ThrowIfNull(view);
        _panel = panel;
        _painter = painter;
        _view = view;
        _toneKeys = [.. panel.Specs.Where(s => s.Kind == "tone").Select(s => s.Key)];
        _quiet = new Debounce(HeldStill, PaintFull);
        panel.ToneChanged += OnToneChanged;
        panel.PaintChanged += OnPaintChanged;
        panel.Committed += OnCommitted;
        OnToneChanged();
    }

    /// <summary>
    /// Gets or sets a value indicating whether the floor is the entry being made: only then are the panel's changes
    /// followed (another recipe's settings mean nothing to the painter). Turned off, a held-still paint waiting to be
    /// asked for is forgotten, and a paint that arrives meanwhile is dropped (not shown, not announced); turned on again,
    /// nothing is painted until <see cref="RepaintNow"/>.
    /// </summary>
    public bool Following
    {
        get;
        set
        {
            field = value;
            if (!value)
            {
                _quiet.Cancel();
                _fullAfterPreview = false;
            }
        }
    } = true;

    /// <summary>A paint was shown: its size, and the milliseconds from asking for it to having it.</summary>
    public event Action<int, double>? Shown;

    /// <summary>Asking the painter threw (not a failed paint, which the painter reports itself through its event).</summary>
    public event Action<string>? Failed;

    /// <summary>Gets the latest full-detail layers that were shown, or null before the first.</summary>
    public Layers? FullLayers { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="FullLayers"/> is the paint of the settings as they are now: nothing is
    /// waiting to be painted and the newest full-detail request has been shown. A colour, contrast or drift change does
    /// not matter (the layers do not depend on them).
    /// </summary>
    public bool FullIsCurrent => !_dirty && FullLayers is not null && _shownFull == _askedFull;

    /// <summary>
    /// Gets a value indicating whether a full-detail paint is on its way: asked for and not back yet, or waiting for the
    /// preview before it. False after one failed or was dropped: then nothing will make <see cref="FullIsCurrent"/> true
    /// until <see cref="RepaintNow"/> or the next change.
    /// </summary>
    public bool FullOnItsWay => _fullsOnTheirWay > 0 || _fullAfterPreview;

    /// <summary>Waits until the full-detail layers are those of the current settings.</summary>
    /// <param name="timeout">The longest to wait.</param>
    /// <param name="waiting">Called once if it has to wait at all.</param>
    /// <returns>The layers, or null when they did not arrive in time.</returns>
    public async Task<Layers?> WaitForFullAsync(TimeSpan timeout, Action? waiting = null)
    {
        long start = Stopwatch.GetTimestamp();
        bool told = false;
        while (!FullIsCurrent)
        {
            if (_disposed || Stopwatch.GetElapsedTime(start) > timeout)
            {
                return null;
            }

            if (!told)
            {
                told = true;
                waiting?.Invoke();
            }

            await Task.Delay(25);
        }

        return FullLayers;
    }

    /// <summary>Paints the settings as they are now at full detail, at once (after an undo, a redo or a file opened).</summary>
    public void RepaintNow()
    {
        _dirty = true;
        _quiet.Cancel();
        PaintFull(now: true);
    }

    /// <summary>Paints the current settings at full detail (at start).</summary>
    public void Start()
    {
        _dirty = true;
        PaintFull(now: true);
    }

    /// <summary>The colour, contrast or drift changed: the view's colours follow at once.</summary>
    public void OnToneChanged()
    {
        if (!Following)
        {
            return;
        }

        Settings s = _panel.Current;
        _view.SetPalette(Palette.Derive(s.GetColour("colour")));
        _view.SetTone(s.GetFloat("contrast"), s.GetFloat("drift"));
    }

    /// <summary>A painting setting changed.</summary>
    /// <param name="dragging">True while the mouse is dragging it: a quick preview is painted now.</param>
    public void OnPaintChanged(bool dragging)
    {
        if (!Following)
        {
            return;
        }

        _dirty = true;
        _fullAfterPreview = false;
        if (dragging)
        {
            Ask(PreviewSize);
        }

        _quiet.Restart();
    }

    /// <summary>
    /// A change is finished: a drag let go, a toggle flipped, an up-down step, a typed value, an arrow key. It is final,
    /// so if a painting setting changed, full detail is painted now (the painter keeps only the newest of a burst).
    /// </summary>
    /// <param name="label">The setting's label (unused here; the history takes it).</param>
    public void OnCommitted(string label)
    {
        _ = label;
        if (!_dirty || !Following)
        {
            return;
        }

        _quiet.Cancel();
        PaintFull();
    }

    /// <summary>Stops following the panel; paints still on their way are dropped.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _quiet.Cancel();
        _panel.ToneChanged -= OnToneChanged;
        _panel.PaintChanged -= OnPaintChanged;
        _panel.Committed -= OnCommitted;
        _stop.Cancel();
        _stop.Dispose();
    }

    private void PaintFull() => PaintFull(now: false);

    // now: ask at once, even with a preview on its way (undo, redo, open, start: that preview is of settings left).
    private void PaintFull(bool now)
    {
        if (!_dirty || _disposed || !Following)
        {
            return;
        }

        if (!now && _previewOnItsWay != 0)
        {
            _fullAfterPreview = true;
            _wantedSize = FullSize;
            _wanted = _panel.Current;
            return;
        }

        _fullAfterPreview = false;
        _dirty = false;
        Ask(FullSize);
    }

    // A preview arrived (or was dropped): full detail waiting for it is asked for now.
    private void PreviewDone(long number)
    {
        if (number != _previewOnItsWay)
        {
            return;
        }

        _previewOnItsWay = 0;
        if (_fullAfterPreview)
        {
            PaintFull();
        }
    }

    // Shown when it paints the settings of the newest paint asked for (or waiting to be), as a paint of a value the
    // slider has left would show a step back. A drag's previews are shown while the drag goes on (the newest paint asked
    // for is a preview too), so the floor follows the hand.
    private bool StillWanted(int size, Settings settings) =>
        (size == PreviewSize && _wantedSize == PreviewSize) || PaintPart(settings).Equals(PaintPart(_wanted!));

    private Settings PaintPart(Settings s) =>
        new(s.Values.Where(kv => !_toneKeys.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

    private void Ask(int size)
    {
        if (!_disposed)
        {
            long number = ++_asked;
            Settings settings = _panel.Current;
            _wantedSize = size;
            _wanted = settings;
            if (size == FullSize)
            {
                _askedFull = number;
                _fullsOnTheirWay++;
            }
            else
            {
                _previewOnItsWay = number;
            }

            TraceLog.Write($"asked #{number} {size} px");
            _ = AskAsync(size, number, settings);
        }
    }

    private async Task AskAsync(int size, long number, Settings settings)
    {
        // Nothing may escape: this task is not awaited, so an error would go unobserved. Any error goes to the status line.
        try
        {
            long start = Stopwatch.GetTimestamp();
            Layers? layers;
            try
            {
                layers = await _painter.PaintAsync(size, settings, _stop.Token);
            }
            finally
            {
                if (size == FullSize)
                {
                    _fullsOnTheirWay--;
                }

                PreviewDone(number);
            }

            if (layers is null || number <= _shown || _disposed)
            {
                TraceLog.Write($"dropped #{number} {size} px ({(layers is null ? "replaced" : "older than the one shown")})");
                return;
            }

            if (!Following)
            {
                // Another recipe is the entry now (a rock): the floor's paint is neither shown nor announced.
                TraceLog.Write($"dropped #{number} {size} px (the floor is not the entry)");
                return;
            }

            if (!StillWanted(size, settings))
            {
                TraceLog.Write($"dropped #{number} {size} px (the settings have moved on and a newer paint is on its way)");
                return;
            }

            TraceLog.Write($"shown #{number} {size} px{(settings.Equals(_panel.Current) ? string.Empty : " (the settings have moved on since it was asked)")}");

            _shown = number;
            if (size == FullSize)
            {
                FullLayers = layers;
                _shownFull = number;
            }

            _view.SetLayers(layers);
            Shown?.Invoke(size, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            // Stopped (the window closed).
        }
        catch (ObjectDisposedException) when (_disposed)
        {
            // The painter went with the window.
        }
        catch (Exception e)
        {
            Failed?.Invoke($"Painting failed: {e.Message}");
        }
    }
}
