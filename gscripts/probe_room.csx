using System; using System.Linq;
foreach (var rn in new[] { "Lvl_Memories_Room", "Lvl_Memories_Room_B", "Lvl_Memories_Room_Drawing", "Lvl_Memories_Treasure", "Lvl_Memories_Basement" }) {
  var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
  if (r == null) { Console.WriteLine(rn + ": none"); continue; }
  Console.WriteLine("== " + rn);
  foreach (var g in r.GameObjects.OrderBy(g => g.X))
    Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-42} x={g.X,5} y={g.Y,5}");
}
