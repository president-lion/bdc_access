using System;
using System.Linq;
EnsureDataLoaded();
string[] want = { "Forest_Swamp_Worm", "Forest_Fishing_Rod_Standing", "Forest_Fishing_Rod",
                  "Forest_Lake_Fishes", "Bridge_Plastic_Bait", "Forest_Float" };
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content)
                .Where(n => n != null && want.Contains(n)).Distinct().ToList();
    if (hits.Count > 0) Console.WriteLine(r.Name.Content + ": " + string.Join(", ", hits));
}
