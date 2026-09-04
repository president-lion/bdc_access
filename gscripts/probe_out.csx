using System; using System.Linq;
var r = Data.Rooms.First(x => x.Name?.Content == "Lvl_Memories_Outside");
Console.WriteLine("== Lvl_Memories_Outside  (" + r.GameObjects.Count + ")");
foreach (var g in r.GameObjects.OrderBy(g => g.X))
  Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-42} x={g.X,5} y={g.Y,5}");
