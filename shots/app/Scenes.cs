using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;
using SkiaSharp;

namespace NotaDocsShots;

/// <summary>
/// Every scene the manifest can ask for. A scene builds a window, renders it at 2× and saves
/// the whole frame or a cropped control. Args come from the manifest entry's `args`.
///
///   device    instrument | effect | midi (kind id), preset, tab, card (index in the chain),
///             clip (a demo sample on an effect's track), sidechain + sidechainName (a demo sample on
///             a second track routed as the effect's key),
///             chainInstrument (an Instrument Rack's chain 1 instrument), trackName, window (click this text — a
///             chain's GUI — and shoot the device window it opens)
///   main      view (arrangement | session | modular), select (track name), clip (track name,
///             + clipIndex, audio: true for an audio clip) to open in the clip editor, with ("Track:clipIndex,…" —
///             more MIDI clips selected with it, so they open together), automation, tab (browser tab index),
///             expand (a folder in that tab) + pick (a row to select, which the preview player takes), detailHeight, element, width, height,
///             fold (the selected track's audio effects to fold into strips, by chain index, e.g. "0,2"; "i" = the instrument)
///             mixer (true: the Mixer docked under the view) + mixerHeight (its panel height; 0 = default)
///   palette   the command palette (⌘⇧P) over the main window: view, select (track), card (index of
///             an audio effect to select in Devices), query, down (rows to move), mods (alt | cmd | shift),
///             origin (devices: the Devices panel had the focus), crop (true: just the palette card)
///   mixer     width, height
///             update (available | downloading | ready: a newer release in the status bar, element: StatusBar)
///   prefs     page (index in Settings' sidebar, 0 = Audio), tab (a switch on the page),
///             check (Updates: run a check against the local feed first), update (as main)
///   connect   the Connect phone popup, with Nota Remote switched on
///   start · about · whatsnew · export · unsaved
/// </summary>
public sealed class Scenes
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _out;
    private readonly AppTheme _theme;
    private readonly IAudioEngine _engine;
    private readonly FactoryPresetCatalog _presets = new();
    private readonly Demo _demo;
    private readonly Redactor _redactor;

    public Scenes(string outDir, AppTheme theme)
    {
        _out = outDir;
        _theme = theme;
        _engine = (IAudioEngine)App.Services!.GetService(typeof(IAudioEngine))!;
        var settings = (ISettingsService)App.Services!.GetService(typeof(ISettingsService))!;
        var data = Environment.GetEnvironmentVariable("NOTA_DATA_DIR")!;
        // The sample and project folders default to the real ~/Music and ~/Documents, which
        // NOTA_DATA_DIR doesn't cover — point them into the scratch folder.
        settings.Current.SamplesFolder = Path.Combine(data, "Nota Samples");
        settings.Current.ProjectsFolder = Path.Combine(data, "Nota Projects");
        settings.Current.Theme = theme;
        SetTheme(theme);
        _redactor = new Redactor(data);
        RenameDevices();
        _demo = new Demo(_engine, Path.Combine(data, "demo"));
        _demo.WriteSamples(settings.Current.SamplesFolder);

        // Self-update (#58): the shots never ask GitHub. Automatic checks are off, and a check a
        // scene runs reads a local release feed that says this very version is the latest.
        if (typeof(Settings).GetProperty("CheckForUpdates") is { } auto) auto.SetValue(settings.Current, false);
        _feed = Path.Combine(data, "release.json");
        File.WriteAllText(_feed, $"{{\"tag_name\": \"v{AppInfo.Version}\", \"html_url\": \"https://github.com/nota-daw/nota/releases\", \"assets\": []}}");
        Environment.SetEnvironmentVariable("NOTA_UPDATE_API", _feed);
    }

    private readonly string _feed;

    // Wear the theme for this pass. Before the Studio colour themes NotaThemeService.Set took
    // an AppTheme; since then it takes an AppearanceChoice — one single theme, here Studio Dark
    // or Studio Light. Reflection keeps the harness building against either.
    private static void SetTheme(AppTheme theme)
    {
        var set = typeof(App).Assembly.GetType("Nota.App.NotaThemeService")!.GetMethod("Set")!;
        var param = set.GetParameters()[0].ParameterType;
        object arg = param == typeof(AppTheme)
            ? theme
            : Activator.CreateInstance(param, false, theme == AppTheme.Light ? "studio-light" : "studio-dark", "studio-dark", "studio-light")!;
        set.Invoke(null, new[] { arg });
    }

    public void Run(Shot shot)
    {
        switch (shot.Scene)
        {
            case "device": Device(shot); break;
            case "main": Main(shot); break;
            case "palette": Palette(shot); break;
            case "mixer": Mixer(shot); break;
            case "prefs": Prefs(shot); break;
            case "start": Start(shot); break;
            case "about": Simple(shot, new AboutWindow()); break;
            case "whatsnew": WhatsNew(shot); break;
            case "export": Simple(shot, new ExportWindow(128, 32, 124)); break;
            case "connect": Connect(shot); break;
            case "unsaved":
                Simple(shot, new SaveChangesWindow("Unsaved changes", "Save changes to “Night Drive” before closing?",
                    "Night Drive", "Unsaved for 12 minutes"));
                break;
            default: throw new ArgumentException($"unknown scene '{shot.Scene}'");
        }
    }

    // ── scenes ─────────────────────────────────────────────────────────────────────────

    private void Device(Shot shot)
    {
        _engine.Reset();
        int track;
        int deviceIndex = -1;
        if (shot.Int("effect") is int fx)
        {
            track = _engine.AddAudioTrack();
            if (shot.Str("clip") is string clip) _engine.AddAudioClip(track, _demo.Sample(clip), 0);
            deviceIndex = _engine.AddBuiltinDevice(track, fx);
            // A keyed effect (a vocoder's modulator, a compressor's key): another track with a sample.
            if (shot.Str("sidechain") is string key)
            {
                int src = _engine.AddAudioTrack();
                _engine.AddAudioClip(src, _demo.Sample(key), 0);
                _engine.SetTrackName(src, shot.Str("sidechainName") ?? "Lead vox");
                _engine.SetDeviceSidechainSource(track, deviceIndex, src);
            }
        }
        else if (shot.Int("instrument") == 4)
        {
            // Drum Rack: loaded with a factory kit (or the kit named in `kit`) so the pads are filled.
            var kits = Kits;
            var kit = shot.Str("kit") ?? kits.All()[0].Id;
            track = kits.CreateTrack(_engine, kit, out _);
            if (track <= 0) throw new InvalidOperationException($"drum kit '{kit}' did not load");
        }
        else if (shot.Int("instrument") == 3)
        {
            track = _engine.AddInstrumentRackTrack();
            if (shot.Int("chainInstrument") is int ck && !_engine.RackSetChainInstrument(track, 0, ck))
                throw new InvalidOperationException($"chain instrument {ck} did not load");
            if (shot.Str("trackName") is string tn) _engine.SetTrackName(track, tn);
        }
        else
        {
            track = _engine.AddInstrumentTrack();
            int kind = shot.Int("instrument") ?? 0;
            _engine.SetTrackBuiltinInstrument(track, kind);
            if (kind == 1) _engine.SetTrackSamplerSample(track, _demo.Sample("pad"), 60);
            if (kind == 10) _engine.SetTrackGrainSample(track, _demo.Sample("pad"), 60);
            if (shot.Int("midi") is int midi) deviceIndex = _engine.AddMidiEffect(track, midi);
        }
        // `rack: demo` fills a rack with chains, zones, devices and macros (frames 1a–1f of the
        // "Nota Racks" design), so its card shows what the rack does.
        if (shot.Str("rack") == "demo") FurnishRack(track, deviceIndex, shot.Int("instrument") == 3 ? 3 : shot.Int("effect") ?? shot.Int("midi") ?? -1);
        if (shot.Str("preset") is string preset)
        {
            // An effect preset loads onto its device, an instrument preset onto the track's
            // instrument (index -1); a MIDI-effect preset adds its device.
            var result = shot.Int("midi") is null
                ? _presets.ApplyInPlace(_engine, preset, track, deviceIndex)
                : _presets.Apply(_engine, preset, track);
            if (!string.IsNullOrEmpty(result) && result.StartsWith("Unknown")) throw new ArgumentException($"{result} ({preset})");
            // A Nota Mosaic preset carries its multisample; load it the way PresetService does and
            // wait for the samples, so the zone map has something to draw.
            if (_presets.Document(preset) is { MosaicSource.Length: > 0 } mdoc
                && Nota.Infrastructure.Mosaic.MosaicSourceLibrary.Program(mdoc.MosaicSource) is { } prog)
            {
                prog.Name = mdoc.DisplayName.Length > 0 ? mdoc.DisplayName : prog.Name;
                _engine.MosaicSetProgram(track, prog.Serialize());
                for (int w = 0; w < 400; w++)
                {
                    if (_engine.TryGetMosaicStatus(track, out var st) && st.FilesTotal > 0 && st.FilesDone >= st.FilesTotal) break;
                    Thread.Sleep(50);
                }
            }
        }
        // A little audio through the device so meters, spectra and envelopes have something to draw.
        _engine.Play();
        var buf = new float[1024 * 2];
        for (int k = 0; k < 40; k++) _engine.RenderOffline(buf, 1024);
        _engine.StopTransport();

        var view = new DeviceChainView(_engine, _presets, null, Kits);
        // An instrument preset names itself in the card's picker, as when the app applies it.
        if (shot.Str("preset") is string ip && deviceIndex < 0 && _presets.Document(ip) is { } idoc)
        {
            var remember = typeof(DeviceChainView).GetMethod("RememberPreset", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var instrumentChain = Enum.Parse(remember.GetParameters()[1].ParameterType, "Instrument");
            remember.Invoke(view, new object[] { track, instrumentChain, -1, idoc.DisplayName, ip });
        }
        var win = Host(view, 1100, 360);
        win.Show();
        view.Show(track);
        Pump();
        view.RefreshSynthLive();
        Pump();
        // `tab` clicks one control by its text, or a chain of them: "L>Filter".
        if (shot.Str("tab") is string tab)
            foreach (var part in tab.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                ClickText(win, part);
                view.RefreshSynthLive();
                Pump();
            }
        view.RefreshSynthLive();
        Pump();
        if (shot.Str("window") is string open)
        {
            // Pop a device out (a rack chain's GUI button) and shoot its device window.
            ClickText(win, open);
            Pump(20);
            var dw = win.OwnedWindows.LastOrDefault() ?? throw new InvalidOperationException($"'{open}' opened no window");
            Scale2(dw);
            Pump(20);
            Save(dw, shot.Id);
            dw.Close();
            win.Close();
            return;
        }
        var chain = view.GetVisualDescendants().OfType<StackPanel>().First(p => p.Orientation == Orientation.Horizontal && p.Children.Count > 0 && p.Bounds.Height > 200);
        var card = chain.Children[shot.Int("card") ?? 0];
        Save(win, shot.Id, card);
        win.Close();
    }

    private void FurnishRack(int t, int di, int kind)
    {
        var e = _engine;
        switch (kind)
        {
            case 3: // Instrument Rack: three layers with zones, an Arp in front of the Volt
            {
                while (e.RackChainCount(t) > 0) e.RackRemoveChain(t, 0);
                int a = e.RackAddChain(t, 6), b = e.RackAddChain(t, 14), c = e.RackAddChain(t, 16);
                e.RackSetChainZone(t, a, 0, 70, 0, 127);
                e.RackSetChainZone(t, b, 58, 127, 0, 127);
                e.RackSetChainZone(t, c, 36, 84, 96, 127);
                e.RackAddChainDevice(t, a, 25);
                e.RackAddChainDevice(t, c, 2);
                e.RackChainMidiAdd(t, a, 0); // an Arp in front of the Volt (#329)
                e.RackSetMacroName(t, 0, "Bright"); e.RackMacroSet(t, 0, 0.6f);
                e.RackSetMacroName(t, 1, "Space"); e.RackMacroSet(t, 1, 0.35f);
                e.RackSetMacroName(t, 2, "Attack"); e.RackMacroSet(t, 2, 0.1f);
                e.RackAddMacroMapping(t, 0, a, -1, 16, 0.3f, 0.9f);
                int m1 = e.RackAddMacroMapping(t, 0, b, -1, 40, 0.2f, 0.8f);
                e.RackSetMappingCurve(t, m1, 2);
                e.RackAddMacroMapping(t, 1, c, 0, 0, 0.2f, 0.8f);
                e.RackAddMacroMapping(t, 2, a, -1, 36, 0f, 0.7f);
                e.RackSetGlide(t, 0.3f);
                break;
            }
            case 5: // Audio Effect Rack in Select mode
            {
                while (e.RackDevChainCount(t, di) > 0) e.RackDevRemoveChain(t, di, 0);
                int x = e.RackDevAddChain(t, di, -1), y = e.RackDevAddChain(t, di, -1);
                e.RackDevAddChainDevice(t, di, x, 6); e.RackDevAddChainDevice(t, di, x, 1);
                e.RackDevAddChainDevice(t, di, y, 2); e.RackDevAddChainDevice(t, di, y, 16);
                e.RackDevSetChainZone(t, di, x, 0, 63); e.RackDevSetChainZone(t, di, y, 48, 127);
                e.RackDevSetMode(t, di, 2); e.RackDevSetChainSelect(t, di, 0.24f);
                e.RackDevSetMacroName(t, di, 0, "Drive"); e.RackDevMacroSet(t, di, 0, 0.45f);
                e.RackDevAddMacroMapping(t, di, 0, x, 0, 1, 0f, 10f);
                break;
            }
            case 6: // MIDI Effect Rack: an arp above C3, chords below, accents on hard hits
            {
                e.MidiRackAddChain(t, di); e.MidiRackAddChain(t, di);
                e.MidiRackSetChainName(t, di, 0, "Arp lead"); e.MidiRackSetChainName(t, di, 1, "Chords"); e.MidiRackSetChainName(t, di, 2, "Accents");
                e.MidiRackSetChainZone(t, di, 0, 60, 127, 0, 127); e.MidiRackSetChainZone(t, di, 1, 0, 59, 0, 127); e.MidiRackSetChainZone(t, di, 2, 36, 96, 110, 127);
                e.MidiRackChainDeviceAdd(t, di, 0, 0); e.MidiRackChainDeviceAdd(t, di, 0, 2);
                e.MidiRackChainDeviceAdd(t, di, 1, 1); e.MidiRackChainDeviceAdd(t, di, 1, 4);
                e.MidiRackChainDeviceAdd(t, di, 2, 5);
                e.MidiRackSetChainTranspose(t, di, 2, 12);
                e.MidiRackSetChainSelect(t, di, 0, 0, 63); e.MidiRackSetChainSelect(t, di, 1, 64, 127);
                e.MidiRackSetMacroName(t, di, 0, "Rate"); e.MidiRackMacroSet(t, di, 0, 0.5f); e.MidiRackAddMacroMapping(t, di, 0, 0, 0, 0, 0f, 3f);
                e.MidiRackSetMacroName(t, di, 1, "Strum"); e.MidiRackMacroSet(t, di, 1, 0.2f); e.MidiRackAddMacroMapping(t, di, 1, 1, 0, 6, 0f, 200f);
                break;
            }
        }
    }

    private static IDrumKits Kits => (IDrumKits)App.Services!.GetService(typeof(IDrumKits))!;

    private MainWindow? _main;

    private MainWindow MainWindow(Shot shot)
    {
        if (_main == null)
        {
            _engine.Reset();
            var mw = new MainWindow();
            mw.DataContext = App.Services!.GetService(typeof(MainWindowViewModel));
            _main = mw;
            mw.Show();
            Pump(20);
            // The launch workspace starts from the default project template (instrument, audio,
            // Reverb and Delay returns); the demo song is built on an empty engine instead.
            _engine.Reset();
            // Name the project, as if it had been saved; headless also draws the NativeMenu inside
            // the window (on macOS it lives in the system menu bar), so hide that strip.
            var bundle = Path.Combine(_demo.Folder, "Night Drive.nota");
            if (Directory.Exists(bundle)) Directory.Delete(bundle, recursive: true);
            typeof(MainWindow).GetField("_projectPath", Private)!.SetValue(mw, bundle);
            var vm = (MainWindowViewModel)mw.DataContext!;
            _demo.Build(
                save: note =>
                {
                    mw.FindControl<ArrangementView>("Timeline")!.Refresh();
                    Pump(5);
                    var task = (Task<bool>)typeof(MainWindow).GetMethod("DoSaveAsync", Private)!.Invoke(mw, new object?[] { false, note })!;
                    while (!task.IsCompleted) Pump(2);
                    if (!task.Result) throw new InvalidOperationException("demo save failed: " + vm.StatusText);
                },
                tempo: bpm => { vm.Transport.Bpm = (decimal)bpm; Pump(2); });
            foreach (var bar in mw.GetVisualDescendants().OfType<NativeMenuBar>()) bar.IsVisible = false;
            mw.FindControl<ArrangementView>("Timeline")!.Refresh();
            Pump(20);
            Scale2(mw);
        }
        var w = shot.Int("width") ?? 1440;
        var h = shot.Int("height") ?? 900;
        _main.Width = w * 2;
        _main.Height = h * 2;
        Pump(10);
        return _main;
    }

    private void Main(Shot shot)
    {
        var mw = MainWindow(shot);
        var view = shot.Str("view") ?? "arrangement";
        Invoke(mw, view switch { "session" => "OnShowSession", "modular" => "OnShowModular", _ => "OnShowArrangement" }, null, new RoutedEventArgs());
        if (shot.Str("select") is string name)
        {
            int id = _demo.Track(name);
            Invoke(mw, "OnTrackSelected", id);
            if (view == "modular") Invoke(mw, "OnCloseDetail", null, new RoutedEventArgs());
            else Invoke(mw, "ShowDevices", id, true);
            if (shot.Str("fold") is string fold) Fold(mw, id, fold, true);
        }
        else Invoke(mw, "OnCloseDetail", null, new RoutedEventArgs());
        if (shot.Str("clip") is string clipTrack)
        {
            // Open a clip of a demo track in the clip editor, as a double-click on it would.
            var timeline = mw.FindControl<ArrangementView>("Timeline")!;
            if (shot.Str("with") is string with)
            {
                // Several MIDI clips selected: the double-click opens them all (multi-clip editor).
                var together = new List<(int, int)> { (_demo.Track(clipTrack), shot.Int("clipIndex") ?? 0) };
                foreach (var part in with.Split(','))
                {
                    var tc = part.Split(':');
                    together.Add((_demo.Track(tc[0].Trim()), tc.Length > 1 ? int.Parse(tc[1]) : 0));
                }
                Invoke(timeline, "SetSelection", together.ToArray().AsEnumerable());
            }
            Invoke(timeline, "OnClipDoubleClicked", _demo.Track(clipTrack), shot.Int("clipIndex") ?? 0, !shot.Bool("audio"));
        }
        // Automation lanes on or off, as the transport's automation button sets them.
        if (mw.FindControl<ToggleButton>("AutomationToggle") is { } autoBtn)
        {
            autoBtn.IsChecked = shot.Bool("automation");
            mw.FindControl<ArrangementView>("Timeline")!.AutomationMode = shot.Bool("automation");
        }
        if (shot.Int("detailHeight") is int dh && mw.FindControl<Grid>("BodyGrid") is { } body)
            body.RowDefinitions[2].Height = new GridLength(dh);
        // The browser tab sticks between shots, so always set it (Instruments by default).
        var browser = mw.FindControl<BrowserView>("Browser")!;
        typeof(BrowserView).GetMethod("SelectTab", Private)!.Invoke(browser, new object[] { shot.Int("tab") ?? 0 });
        if (shot.Str("pick") is string pick)
        {
            // Open a folder (`expand`) and select a row in it, as a click would: the preview
            // player below the list takes the row.
            var list = ((ListBox[])typeof(BrowserView).GetField("_pages", Private)!.GetValue(browser)!)[shot.Int("tab") ?? 0];
            BrowserItem? Row(string name) => (list.ItemsSource as System.Collections.IEnumerable)?.OfType<BrowserItem>().FirstOrDefault(r => r.Name == name);
            if (shot.Str("expand") is string folder)
            {
                var vm = (BrowserViewModel)typeof(BrowserView).GetField("_vm", Private)!.GetValue(browser)!;
                vm.ToggleExpand(Row(folder) ?? throw new ArgumentException($"no folder '{folder}'"));
                Pump(10);
            }
            list.SelectedItem = Row(pick) ?? throw new ArgumentException($"no row '{pick}'");
            Pump(30);
        }
        // Zoom: fit the whole song, or the app's default (28 px a beat from bar 1) — the view
        // keeps its zoom between shots otherwise.
        var tl = mw.FindControl<ArrangementView>("Timeline")!;
        if (shot.Bool("fit")) Invoke(tl, "ZoomToFitProject");
        else Invoke(tl, "ApplyOverviewView", 28.0, 0.0);
        Pump(20);
        if (shot.Str("update") is string stage) FakeUpdate(stage);
        else HideUpdate();
        // The Mixer docked under the view (specs/054), at `mixerHeight` (0 = its default).
        bool mixer = shot.Bool("mixer");
        if (mixer)
        {
            var settings = (ISettingsService)App.Services!.GetService(typeof(ISettingsService))!;
            settings.Current.MixerFloating = false;
            settings.Current.MixerDockHeight = shot.Int("mixerHeight") ?? 0;
            Invoke(mw, "ShowMixer");
            Pump(30);
        }
        var element = shot.Str("element") is string el ? mw.FindControl<Control>(el) ?? throw new ArgumentException($"no control '{el}'") : null;
        Save(mw, shot.Id, element);
        if (mixer) { Invoke(mw, "HideMixer"); Pump(5); }
        // The demo song is shared by every shot: open the folded cards again.
        if (shot.Str("select") is string sel && shot.Str("fold") is string unfold) Fold(mw, _demo.Track(sel), unfold, false);
    }

    // Fold (or unfold) devices on a track and redraw its chain. Through reflection, so the
    // harness still builds against an app from before device folding (specs/021).
    private void Fold(MainWindow mw, int trackId, string which, bool folded)
    {
        var set = typeof(IAudioEngine).GetMethod("SetDeviceFolded") ?? throw new ArgumentException("this app can't fold devices");
        foreach (var part in which.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            set.Invoke(_engine, new object[] { trackId, part == "i" ? -1 : int.Parse(part), folded });
        if (folded) Invoke(mw, "ShowDevices", trackId, true);
    }

    private void Palette(Shot shot)
    {
        var mw = MainWindow(shot);
        var view = shot.Str("view") ?? "arrangement";
        Invoke(mw, view switch { "session" => "OnShowSession", "modular" => "OnShowModular", _ => "OnShowArrangement" }, null, new RoutedEventArgs());
        var tl = mw.FindControl<ArrangementView>("Timeline")!;
        Invoke(tl, "ApplyOverviewView", 28.0, 0.0);
        if (shot.Str("select") is string name)
        {
            int id = _demo.Track(name);
            Invoke(mw, "OnTrackSelected", id);
            if (view != "modular") Invoke(mw, "ShowDevices", id, true);
            var chain = (DeviceChainView)typeof(MainWindow).GetField("_deviceChain", Private)!.GetValue(mw)!;
            if (shot.Int("card") is int card)
            {
                var kindType = typeof(DeviceChainView).GetNestedType("ChainKind", BindingFlags.NonPublic)!;
                Invoke(chain, "SelectDevice", Enum.Parse(kindType, "Effect"), card);
            }
            typeof(MainWindow).GetField("_detailWasLastFocused", Private)!.SetValue(mw, shot.Str("origin") == "devices");
        }
        else Invoke(mw, "OnCloseDetail", null, new RoutedEventArgs());
        Pump(10);
        Invoke(mw, "TogglePalette", new object?[] { null });
        Pump(10);
        var palette = (Control)typeof(MainWindow).GetField("_palette", Private)!.GetValue(mw)!;
        var box = palette.GetVisualDescendants().OfType<TextBox>().First();
        box.Text = shot.Str("query") ?? "";
        Pump(10);
        for (int i = 0; i < (shot.Int("down") ?? 0); i++)
        {
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down, Source = box });
            Pump(2);
        }
        if (shot.Str("mods") is string mods)
        {
            var m = mods switch { "alt" => KeyModifiers.Alt, "shift" => KeyModifiers.Shift, _ => KeyModifiers.Meta };
            var key = mods switch { "alt" => Key.LeftAlt, "shift" => Key.LeftShift, _ => Key.LWin };
            palette.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = m, Source = box });
        }
        Pump(20);
        Control? crop = null;
        if (shot.Bool("crop")) crop = palette.GetVisualDescendants().OfType<Border>().First(b => b.Width == 640);
        Save(mw, shot.Id, crop);
        Invoke(mw, "TogglePalette", new object?[] { null });
        Pump(5);
    }

    private void Mixer(Shot shot)
    {
        MainWindow(shot);   // the demo song
        var view = new MixerView(_engine);
        var win = Host(view, shot.Int("width") ?? 1200, shot.Int("height") ?? 560);
        win.Show();
        view.Refresh();
        Pump(20);
        Save(win, shot.Id);
        win.Close();
    }

    private void Prefs(Shot shot)
    {
        var settings = (ISettingsService)App.Services!.GetService(typeof(ISettingsService))!;
        var vm = App.Services!.GetService(typeof(MainWindowViewModel)) as MainWindowViewModel;
        var win = new PreferencesWindow(new SettingsViewModel(settings), vm);
        Scale2(win);
        win.Show();
        Pump(10);
        if (shot.Bool("check")) CheckUpdates(settings);
        else if (shot.Str("update") is string stage) FakeUpdate(stage);
        Invoke(win, "Select", shot.Int("page") ?? 0);
        Pump(30);
        // A page's own switch (Downloads: "Sample Packs", "AI Models"); registries load from the
        // network, so give the list a moment.
        if (shot.Str("tab") is string sub)
        {
            ClickText(win, sub);
            for (int i = 0; i < 40; i++) Pump(5);
        }
        else for (int i = 0; i < 20; i++) Pump(5);
        Save(win, shot.Id);
        win.Close();
    }

    // ---- self-update (#58), through reflection so the harness builds against older apps ------

    private static Type? Updates => typeof(App).Assembly.GetType("Nota.App.AppUpdates");
    private static object? UpdatesShared => Updates?.GetProperty("Shared")!.GetValue(null);

    // Settings → Updates as after a check: automatic checks on, "the latest version".
    private static void CheckUpdates(ISettingsService settings)
    {
        if (UpdatesShared is not { } u) return;
        typeof(Settings).GetProperty("CheckForUpdates")!.SetValue(settings.Current, true);
        MakeInstallable();
        var task = (Task)Updates!.GetMethod("CheckAsync")!.Invoke(u, null)!;
        for (int i = 0; i < 200 && !task.IsCompleted; i++) Pump(1);
    }

    // This process is no packaged app: point the updater at a writable stand-in install, so
    // the UI shows what a real install shows (Update, not Download).
    private static void MakeInstallable()
    {
        var updater = App.Services!.GetService(typeof(IAppUpdater))!;
        var target = Path.Combine(Environment.GetEnvironmentVariable("NOTA_DATA_DIR")!, "install", "Nota.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "");
        updater.GetType().GetField("_target", Private)!.SetValue(updater, target);
    }

    // A newer release in the status bar: "available", "downloading" (two-thirds in) or "ready".
    private static void FakeUpdate(string stage)
    {
        if (UpdatesShared is not { } u) return;
        MakeInstallable();
        var v = AppInfo.Version.Split('.');
        var next = $"{v[0]}.{int.Parse(v[1]) + 1}.1";
        var release = new AppRelease(next, $"https://github.com/nota-daw/nota/releases/tag/v{next}",
            new AppReleaseAsset($"Nota-{next}-x86_64.AppImage", "https://example.invalid/nota", 182_000_000, null));
        var stages = Updates!.GetNestedType("Stage")!;
        Updates.GetProperty("Release")!.SetValue(u, release);
        Updates.GetProperty("State")!.SetValue(u, Enum.Parse(stages, stage, ignoreCase: true));
        Updates.GetProperty("Dismissed")!.SetValue(u, false);
        Updates.GetProperty("Progress")!.SetValue(u, new StoreProgress(0.66, $"Downloading Nota {next}…"));
        RaiseUpdatesChanged(u);
    }

    private static void HideUpdate()
    {
        if (UpdatesShared is not { } u || Updates!.GetProperty("Release")!.GetValue(u) is null) return;
        Updates.GetProperty("Release")!.SetValue(u, null);
        Updates.GetProperty("State")!.SetValue(u, Enum.ToObject(Updates.GetNestedType("Stage")!, 0));
        RaiseUpdatesChanged(u);
    }

    private static void RaiseUpdatesChanged(object u)
    {
        (Updates!.GetField("Changed", Private)!.GetValue(u) as Action)?.Invoke();
        Pump(5);
    }

    private void Start(Shot shot)
    {
        var recent = new List<RecentProjectItem>
        {
            new("Night Drive", "/Users/you/Documents/Nota Projects/Night Drive.nota", "Today, 14:20"),
            new("Glass Garden", "/Users/you/Documents/Nota Projects/Glass Garden.nota", "Yesterday"),
            new("Tape Loops 03", "/Users/you/Documents/Nota Projects/Tape Loops 03.nota", "3 Oct"),
            new("Sketch — 128 bpm", "/Users/you/Documents/Nota Projects/Sketch 128.nota", "28 Sep"),
        };
        Simple(shot, new WelcomeWindow(recent, () => { }, () => { }, _ => { }, () => { }, () => { }, true, _ => { }));
    }

    private void WhatsNew(Shot shot)
    {
        // What the app shows: a release's curated notes where it has them, else its changelog section.
        var entries = AppInfo.Changelog()
            .Where(e => e.Sections.Count > 0 && SemVer.TryParse(e.Version, out _)).Take(shot.Int("entries") ?? 1).ToList();
        Simple(shot, new WhatsNewWindow(entries));
    }

    private void Simple(Shot shot, Window win)
    {
        Scale2(win);
        win.Show();
        Pump(20);
        Save(win, shot.Id);
        win.Close();
    }

    /// <summary>The Connect phone popup (top bar's Remote button) with the server running, so
    /// it shows the QR, the address and the code. The address is scrubbed like any other.</summary>
    private void Connect(Shot shot)
    {
        var settings = (ISettingsService)App.Services!.GetService(typeof(ISettingsService))!;
        var remoteType = typeof(App).Assembly.GetType("Nota.App.Remote.RemoteService")
            ?? typeof(App).Assembly.GetTypes().First(t => t.Name == "RemoteService");
        var remote = App.Services!.GetService(remoteType)!;
        settings.Current.RemoteEnabled = true;
        var apply = (Task)remoteType.GetMethod("ApplyAsync")!.Invoke(remote, null)!;
        // ApplyAsync resumes on the UI thread, so pump it rather than block on it.
        for (int i = 0; i < 200 && !apply.IsCompleted; i++) Pump(1);
        var viewType = typeof(App).Assembly.GetTypes().First(t => t.Name == "ConnectPhoneView");
        var view = (Control)Activator.CreateInstance(viewType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new object[] { remote, settings, _engine }, null)!;
        var holder = new Border { Padding = new Thickness(0), Child = view, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var win = Host(holder, 400, shot.Int("height") ?? 640);
        win.Show();
        Pump(20);
        Save(win, shot.Id, view);
        win.Close();
        settings.Current.RemoteEnabled = false;
        var stop = (Task)remoteType.GetMethod("ApplyAsync")!.Invoke(remote, null)!;
        for (int i = 0; i < 200 && !stop.IsCompleted; i++) Pump(1);
    }

    /// <summary>Real audio / MIDI / gamepad devices on this machine show under stock names.</summary>
    private void RenameDevices()
    {
        T? Svc<T>() where T : class => App.Services!.GetService(typeof(T)) as T;
        void Map(IEnumerable<string> real, string[] stock)
        {
            int i = 0;
            foreach (var name in real.Distinct().OrderByDescending(n => n.Length))
                _redactor.Rename(name, i < stock.Length ? stock[i++] : $"{stock[^1]} {++i}");
        }
        if (Svc<IAudioDeviceService>() is { } audio)
        {
            Map(audio.OutputDevices().Select(d => d.Name), new[] { "MacBook Pro Speakers", "External Headphones", "USB Audio Interface" });
            Map(audio.InputDevices().Select(d => d.Name), new[] { "MacBook Pro Microphone", "USB Audio Interface In" });
        }
        if (Svc<IMidiDeviceService>() is { } midi)
            Map(midi.InputDevices().Select(d => d.Name), new[] { "USB MIDI Keyboard", "IAC Driver Bus 1", "MIDI Controller" });
        if (Svc<IGamepadService>() is { } pads)
            Map(pads.Pads().Select(d => d.Name), new[] { "Wireless Controller" });
    }

    // ── plumbing ───────────────────────────────────────────────────────────────────────

    private static Window Host(Control content, double w, double h)
    {
        var win = new Window { Width = w, Height = h, Content = content, Background = Brush.Parse(Theme("Brush.BgApp")) };
        Scale2(win);
        return win;
    }

    private static string Theme(string key)
        => Avalonia.Application.Current!.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var v) && v is ISolidColorBrush b
            ? b.Color.ToString() : "#0B0A09";

    /// <summary>Render the window's content at 2×. RenderTargetBitmap at 192 dpi drops templated
    /// parts, so scale the layout instead and capture the frame.</summary>
    private static void Scale2(Window win)
    {
        var content = win.Content as Control;
        win.Content = null;
        win.Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(2, 2), Child = content };
        // Double whichever dimension is fixed; SizeToContent grows the other with the content.
        if (win.SizeToContent is SizeToContent.Manual or SizeToContent.Height) win.Width *= 2;
        if (win.SizeToContent is SizeToContent.Manual or SizeToContent.Width) win.Height *= 2;
    }

    private static void Pump(int n = 10)
    {
        for (int i = 0; i < n; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(15);
        }
    }

    private static void Invoke(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethods(Private | BindingFlags.Public)
            .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length)
            ?? throw new MissingMethodException(target.GetType().Name, method);
        m.Invoke(target, args);
        Pump();
    }

    private static readonly Avalonia.Input.Pointer Mouse = new(1, PointerType.Mouse, true);

    /// <summary>Click the control that shows <paramref name="text"/> — a tab, segment or button.</summary>
    private static void ClickText(Window win, string text)
    {
        var tb = win.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(x => x.Text == text)
                 ?? throw new ArgumentException($"no '{text}' on screen");
        // A button (tab segments, Downloads' switch) takes a Click; anything else a pointer press.
        if (tb.GetVisualAncestors().OfType<Button>().FirstOrDefault() is { } button)
        {
            if (Environment.GetEnvironmentVariable("NOTA_SHOTS_DEBUG") != null) Console.WriteLine($"click {text}: {button.GetType().Name}");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump();
            return;
        }
        var target = (Control?)tb.Parent ?? tb;
        var p = new Point(3, 3);
        target.RaiseEvent(new PointerPressedEventArgs(target, Mouse, target, p, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
        target.RaiseEvent(new PointerReleasedEventArgs(target, Mouse, target, p, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
        Pump();
    }

    /// <summary>Capture the window and save it (or just <paramref name="crop"/>'s area) as PNG.</summary>
    private void Save(Window win, string id, Control? crop = null, IReadOnlyDictionary<string, string>? renames = null)
    {
        Pump(5);
        _redactor.Apply(win, renames);
        Pump(3);
        var path = Path.Combine(_out, id + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var frame = win.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame rendered");
        frame.Save(path);
        if (crop == null) return;

        var origin = crop.TranslatePoint(new Point(0, 0), win) ?? throw new InvalidOperationException("crop target not in window");
        var end = crop.TranslatePoint(new Point(crop.Bounds.Width, crop.Bounds.Height), win)!.Value;
        using var full = SKBitmap.Decode(path);
        var rect = SKRectI.Round(new SKRect((float)origin.X, (float)origin.Y, (float)end.X, (float)end.Y));
        rect.Intersect(new SKRectI(0, 0, full.Width, full.Height));
        using var part = new SKBitmap();
        if (!full.ExtractSubset(part, rect)) throw new InvalidOperationException($"bad crop {rect}");
        using var img = SKImage.FromBitmap(part);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.Create(path);
        data.SaveTo(fs);
    }
}
