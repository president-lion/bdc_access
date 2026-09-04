using System; using System.IO; using System.Linq; using UndertaleModLib.Models;
EnsureDataLoaded();
var vb = Data.GameObjects.ByName("View_Back");
bool Desc(UndertaleGameObject o, UndertaleGameObject root){ while(o!=null){ if(o==root) return true; o=o.ParentId; } return false; }
int nrooms=0, npics=0;
var sw = new StringWriter();
foreach (var r in Data.Rooms) {
  var objs = r.GameObjects.Select(g => g.ObjectDefinition).Where(o => o != null).ToList();
  if (!objs.Any(o => Desc(o, vb))) continue;
  nrooms++;
  var pics = objs.Where(o => o.Sprite != null
        && !o.Sprite.Name.Content.StartsWith("S_Test")
        && !o.Sprite.Name.Content.StartsWith("X_System")
        && !o.Sprite.Name.Content.StartsWith("S_Arrow"))
      .Select(o => o.Sprite.Name.Content).Distinct().ToList();
  npics += pics.Count;
  sw.WriteLine(r.Name.Content + " | " + string.Join(", ", pics));
}
File.WriteAllText(@"E:\modgames\bdc\mod\research\closeups.txt", sw.ToString());
Console.WriteLine($"close-up rooms: {nrooms}, distinct picture sprites listed: {npics}");
