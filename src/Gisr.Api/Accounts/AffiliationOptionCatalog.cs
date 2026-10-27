using Gisr.Shared.Accounts;

namespace Gisr.Api.Accounts;

public static class AffiliationOptionCatalog
{
    public static IReadOnlyList<AffiliationOptionGroupDto> Groups { get; } =
    [
        new("Governments & Political Organizations",
        [
            "Galactic Republic", "Galactic Empire", "New Republic", "First Order", "Resistance",
            "Confederacy of Independent Systems (Separatists)", "Sith Eternal", "Galactic Alliance", "Hutt Cartel",
        ]),
        new("Force Traditions",
        [
            "Jedi Order", "Jedi Praxeum", "Sith Order", "Nightsisters", "Guardians of the Whills", "Bendu followers", "Church of the Force",
        ]),
        new("Military Organizations",
        [
            "Republic Military", "Grand Army of the Republic", "Imperial Navy", "Imperial Army", "Stormtrooper Corps",
            "501st Legion", "Rebel Alliance Fleet", "Resistance Starfighter Corps", "Mandalorian Protectors",
        ]),
        new("Criminal Organizations",
        [
            "Black Sun", "Crimson Dawn", "Pyke Syndicate", "Hutt Cartel", "Crymorah Syndicate", "Zann Consortium", "Guavian Death Gang",
        ]),
        new("Cultural & Planetary Groups",
        [
            "Mandalorians", "Death Watch", "Children of the Watch", "Tusken Raiders", "Ewok Tribes", "Wookiees of Kashyyyk",
            "Naboo Royal Court", "Gungan Grand Army",
        ]),
        new("Corporate Organizations",
        [
            "Trade Federation", "Techno Union", "Corporate Alliance", "Banking Clan", "Czerka Corporation", "Kuat Drive Yards",
        ]),
        new("Notable Special Groups",
        [
            "Rebel Alliance", "Rogue Squadron", "Inquisitorius", "Imperial Security Bureau (ISB)", "Knights of Ren",
            "Bad Batch (Clone Force 99)", "Phoenix Cell", "Spectres",
        ]),
    ];

    private static readonly HashSet<string> Names = Groups.SelectMany(group => group.Names).ToHashSet(StringComparer.Ordinal);

    public static bool Contains(string? name) => name is not null && Names.Contains(name);
}
