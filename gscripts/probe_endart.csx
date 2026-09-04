using System; using System.IO; using System.Linq; using System.Text;
using UndertaleModLib.Models; using UndertaleModLib.Util;
EnsureDataLoaded();
string root = @"C:\Users\User\AppData\Local\Temp\claude\e--modgames-bdc\a40f5b2b-ab9a-433e-afc5-b3e21f6ad02c\scratchpad\endart";
Directory.CreateDirectory(root);
var w = new TextureWorker();
var log = new StringBuilder();
foreach (var r in Data.Rooms)
{
    var rn = r.Name?.Content ?? "";
    if (!rn.Contains("Last_Screen")) continue;
    log.AppendLine("ROOM " + rn + " tiles=" + r.Tiles.Count + " bgs=" + r.Backgrounds.Count);
    foreach (var b in r.Backgrounds)
    {
        var bd = b.BackgroundDefinition;
        if (bd == null) continue;
        log.AppendLine("  BG " + bd.Name.Content + " enabled=" + b.Enabled + " x=" + b.X + " y=" + b.Y);
        if (bd.Texture != null) w.ExportAsPNG(bd.Texture, Path.Combine(root, rn + "__BG__" + bd.Name.Content + ".png"));
    }
    int k = 0;
    foreach (var t in r.Tiles)
    {
        var bd = t.BackgroundDefinition;
        if (bd == null) continue;
        log.AppendLine("  TILE " + bd.Name.Content + " x=" + t.X + " y=" + t.Y + " w=" + t.Width + " h=" + t.Height + " sx=" + t.ScaleX + " sy=" + t.ScaleY + " depth=" + t.TileDepth);
        if (bd.Texture != null) w.ExportAsPNG(bd.Texture, Path.Combine(root, rn + "__TILE" + (k++) + "__" + bd.Name.Content + ".png"));
    }
    foreach (var g in r.GameObjects)
    {
        var sp = g.ObjectDefinition?.Sprite;
        if (sp == null || sp.Textures.Count == 0 || sp.Textures[0].Texture == null) continue;
        w.ExportAsPNG(sp.Textures[0].Texture, Path.Combine(root, rn + "__OBJ__" + g.ObjectDefinition.Name.Content + ".png"));
    }
}
File.WriteAllText(Path.Combine(root, "_log.txt"), log.ToString());
Console.WriteLine(log.ToString());
