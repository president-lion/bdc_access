using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    if (r.Name.Content != "Lvl_Graveyard_Crypt_Inside") continue;
    foreach (var g in r.GameObjects.GroupBy(o => o.ObjectDefinition?.Name?.Content).OrderBy(g => g.Key))
        Console.WriteLine("   " + g.Count() + "  " + g.Key);
}
