using System;
using System.Linq;
EnsureDataLoaded();
string[] want = { "Flat_B_Kitchen_Drawer", "Flat_B_Kitchen_Drawer_Mask", "Flat_Kitchen_Stamps",
                  "Flat_Kitchen_Letter", "Flat_Coin_5", "Item_Coin_5" };
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content)
                .Where(n => n != null && want.Contains(n)).Distinct().ToList();
    if (hits.Count > 0) Console.WriteLine(r.Name.Content + ": " + string.Join(", ", hits));
}
