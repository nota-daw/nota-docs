// Finds text a window shows cut off: a single-line TextBlock whose text is wider than the box
// it is drawn in (a fixed-width tag, a column, a trimmed label), or that runs past an ancestor
// that clips. Used to review a translation: NOTA_SHOTS_CLIPS=<file> appends one line per
// finding ("<shot id>\t<what>\t<text>") while the scenes render.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace NotaDocsShots;

internal static class ClippedText
{
    public static void Report(Window win, string id)
    {
        if (Environment.GetEnvironmentVariable("NOTA_SHOTS_CLIPS") is not { Length: > 0 } file) return;
        var lines = new List<string>();
        foreach (var tb in win.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || string.IsNullOrWhiteSpace(tb.Text) || tb.Bounds.Width < 1) continue;
            if (tb.TextWrapping != TextWrapping.NoWrap) continue;
            double natural = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, null).Width;
            string text = tb.Text.Replace('\n', ' ');
            if (natural > tb.Bounds.Width + 1)
            {
                lines.Add($"{id}\tbox {tb.Bounds.Width:0}px < text {natural:0}px\t{text}");
                continue;
            }
            // Past an ancestor that clips (a card, a panel, the window).
            foreach (var a in tb.GetVisualAncestors().OfType<Control>())
            {
                if (!a.ClipToBounds && a is not Window) continue;
                var tl = tb.TranslatePoint(new Point(0, 0), a);
                if (tl is { } p && p.X + natural > a.Bounds.Width + 1 && p.X < a.Bounds.Width)
                {
                    lines.Add($"{id}\tpast {a.GetType().Name} ({a.Bounds.Width:0}px)\t{text}");
                    break;
                }
            }
        }
        if (lines.Count > 0) File.AppendAllLines(file, lines.Distinct());
    }
}
