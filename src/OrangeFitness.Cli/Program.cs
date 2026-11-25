// Command-line runner for checking the workflow without the UI.
//   OrangeFitness.Cli [settings.json] [--live]
// Without --live it always runs in test mode (no POSTs to the site).
using System.Text;
using OrangeFitness.Core;

Console.OutputEncoding = Encoding.UTF8;

var live = args.Contains("--live");
var path = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "settings.json";
var settings = SettingsStore.Load(path);
if (!live) settings.TestMode = true;

Console.WriteLine($"Settings: {Path.GetFullPath(path)}{(File.Exists(path) ? "" : " (not found – defaults)")}");
var service = new BookingService(settings, Console.WriteLine);
var results = await service.RunAsync();
return results.Count > 0 && results.All(r => r.Ok) ? 0 : 1;
