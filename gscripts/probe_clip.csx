using System;
using System.Linq;
EnsureDataLoaded();
string[] names = { "Bridge_Clip", "Bridge_06_Mask", "Bridge_Coin_Item" };
foreach (var n in names)
{
    var o = Data.GameObjects.ByName(n);
    Console.WriteLine($"{n}: sprite={(o.Sprite == null ? "NONE" : o.Sprite.Name.Content)} " +
                      $"visible={o.Visible} parent={(o.ParentId == null ? "-" : o.ParentId.Name.Content)}");
}
Console.WriteLine();
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Where(g => names.Contains(g.ObjectDefinition?.Name?.Content)).ToList();
    if (hits.Count == 0) continue;
    Console.WriteLine(r.Name.Content + ":");
    foreach (var g in hits)
        Console.WriteLine($"    {g.ObjectDefinition.Name.Content,-18} at ({g.X}, {g.Y})");
}
