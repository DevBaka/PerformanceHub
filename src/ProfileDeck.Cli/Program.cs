using ProfileDeck.Core.Audio;
using ProfileDeck.Core.Display;
using ProfileDeck.Core.Engine;
using ProfileDeck.Core.Logging;
using ProfileDeck.Core.Models;
using ProfileDeck.Core.Persistence;
using ProfileDeck.Core.Services;

AppPaths.EnsureCreated();
var log = new FileLogSink();
log.EntryLogged += e => Console.WriteLine($"[{e.Level}] {e.Message}");

var displayManager = new DisplayManager();
var displayRepo = new ProfileRepository<DisplayProfile>(AppPaths.DisplayProfilesDir, p => p.Name);
var systemRepo = new ProfileRepository<SystemProfile>(AppPaths.SystemProfilesDir, p => p.Name);
var engine = new SystemProfileEngine(log, displayManager, displayRepo);

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

// `ProfileDeck.exe --profile "DJ"` - direct trigger for Stream Deck / shortcuts, no UI.
if (string.Equals(args[0], "--profile", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2) { Console.WriteLine("Profilname fehlt: --profile \"Name\""); return 1; }
    var profile = systemRepo.GetByName(args[1]);
    if (profile == null) { Console.WriteLine($"System-Profil '{args[1]}' nicht gefunden."); return 2; }
    var applyResult = engine.Apply(profile, dryRun: args.Contains("--dry-run"));
    return applyResult.Success ? 0 : 3;
}

switch (args[0].ToLowerInvariant())
{
    case "display" when args.Length >= 2 && args[1] == "list":
    {
        var topo = displayManager.GetCurrentTopology();
        Console.WriteLine($"{topo.Count} Ziel(e) gefunden:\n");
        foreach (var d in topo)
            Console.WriteLine("  " + d);
        return 0;
    }

    case "display" when args.Length >= 3 && args[1] == "capture":
    {
        var name = args[2];
        var profile = displayManager.CaptureCurrentAsProfile(name);
        displayRepo.Save(profile);
        Console.WriteLine($"Aktuelle Topologie als Display-Profil '{name}' gespeichert ({profile.Displays.Count(d => d.Active)} aktive Displays).");
        return 0;
    }

    case "display" when args.Length >= 3 && args[1] == "apply":
    {
        var name = args[2];
        var dryRun = args.Contains("--dry-run");
        var profile = displayRepo.GetByName(name);
        if (profile == null) { Console.WriteLine($"Display-Profil '{name}' nicht gefunden."); return 2; }
        var result = displayManager.Apply(profile, log, dryRun);
        return result.Success ? 0 : 3;
    }

    case "display" when args.Length >= 2 && args[1] == "profiles":
    {
        foreach (var p in displayRepo.GetAll())
            Console.WriteLine($"  {p.Name} ({p.Displays.Count(d => d.Active)} aktiv)");
        return 0;
    }

    case "system" when args.Length >= 3 && args[1] == "apply":
    {
        var name = args[2];
        var dryRun = args.Contains("--dry-run");
        var profile = systemRepo.GetByName(name);
        if (profile == null) { Console.WriteLine($"System-Profil '{name}' nicht gefunden."); return 2; }
        var result = engine.Apply(profile, dryRun);
        return result.Success ? 0 : 3;
    }

    case "system" when args.Length >= 2 && args[1] == "profiles":
    {
        foreach (var p in systemRepo.GetAll())
            Console.WriteLine($"  {p.Name}");
        return 0;
    }

    case "restore":
    {
        var result = engine.RestorePrevious();
        return result.Success ? 0 : 3;
    }

    case "audio" when args.Length >= 2 && args[1] == "list":
    {
        var audio = new AudioDeviceService();
        Console.WriteLine("Ausgabe:");
        foreach (var d in audio.GetOutputDevices()) Console.WriteLine($"  {d.Name} [{d.Id}]{(d.IsDefault ? " (Standard)" : "")}");
        Console.WriteLine("Eingabe:");
        foreach (var d in audio.GetInputDevices()) Console.WriteLine($"  {d.Name} [{d.Id}]{(d.IsDefault ? " (Standard)" : "")}");
        return 0;
    }

    case "services" when args.Length >= 2 && args[1] == "list":
    {
        var svc = new ServiceControlService();
        var filter = args.Length >= 3 ? args[2] : null;
        foreach (var s in svc.ListAll())
        {
            if (filter != null && !s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !s.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;
            Console.WriteLine($"  {s.Name,-30} {s.Status,-12} {s.StartupType,-12} {s.DisplayName}");
        }
        return 0;
    }

    default:
        PrintUsage();
        return 1;
}

static void PrintUsage()
{
    Console.WriteLine("""
    ProfileDeck CLI

      --profile "Name"              System-Profil ohne UI anwenden (z.B. per Stream Deck)
      --profile "Name" --dry-run    Nur simulieren/loggen

      display list                  Alle erkannten Monitore mit Status anzeigen
      display capture <name>        Aktuelle Anordnung als Display-Profil speichern
      display apply <name>          Display-Profil anwenden
      display apply <name> --dry-run
      display profiles              Gespeicherte Display-Profile auflisten

      system apply <name>           System-Profil anwenden
      system apply <name> --dry-run
      system profiles               Gespeicherte System-Profile auflisten

      restore                       Letzten Snapshot wiederherstellen ("Restore Previous")

      audio list                    Audiogeraete (Ausgabe/Eingabe) mit Id auflisten
      services list [Filter]        Windows-Dienste mit Status/Starttyp auflisten
    """);
}
