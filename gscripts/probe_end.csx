using System; using System.Linq;
foreach (var rn in new[] { "Lvl_Memories_End_Chapter", "Lvl_Memories_Flush", "Lvl_Forest_Start", "Lvl_Forest_Chapter" }) {
  var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
  if (r == null) { Console.WriteLine(rn + ": none"); continue; }
  Console.WriteLine("== " + rn + " (index " + Data.Rooms.IndexOf(r) + ")");
  foreach (var g in r.GameObjects.OrderBy(g => g.X))
    Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-42} x={g.X,5} y={g.Y,5}");
}
Console.WriteLine("room 90 = " + Data.Rooms[90].Name.Content);
