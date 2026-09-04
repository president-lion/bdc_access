using System; using System.Linq;
foreach (var rn in new[] { "Lvl_Ending_Saver", "Lvl_Ending_Chapter", "Lvl_Ending_Start", "Lvl_Ending_Hospital_Hall_03" }) {
  var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
  if (r == null) { Console.WriteLine(rn + ": none"); continue; }
  Console.WriteLine("== " + rn);
  foreach (var g in r.GameObjects.OrderBy(g => g.X))
    Console.WriteLine($"   {g.ObjectDefinition?.Name?.Content,-42} x={g.X,5} y={g.Y,5}");
}
