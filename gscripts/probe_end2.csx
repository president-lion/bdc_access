using System; using System.Linq;
foreach (var rn in new[] { "Lvl_Ending_Saver" }) {
  var r = Data.Rooms.FirstOrDefault(x => x.Name?.Content == rn);
  if (r == null) { Console.WriteLine(rn + ": none"); continue; }
  Console.WriteLine("== " + rn + " (index " + Data.Rooms.IndexOf(r) + ")");
  foreach (var g in r.GameObjects) Console.WriteLine("   " + g.ObjectDefinition?.Name?.Content);
}
Console.WriteLine("-- rooms after Ending_Saver:");
int i0 = Data.Rooms.IndexOf(Data.Rooms.First(x => x.Name?.Content == "Lvl_Ending_Saver"));
for (int i = i0; i < Math.Min(i0 + 14, Data.Rooms.Count); i++) Console.WriteLine("   " + i + " " + Data.Rooms[i].Name.Content);
