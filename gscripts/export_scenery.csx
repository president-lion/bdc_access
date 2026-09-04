using System; using System.IO; using System.Linq; using UndertaleModLib.Models; using UndertaleModLib.Util;
EnsureDataLoaded();
string root = @"C:\Users\User\AppData\Local\Temp\claude\e--modgames-bdc\a40f5b2b-ab9a-433e-afc5-b3e21f6ad02c\scratchpad\scn";
Directory.CreateDirectory(root);
var io = Data.GameObjects.ByName("Interactive_Object");
bool D(UndertaleGameObject o){ for(var p=o;p!=null;p=p.ParentId) if(p==io) return true; return false; }
var rooms = new Dictionary<UndertaleGameObject,string>();
foreach (var r in Data.Rooms) foreach (var g in r.GameObjects) { var o=g.ObjectDefinition; if(o!=null && !rooms.ContainsKey(o)) rooms[o]=r.Name.Content; }
var w = new TextureWorker(); var log = new StringWriter(); int n=0;
foreach (var o in Data.GameObjects)
{
    if (o == io || !D(o) || !rooms.ContainsKey(o)) continue;
    var sp = o.Sprite; var sn = sp?.Name?.Content ?? "";
    if (sn == "" || sn.StartsWith("S_Test")) continue;
    if (!o.Visible) continue;
    if (sp.Textures.Count == 0 || sp.Textures[0].Texture == null) continue;
    var file = o.Name.Content + ".png";
    w.ExportAsPNG(sp.Textures[0].Texture, Path.Combine(root, file));
    log.WriteLine(o.Name.Content + "\t" + rooms[o] + "\t" + sn);
    n++;
}
File.WriteAllText(Path.Combine(root, "_index.txt"), log.ToString());
Console.WriteLine("exported " + n);
