using System; using System.IO; using System.Linq; using UndertaleModLib.Models; using UndertaleModLib.Util;
EnsureDataLoaded();
string root = @"E:\modgames\bdc\mod\research\pic_sprites";
Directory.CreateDirectory(root);
var vb = Data.GameObjects.ByName("View_Back");
bool Desc(UndertaleGameObject o, UndertaleGameObject r){ while(o!=null){ if(o==r) return true; o=o.ParentId; } return false; }
var skipRooms = new[] { "Lvl_Flat_Board", "Lvl_Hospital_Board" };
var w = new TextureWorker(); var log = new StringWriter();
foreach (var r in Data.Rooms) {
  var objs = r.GameObjects.Select(g => g.ObjectDefinition).Where(o => o != null).ToList();
  if (!objs.Any(o => Desc(o, vb))) continue;
  if (skipRooms.Contains(r.Name.Content)) continue;
  string rn = r.Name.Content.StartsWith("Lvl_") ? r.Name.Content.Substring(4) : r.Name.Content;
  string dir = Path.Combine(root, rn); 
  var sprs = objs.Select(o => o.Sprite).Where(s => s != null
       && !s.Name.Content.StartsWith("S_Test") && !s.Name.Content.StartsWith("X_System")
       && !s.Name.Content.StartsWith("S_Arrow") && s.Name.Content != "S_Paper_Sheets_Back")
     .GroupBy(s => s.Name.Content).Select(g => g.First())
     .OrderByDescending(s => s.Width * s.Height).ToList();
  if (sprs.Count == 0) { log.WriteLine(rn + " | (no object sprite; bg=" + (r.Backgrounds.Count>0 && r.Backgrounds[0].BackgroundDefinition!=null ? r.Backgrounds[0].BackgroundDefinition.Name.Content : "none") + ")"); continue; }
  Directory.CreateDirectory(dir);
  log.WriteLine(rn + " |");
  foreach (var s in sprs) {
    for (int f = 0; f < Math.Min(s.Textures.Count, 2); f++) {
      var pg = s.Textures[f]?.Texture; if (pg == null) continue;
      string nm = s.Textures.Count == 1 ? s.Name.Content : s.Name.Content + "_f" + f;
      w.ExportAsPNG(pg, Path.Combine(dir, nm + ".png"));
    }
    log.WriteLine($"    {s.Name.Content,-42} {s.Width}x{s.Height} frames={s.Textures.Count}");
  }
}
File.WriteAllText(@"E:\modgames\bdc\mod\research\closeup_sprites.txt", log.ToString());
Console.WriteLine("done");
