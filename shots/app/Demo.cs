using Nota.Application;

namespace NotaDocsShots;

/// <summary>
/// "Night Drive" — the demo song every main-window screenshot shows: 124 BPM, A minor, 32 bars,
/// a drum/bass group, keys, a lead, an audio texture and a reverb return, plus a few Session
/// scenes. Audio is synthesised here, so the docs never depend on files outside the repo.
/// </summary>
public sealed class Demo
{
    private const int Rate = 48000;
    private const double Bpm = 124;
    private readonly IAudioEngine _engine;
    private readonly string _dir;
    private readonly Dictionary<string, int> _tracks = new(StringComparer.OrdinalIgnoreCase);

    public Demo(IAudioEngine engine, string dir)
    {
        _engine = engine;
        _dir = dir;
        Directory.CreateDirectory(dir);
    }

    public string Folder => _dir;

    public int Track(string name) => _tracks.TryGetValue(name, out var id) ? id : throw new ArgumentException($"no demo track '{name}'");

    /// <summary>Path of a generated sample ("pad", "kick", "hat", "chords").</summary>
    public string Sample(string name)
    {
        var path = Path.Combine(_dir, name + ".wav");
        if (!File.Exists(path)) WriteWav(path, name switch
        {
            "kick" => Kick(),
            "hat" => Hat(),
            "chords" => Chords(new[] { 53, 57, 60 }, 4),
            _ => Chords(new[] { 57, 60, 64, 69 }, 8),
        });
        return path;
    }

