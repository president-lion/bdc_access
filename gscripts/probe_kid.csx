using System;
using System.Linq;
using UndertaleModLib.Models;

foreach (var rn in new[] { "Lvl_Memories_Room_Kid", "Lvl_Memories_Trash", "Lvl_Memories_Item_Holder", "Lvl_Memories_Kitchen", "Lvl_Memories_Road" })
{
    var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
    if (r == null) { Console.WriteLine(rn + ": no such room"); continue; }
    Console.WriteLine("== " + rn + "  (" + r.GameObjects.Count + " instances)");
    foreach (var g in r.GameObjects.OrderBy(g => g.X))
        Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-44} x={g.X,5} y={g.Y,5}");
}
