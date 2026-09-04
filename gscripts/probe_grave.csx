using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    var names = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content).Where(n=>n!=null).ToList();
    if (!names.Any(n => n == "Graveyard_Pickaxe" || n == "Graveyard_Cementery_Gtave" || n == "Graveyard_Cementery_Pickaxe")) continue;
    Console.WriteLine("=== " + r.Name.Content);
    foreach (var g in names.GroupBy(n=>n).OrderBy(g=>g.Key))
        Console.WriteLine($"   {g.Count(),3}  {g.Key}");
}