    /// <summary>A small library in the Samples folder, so the browser's Files tab isn't empty.</summary>
    public void WriteSamples(string folder)
    {
        foreach (var (sub, file, src) in new[]
        {
            ("Drums", "Kick 808.wav", "kick"), ("Drums", "Hat closed.wav", "hat"),
            ("Loops", "Pad Am 124 bpm.wav", "pad"), ("Loops", "Chords F 124 bpm.wav", "chords"),
        })
        {
            var dir = Path.Combine(folder, sub);
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, file);
            if (!File.Exists(dst)) File.Copy(Sample(src), dst);
        }
    }

    /// <summary>Build the song in four steps, calling <paramref name="save"/> after each so the
    /// project gets a real version history (History tab). <paramref name="tempo"/> sets the BPM
    /// through the transport view model, which is what a save records.</summary>
    public void Build(Action<string?> save, Action<double> tempo)
    {
        _engine.SetTimeSignature(4, 4);
        tempo(120);

        // 1 · a beat and a bass line.
        int drums = Instrument("Drums", 12, 0);
        int bass = Instrument("Bass", 7, 2);
        for (int bar = 4; bar < 32; bar += 4) Clip(drums, bar, 4, DrumBar(full: bar >= 16), "Beat");
        for (int bar = 8; bar < 32; bar += 4) Clip(bass, bar, 4, BassLine(), "Bass");
        _engine.AddBuiltinDevice(bass, 1);      // Compressor
        save("First beat");

        // 2 · chords.
        int keys = Instrument("Keys", 14, 3);
        for (int bar = 0; bar < 32; bar += 4) Clip(keys, bar, 4, ChordProgression(), "Chords");
        _engine.AddBuiltinDevice(keys, 0);      // EQ-8
        _engine.AddBuiltinDevice(keys, 20);     // Chamber
        save(null);

        // 3 · faster.
        tempo(Bpm);
        save(null);

        // 4 · hook, texture, a reverb return, a group and the Session scenes.
        int lead = Instrument("Lead", 6, 6);
        int texture = _engine.AddAudioTrack();
        Name(texture, "Texture", 4);
        int verb = _engine.AddReturnTrack();
        Name(verb, "Reverb", 8);
        _engine.AddBuiltinDevice(verb, 2);
        int group = _engine.CreateGroup(new[] { drums, bass });
        if (group > 0) Name(group, "Rhythm", 0);
        _engine.AddBuiltinDevice(lead, 3);      // Delay
        _engine.SetTrackSend(keys, 0, 0.35f);
        _engine.SetTrackSend(lead, 0, 0.25f);
        _engine.SetTrackVolume(texture, 0.6f);
        _engine.SetTrackPan(lead, 0.15f);
        Clip(lead, 8, 8, Melody(), "Hook");
        Clip(lead, 24, 8, Melody(), "Hook");
        foreach (var at in new[] { 0, 64 })
            _engine.SetClipName(texture, _engine.AddAudioClip(texture, Sample("pad"), at), "Pad");

        // Automation: the hook fades in over its first phrase and dips at the turnaround.
        int lane = _engine.AddAutomationLane(lead, AutomationTarget.Volume, -1, -1);
        if (lane >= 0)
            _engine.SetAutomationPoints(lead, lane, new[]
            {
                new AutomationPoint(32, 0.25f), new AutomationPoint(48, 0.85f, 0.4f),
                new AutomationPoint(60, 0.85f), new AutomationPoint(64, 0.45f), new AutomationPoint(96, 0.8f, -0.3f),
            });

        while (_engine.SceneCount < 3) _engine.AddScene();
        string[] scenes = { "Intro", "Verse", "Drop" };
        for (int s = 0; s < 3; s++)
        {
            _engine.SetSceneName(s, scenes[s]);
            Slot(keys, s, ChordProgression(), "Chords");
            if (s >= 1) { Slot(drums, s, DrumBar(full: s == 2), s == 2 ? "Full beat" : "Beat"); Slot(bass, s, BassLine(), "Bass"); }
            if (s == 2) Slot(lead, s, Melody().Where(n => n.StartBeat < 16).ToArray(), "Hook");
        }
        save("Hook and texture");

        if (Environment.GetEnvironmentVariable("NOTA_SHOTS_DEBUG") != null)
            for (int i = 0; _engine.TryGetTrackInfo(i, out var ti); i++)
                Console.WriteLine($"track[{i}] id={ti.Id} type={ti.Type} clips={ti.ClipCount} group={ti.GroupId}");
    }

    // ── tracks and clips ────────────────────────────────────────────────────────────────

    private int Instrument(string name, int kind, int color)
    {
        int t = _engine.AddInstrumentTrack();
        _engine.SetTrackBuiltinInstrument(t, kind);
        Name(t, name, color);
        return t;
    }

    private void Name(int track, string name, int color)
    {
        _engine.SetTrackName(track, name);
        _engine.SetTrackColorIndex(track, color);
        _tracks[name] = track;
    }

    private void Clip(int track, int bar, int bars, NotaNote[] notes, string name)
    {
        int c = _engine.AddMidiClip(track, bar * 4, bars * 4);
        _engine.SetClipNotes(track, c, notes.Where(n => n.StartBeat < bars * 4).ToArray());
        _engine.SetClipName(track, c, name);
    }

    private void Slot(int track, int scene, NotaNote[] notes, string name)
    {
        _engine.AddSessionMidiClip(track, scene, 16);
        _engine.SetSessionNotes(track, scene, notes);
        _engine.SetSessionClipName(track, scene, name);
    }

    private static NotaNote N(int pitch, double start, double len, float vel = 0.8f) => new(pitch, start, len, vel);

    private static NotaNote[] DrumBar(bool full)
    {
        var l = new List<NotaNote>();
        for (int b = 0; b < 4; b++)
        {
            double o = b * 4;
            l.Add(N(36, o, 0.25)); l.Add(N(36, o + 2, 0.25));
            if (full) l.Add(N(36, o + 2.75, 0.25, 0.6f));
            l.Add(N(38, o + 1, 0.25)); l.Add(N(38, o + 3, 0.25));
            for (int h = 0; h < 8; h++) l.Add(N(42, o + h * 0.5, 0.2f, h % 2 == 0 ? 0.7f : 0.45f));
        }
        return l.ToArray();
    }

    private static readonly int[][] Chords4 = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };

    private static NotaNote[] ChordProgression()
        => Chords4.SelectMany((ch, i) => ch.Select(p => N(p, i * 4, 3.75, 0.7f))).ToArray();

    private static NotaNote[] BassLine()
    {
        int[] roots = { 33, 29, 36, 31 };
        return roots.SelectMany((r, i) => new[] { N(r, i * 4, 0.75), N(r, i * 4 + 1.5, 0.5, 0.6f), N(r + 12, i * 4 + 2.5, 0.5, 0.7f), N(r, i * 4 + 3, 0.75) }).ToArray();
    }

    private static NotaNote[] Melody()
    {
        int[] p = { 69, 72, 76, 74, 72, 69, 67, 69, 72, 74, 76, 79, 76, 74, 72, 71 };
        return p.Select((x, i) => N(x, i * 2, i % 4 == 3 ? 1.75 : 1.0, 0.75f)).ToArray();
    }

    // ── audio ───────────────────────────────────────────────────────────────────────────

    private static float[] Chords(int[] midi, int beats)
    {
        int n = (int)(Rate * 60 / Bpm * beats);
        var s = new float[n];
        var rnd = new Random(7);
        foreach (var m in midi)
        {
            double f = 440 * Math.Pow(2, (m - 69) / 12.0);
            for (int v = -1; v <= 1; v++)
            {
                double fd = f * (1 + v * 0.004), ph = rnd.NextDouble();
                for (int i = 0; i < n; i++)
                {
                    double t = i / (double)Rate;
                    double env = Math.Min(1, t / 0.6) * Math.Min(1, (n - i) / (Rate * 0.8));
                    double saw = 2 * ((t * fd + ph) % 1) - 1;
                    s[i] += (float)(env * (0.6 * Math.Sin(2 * Math.PI * fd * t) + 0.15 * saw) * (1 + 0.3 * Math.Sin(2 * Math.PI * 0.25 * t)));
                }
            }
        }
        return Normalize(s, 0.5f);
    }

    private static float[] Kick()
    {
        int n = Rate / 2;
        var s = new float[n];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)Rate;
            ph += 2 * Math.PI * (45 + 110 * Math.Exp(-t * 30)) / Rate;
            s[i] = (float)(Math.Sin(ph) * Math.Exp(-t * 7));
        }
        return Normalize(s, 0.9f);
    }

    private static float[] Hat()
    {
        int n = Rate / 8;
        var s = new float[n];
        var rnd = new Random(3);
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(rnd.NextDouble() * 2 - 1);
            s[i] = (w - prev) * (float)Math.Exp(-i / (double)Rate * 45);
            prev = w;
        }
        return Normalize(s, 0.6f);
    }

    private static float[] Normalize(float[] s, float peak)
    {
        float m = s.Max(Math.Abs);
        if (m > 0) for (int i = 0; i < s.Length; i++) s[i] *= peak / m;
        return s;
    }

    private static void WriteWav(string path, float[] mono)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = mono.Length * 2 * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(bytes);
        foreach (var x in mono)
        {
            short v = (short)Math.Clamp(x * 32767f, -32768f, 32767f);
            w.Write(v); w.Write(v);
        }
    }
}
