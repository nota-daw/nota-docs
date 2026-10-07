// Nota docs screenshot harness. Renders real Nota UI headlessly at 2× and writes one PNG per
// shot. Driven by shots/shoot.mjs:
//
//   dotnet run -- <job.json>
//
// job.json: { "out": "<dir>", "theme": "dark" | "light", "shots": [ { "id", "scene", "args" } ] }
// Each shot lands at <out>/<id>.png. Scenes are in Scenes.cs; the demo song in Demo.cs.
// NOTA_DATA_DIR must point at a scratch folder (the runner sets it) so nothing touches the
// user's real settings, plug-in lists or sample library.

using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Nota.App;
using Nota.Application;
using NotaDocsShots;

if (args.Length < 1) { Console.Error.WriteLine("usage: NotaDocsShots <job.json>"); return 2; }
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOTA_DATA_DIR")))
{
    Console.Error.WriteLine("NOTA_DATA_DIR is not set — refusing to run against the real user data.");
    return 2;
}

var job = JsonSerializer.Deserialize<Job>(File.ReadAllText(args[0]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false }).WithNotaFonts().SetupWithoutStarting();

var scenes = new Scenes(job.Out, job.Theme == "light" ? AppTheme.Light : AppTheme.Dark);
int failed = 0;
foreach (var shot in job.Shots)
{
    try
    {
        scenes.Run(shot);
        Console.WriteLine($"ok     {shot.Id}");
    }
    catch (Exception e)
    {
        failed++;
        Console.WriteLine($"FAILED {shot.Id}: {(e.InnerException ?? e).Message}");
    }
}
return failed == 0 ? 0 : 1;

namespace NotaDocsShots
{
    public sealed record Job(string Out, string Theme, List<Shot> Shots);

    public sealed record Shot(string Id, string Scene, Dictionary<string, JsonElement>? Args)
    {
        public string? Str(string key) => Args != null && Args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        public int? Int(string key) => Args != null && Args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        public bool Bool(string key) => Args != null && Args.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.True;
    }
}
