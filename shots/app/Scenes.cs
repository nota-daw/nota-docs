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
///   device    instrument | effect | midi (kind id), preset, tab, card (index in the chain)
///   main      view (arrangement | session | modular), select (track name), clip (track name,
///             + clipIndex, audio: true for an audio clip) to open in the clip editor, automation, tab (browser tab index), detailHeight, element, width, height
///   palette   the command palette (⌘⇧P) over the main window: view, select (track), card (index of
///             an audio effect to select in Devices), query, down (rows to move), mods (alt | cmd | shift),
///             origin (devices: the Devices panel had the focus), crop (true: just the palette card)
///   mixer     width, height
///   prefs     page (index in Settings' sidebar, 0 = Audio), tab (a switch on the page)
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
        typeof(App).Assembly.GetType("Nota.App.NotaThemeService")!.GetMethod("Set")!.Invoke(null, new object[] { theme });
        _redactor = new Redactor(data);
        RenameDevices();
        _demo = new Demo(_engine, Path.Combine(data, "demo"));
        _demo.WriteSamples(settings.Current.SamplesFolder);
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
            deviceIndex = _engine.AddBuiltinDevice(track, fx);
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
        if (shot.Str("preset") is string preset)
        {
            var result = deviceIndex >= 0 && shot.Int("midi") is null
                ? _presets.ApplyInPlace(_engine, preset, track, deviceIndex)
                : _presets.Apply(_engine, preset, track);
            if (!string.IsNullOrEmpty(result) && result.StartsWith("Unknown")) throw new ArgumentException($"{result} ({preset})");
        }
        // A little audio through the device so meters, spectra and envelopes have something to draw.
        _engine.Play();
        var buf = new float[1024 * 2];
        for (int k = 0; k < 40; k++) _engine.RenderOffline(buf, 1024);
        _engine.StopTransport();

        var view = new DeviceChainView(_engine, _presets, null, Kits);
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
        var chain = view.GetVisualDescendants().OfType<StackPanel>().First(p => p.Orientation == Orientation.Horizontal && p.Children.Count > 0 && p.Bounds.Height > 200);
        var card = chain.Children[shot.Int("card") ?? 0];
        Save(win, shot.Id, card);
        win.Close();
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
        }
        else Invoke(mw, "OnCloseDetail", null, new RoutedEventArgs());
        if (shot.Str("clip") is string clipTrack)
        {
            // Open a clip of a demo track in the clip editor, as a double-click on it would.
            var timeline = mw.FindControl<ArrangementView>("Timeline")!;
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
        typeof(BrowserView).GetMethod("SelectTab", Private)!.Invoke(mw.FindControl<BrowserView>("Browser")!, new object[] { shot.Int("tab") ?? 0 });
        // Zoom: fit the whole song, or the app's default (28 px a beat from bar 1) — the view
        // keeps its zoom between shots otherwise.
        var tl = mw.FindControl<ArrangementView>("Timeline")!;
        if (shot.Bool("fit")) Invoke(tl, "ZoomToFitProject");
        else Invoke(tl, "ApplyOverviewView", 28.0, 0.0);
        Pump(20);
        var element = shot.Str("element") is string el ? mw.FindControl<Control>(el) ?? throw new ArgumentException($"no control '{el}'") : null;
        Save(mw, shot.Id, element);
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
        var repo = Environment.GetEnvironmentVariable("NOTA_REPO") ?? throw new ArgumentException("NOTA_REPO not set");
        var entries = ChangelogParser.Parse(File.ReadAllText(Path.Combine(repo, "CHANGELOG.md")))
            .Where(e => e.Sections.Count > 0 && !e.Version.Contains("Unreleased", StringComparison.OrdinalIgnoreCase)).Take(shot.Int("entries") ?? 1).ToList();
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
