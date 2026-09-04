using System;
using System.IO;
using System.Text;
using System.Linq;

EnsureDataLoaded();

var sb = new StringBuilder();
sb.AppendLine("idx\tname\tparent\tsprite\tvisible\tpersistent\tdepth");
for (int i = 0; i < Data.GameObjects.Count; i++)
{
    var o = Data.GameObjects[i];
    sb.AppendLine($"{i}\t{o.Name?.Content}\t{o.ParentId?.Name?.Content ?? "-"}\t{o.Sprite?.Name?.Content ?? "-"}\t{o.Visible}\t{o.Persistent}\t{o.Depth}");
}
File.WriteAllText(@"e:\modgames\bdc\mod\research\objects.txt", sb.ToString());
Console.WriteLine($"objects: {Data.GameObjects.Count}");

// Rooms
var rb = new StringBuilder();
rb.AppendLine("idx\tname\tcaption\twidth\theight");
for (int i = 0; i < Data.Rooms.Count; i++)
{
    var r = Data.Rooms[i];
    rb.AppendLine($"{i}\t{r.Name?.Content}\t{r.Caption?.Content}\t{r.Width}\t{r.Height}");
}
File.WriteAllText(@"e:\modgames\bdc\mod\research\rooms.txt", rb.ToString());
Console.WriteLine($"rooms: {Data.Rooms.Count}");

// Sprites
var sp = new StringBuilder();
sp.AppendLine("idx\tname\tframes\twidth\theight");
for (int i = 0; i < Data.Sprites.Count; i++)
{
    var s = Data.Sprites[i];
    sp.AppendLine($"{i}\t{s.Name?.Content}\t{s.Textures?.Count}\t{s.Width}\t{s.Height}");
}
File.WriteAllText(@"e:\modgames\bdc\mod\research\sprites.txt", sp.ToString());
Console.WriteLine($"sprites: {Data.Sprites.Count}");
