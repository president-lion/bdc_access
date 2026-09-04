using System;
using System.Linq;
EnsureDataLoaded();
var wire = Data.GameObjects.ByName("Bridge_Wire");
foreach (var r in Data.Rooms)
{
    int n = r.GameObjects.Count(g => g.ObjectDefinition == wire);
    if (n > 0)
    {
        Console.WriteLine($"{r.Name.Content}: {n} Bridge_Wire instance(s)");
        foreach (var g in r.GameObjects.Where(g => g.ObjectDefinition == wire))
            Console.WriteLine($"    at ({g.X}, {g.Y})");
    }
}
Console.WriteLine();
Console.WriteLine("Bridge_Wire sprite: " + (wire.Sprite == null ? "none" : wire.Sprite.Name.Content));
