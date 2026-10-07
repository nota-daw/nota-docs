using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace NotaDocsShots;

/// <summary>
/// Scrubs the machine the shots are taken on out of every frame before it is saved: the user's
/// name and computer name, local IP addresses, real audio / MIDI device names and the scratch
/// data folder. Purely visual — it rewrites on-screen text, never settings, so nothing reaches
/// the user's real audio.json / midi.json.
/// </summary>
public sealed class Redactor
{
    private readonly List<(Regex Pattern, string Replacement)> _rules = new();

    public Redactor(string dataDir)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // Paths first, longest first: the scratch folders map onto the folders a user would see.
        AddLiteral(Path.Combine(dataDir, "Nota Samples"), "~/Music/Nota Samples");
        AddLiteral(Path.Combine(dataDir, "Nota Projects"), "~/Documents/Nota Projects");
        AddLiteral(Path.Combine(dataDir, "demo"), "~/Documents/Nota Projects");
        AddLiteral(dataDir, "~/Library/Application Support/Nota");
        AddLiteral(home, "~");

        // Who and where: full name, account name, computer and host names.
        foreach (var name in new[] { Run("scutil", "--get", "ComputerName"), Run("scutil", "--get", "LocalHostName"), Environment.MachineName })
            if (name.Length > 2) AddLiteral(name, "Studio Mac");
        var full = Run("id", "-F");
        foreach (var token in full.Split(' ', StringSplitOptions.RemoveEmptyEntries).Append(Environment.UserName).Where(t => t.Length > 2))
        {
            // "Alex's AirPods" → "AirPods"; any other mention → "you".
            _rules.Add((new Regex($@"\b{Regex.Escape(token)}[’']s\s+", RegexOptions.IgnoreCase), ""));
            _rules.Add((new Regex($@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase), "you"));
        }
        _rules.Add((new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b"), "192.168.1.20"));
    }

    /// <summary>Show a real device under a stock name ("MacBook Pro Speakers"). Device renames
    /// run before the other rules so a name is replaced whole, not piecemeal.</summary>
    public void Rename(string real, string shown)
    {
        if (!string.IsNullOrWhiteSpace(real) && real != shown) _rules.Insert(0, (new Regex(Regex.Escape(real)), shown));
    }

    private void AddLiteral(string text, string replacement)
    {
        if (!string.IsNullOrWhiteSpace(text)) _rules.Add((new Regex(Regex.Escape(text), RegexOptions.IgnoreCase), replacement));
    }

    /// <summary>Apply the rules to every text on screen, then any scene-specific renames.</summary>
    public void Apply(Window win, IReadOnlyDictionary<string, string>? renames = null)
    {
        foreach (var tb in win.GetVisualDescendants().OfType<TextBlock>())
            if (tb.Text is { Length: > 0 } t) tb.Text = Scrub(t, renames);
        foreach (var box in win.GetVisualDescendants().OfType<TextBox>())
            if (box.Text is { Length: > 0 } t) box.Text = Scrub(t, renames);
    }

    public string Scrub(string text, IReadOnlyDictionary<string, string>? renames = null)
    {
        if (renames != null && renames.TryGetValue(text, out var renamed)) return renamed;
        foreach (var (pattern, replacement) in _rules) text = pattern.Replace(text, replacement);
        return text;
    }

    private static string Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            var s = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return s;
        }
        catch { return ""; }
    }
}
