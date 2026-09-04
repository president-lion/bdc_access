using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    var n = r.Name.Content;
    if (n != "Lvl_Computer" && n != "Lvl_Hospital_Reception") continue;
    Console.WriteLine("=== " + n);
    foreach (var g in r.GameObjects.GroupBy(o => o.ObjectDefinition?.Name?.Content).OrderBy(g=>g.Key))
        Console.WriteLine($"   {g.Count(),3}  {g.Key}");
}
