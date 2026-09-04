// Export the sprite of every Info popup, so their contents can be transcribed.
using System;
using System.IO;
using System.Linq;
using UndertaleModLib.Models;
using UndertaleModLib.Util;

EnsureDataLoaded();

string outDir = @"E:\modgames\bdc\mod\research\info_sprites";
Directory.CreateDirectory(outDir);

var info = Data.GameObjects.ByName("Info");
var worker = new TextureWorker();
int n = 0;

foreach (var o in Data.GameObjects)
{
    if (o.ParentId != info) continue;
    var spr = o.Sprite;
    if (spr == null || spr.Textures.Count == 0) { Console.WriteLine("  (no sprite) " + o.Name.Content); continue; }
    var page = spr.Textures[0]?.Texture;
    if (page == null) { Console.WriteLine("  (no page) " + o.Name.Content); continue; }
    string path = Path.Combine(outDir, o.Name.Content + ".png");
    worker.ExportAsPNG(page, path);
    Console.WriteLine($"  {o.Name.Content}  {spr.Width}x{spr.Height}  frames={spr.Textures.Count}");
    n++;
}

Console.WriteLine($"exported {n} info sprites to {outDir}");
