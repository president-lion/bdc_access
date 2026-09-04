using System; using System.IO; using System.Linq; using UndertaleModLib.Util;
EnsureDataLoaded();
string outDir = @"E:\modgames\bdc\mod\research\pic_sprites";
Directory.CreateDirectory(outDir);
var want = new[] {
 "S_Memories_Photo","S_Memories_Map","S_Memories_Map_Blood","S_Memories_Map_Cut",
 "S_Memories_Key","S_Memories_Key_Cut","S_Memories_Happy_Day","S_Memories_Happy_Cut",
 "S_Memories_Toy_Guards","S_Memories_Toy_Cut","S_Memories_Toy_Cut_02","S_Memories_Toy_Cut_03",
 "S_Memories_Portrait","S_Memories_Portrait_02","S_Memories_Spike_Dead","S_Memories_Mothers_Day",
 "S_Memories_Dad","S_Memories_Letter_Death","S_Memories_Toy_Army","S_Memories_Drawing_Room",
 "S_Memories_Grave","S_Memories_Letter","S_Memories_Pass","S_Memories_Last_Warning",
 "S_Memories_Room_Parrents_Ereaser","S_Memories_Dog_House_Map","S_Memories_Trash_Items" };
var w = new TextureWorker(); int n = 0;
foreach (var s in want) {
  var spr = Data.Sprites.ByName(s);
  if (spr == null) { Console.WriteLine("MISSING " + s); continue; }
  for (int f = 0; f < spr.Textures.Count; f++) {
    var pg = spr.Textures[f]?.Texture; if (pg == null) continue;
    string nm = spr.Textures.Count == 1 ? s : s + "_f" + f;
    w.ExportAsPNG(pg, Path.Combine(outDir, nm + ".png")); n++;
  }
  Console.WriteLine($"  {s}  {spr.Width}x{spr.Height}  frames={spr.Textures.Count}");
}
Console.WriteLine("exported " + n);
