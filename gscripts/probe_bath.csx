using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    var n = r.Name.Content;
    if (n != "Lvl_Flat_Bathroom" && n != "Lvl_Flat_B_Bathroom") continue;
    Console.WriteLine("=== " + n + "  instances=" + r.GameObjects.Count);
    foreach (var g in r.GameObjects.GroupBy(o => o.ObjectDefinition?.Name?.Content).OrderBy(g => g.Key))
        Console.WriteLine("   " + g.Count() + "  " + g.Key);
}
