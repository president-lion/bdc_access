using System; using System.IO; using System.Linq; using UndertaleModLib.Models; using UndertaleModLib.Util;
EnsureDataLoaded();
string root = @"E:\modgames\bdc\mod\research\pic_bgs";
Directory.CreateDirectory(root);
var vb = Data.GameObjects.ByName("View_Back");
bool Desc(UndertaleGameObject o, UndertaleGameObject r){ while(o!=null){ if(o==r) return true; o=o.ParentId; } return false; }
var w = new TextureWorker(); var log = new StringWriter(); int n = 0;
foreach (var r in Data.Rooms) {
  var objs = r.GameObjects.Select(g => g.ObjectDefinition).Where(o => o != null).ToList();
  if (!objs.Any(o => Desc(o, vb))) continue;
  string rn = r.Name.Content.StartsWith("Lvl_") ? r.Name.Content.Substring(4) : r.Name.Content;
  foreach (var bd in r.Tiles.Select(t => t.BackgroundDefinition).Where(b => b != null).GroupBy(b => b.Name.Content).Select(g => g.First())) {
    var pg = bd.Texture; if (pg == null) continue;
    w.ExportAsPNG(pg, Path.Combine(root, rn + "__" + bd.Name.Content + ".png"));
    log.WriteLine($"{rn,-30} {bd.Name.Content}"); n++;
  }
}
File.WriteAllText(@"E:\modgames\bdc\mod\research\closeup_bgs.txt", log.ToString());
Console.WriteLine("exported " + n);
