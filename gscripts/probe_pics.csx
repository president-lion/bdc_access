using System; using System.Linq;
// Rooms that are a single picture you look at: they carry the paper backdrop.
foreach (var r in Data.Rooms) {
  var names = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content).ToList();
  if (!names.Any(n => n != null && (n == "Paper_Sheets_Back" || n.EndsWith("_Back") && names.Count < 12))) continue;
  if (r.GameObjects.Count > 14) continue;
  Console.WriteLine("== " + r.Name.Content + " (" + r.GameObjects.Count + ")");
  foreach (var g in r.GameObjects) {
    var o = g.ObjectDefinition;
    Console.WriteLine($"   {o?.Name?.Content,-40} spr={o?.Sprite?.Name?.Content}");
  }
}
