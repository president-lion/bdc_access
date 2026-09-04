using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    if (r.Name.Content != "Lvl_Flat_Board") continue;
    Console.WriteLine("=== " + r.Name.Content);
    foreach (var g in r.GameObjects.GroupBy(o => o.ObjectDefinition?.Name?.Content).OrderBy(g => g.Key))
        Console.WriteLine("   " + g.Count() + "  " + g.Key);
}
