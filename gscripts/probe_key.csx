using System;
using System.Linq;
var want = new[] { "Memories_Drawing_Key_Mask", "Memories_Drawing_Map_Key", "Memories_Handle",
                   "Memories_Scissors", "Memories_Hall_Drawer", "Memories_Hall_Paper",
                   "Memories_Trash_Items", "Memories_Kitchen_Mystery_Box", "Item_Memories_Key" };
foreach (var w in want)
{
    var hits = Data.Rooms.Where(r => r.GameObjects.Any(g => g.ObjectDefinition?.Name?.Content == w))
                         .Select(r => r.Name?.Content);
    Console.WriteLine($"{w,-32} -> {string.Join(", ", hits)}");
}
Console.WriteLine();
foreach (var rn in new[] { "Lvl_Memories_Drawing_Happy", "Lvl_Memories_Map", "Lvl_Memories_Hall",
                           "Lvl_Memories_Room_B", "Lvl_Memories_Basement" })
{
    var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
    if (r == null) { Console.WriteLine(rn + ": none"); continue; }
    Console.WriteLine("== " + rn);
    foreach (var g in r.GameObjects.OrderBy(g => g.X))
        Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-42} x={g.X,5} y={g.Y,5}");
}
