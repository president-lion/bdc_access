using System; using System.Linq;
foreach (var rn in new[] { "Lvl_Bridge_Car", "Lvl_Hospital_Teddy", "Lvl_Memories_Portrait", "Lvl_Hospital_Soup" }) {
  var r = Data.Rooms.First(x => x.Name.Content == rn);
  Console.WriteLine("== " + rn + "  tiles=" + r.Tiles.Count + " bgs=" + r.Backgrounds.Count + " objs=" + r.GameObjects.Count);
  foreach (var t in r.Tiles.Take(8))
    Console.WriteLine($"   tile bg={t.BackgroundDefinition?.Name?.Content} x={t.X} y={t.Y} src=({t.SourceX},{t.SourceY}) {t.Width}x{t.Height} depth={t.TileDepth}");
  foreach (var g in r.GameObjects)
    Console.WriteLine($"   obj {g.ObjectDefinition?.Name?.Content,-40} spr={g.ObjectDefinition?.Sprite?.Name?.Content}");
}
