using System;
using System.Linq;
EnsureDataLoaded();
string[] want = { "Graveyard_Flower", "Graveyard_Glove", "Item_Flower", "Graveyard_Ivy",
                  "Graveyard_Flower_Lady", "Graveyard_Crypt_Flower" };
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content)
                .Where(n => n != null && want.Contains(n)).Distinct().ToList();
    if (hits.Count > 0) Console.WriteLine($"{r.Name.Content}: {string.Join(", ", hits)}");
}
