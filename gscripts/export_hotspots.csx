using System; using System.IO; using System.Linq; using System.Text;
using UndertaleModLib.Models; using UndertaleModLib.Util;
EnsureDataLoaded();
string root = @"C:\Users\User\AppData\Local\Temp\claude\e--modgames-bdc\a40f5b2b-ab9a-433e-afc5-b3e21f6ad02c\scratchpad\masks";
Directory.CreateDirectory(root);
Directory.CreateDirectory(Path.Combine(root, "bg"));
Directory.CreateDirectory(Path.Combine(root, "spr"));

var io = Data.GameObjects.ByName("Interactive_Object");
bool D(UndertaleGameObject o){ for(var p=o;p!=null;p=p.ParentId) if(p==io) return true; return false; }
UndertaleCode Hover(UndertaleGameObject o){ for(var p=o;p!=null;p=p.ParentId){ var l=p.Events[(int)EventType.Other]; if(l==null) continue; foreach(var e in l) if(e.EventSubtype==11 && e.Actions.Count>0) return e.Actions[0].CodeId; } return null; }
var exitFns = new HashSet<string>{"_cursor_go","_cursor_enter","_cursor_back"};
var actFns = new HashSet<string>{"_cursor_hand","_cursor_hit","_cursor_high","_cursor_look","_cursor_locked","_check_item","_activate_item_cursor","_cursor_no_item","_cursor_different"};
var clutterRx = new System.Text.RegularExpressions.Regex(
  "grave_debris|bird_dead|dead_bird|candle_night|blood|stain|_mess|_gore|_piss|_dirt|spider_web|cobweb|_rubble|_debris|walk_break|wall_break|road_break|wall_broken|road_part|walk_part|wall_part|grave_part|sidewalk_stone|wall_thing|wall_element",
  System.Text.RegularExpressions.RegexOptions.IgnoreCase);

bool Wanted(UndertaleGameObject o)
{
    if (o == io || !D(o) || o.Visible) return false;
    var n = o.Name?.Content; if (string.IsNullOrEmpty(n)) return false;
    if (clutterRx.IsMatch(n)) return false;
    var h = Hover(o);
    if (h?.Instructions != null)
        foreach (var i in h.Instructions) { var f=i.ValueFunction?.Name?.Content; if(f==null) continue;
            if (exitFns.Contains(f) || actFns.Contains(f)) return false; }
    return true;
}

var w = new TextureWorker();
var doneBg = new HashSet<string>();
var sprDone = new HashSet<string>();
var seen = new HashSet<string>();
var log = new StringBuilder();
int n = 0;
foreach (var r in Data.Rooms)
{
    if (r.Tiles.Count == 0) continue;
    foreach (var g in r.GameObjects)
    {
        var o = g.ObjectDefinition;
        if (o == null || !Wanted(o)) continue;
        var name = o.Name.Content;
        if (!seen.Add(name)) continue;
        var sp = o.Sprite; if (sp == null) continue;

        double ox = g.X - (sp.OriginX * g.ScaleX);
        double oy = g.Y - (sp.OriginY * g.ScaleY);
        double ow = sp.Width  * g.ScaleX;
        double oh = sp.Height * g.ScaleY;
        double cx = ox + ow / 2, cy = oy + oh / 2;

        UndertaleRoom.Tile best = null;
        foreach (var t in r.Tiles)
        {
            if (t.BackgroundDefinition?.Texture == null) continue;
            if (cx >= t.X && cx < t.X + t.Width && cy >= t.Y && cy < t.Y + t.Height) { best = t; break; }
            if (best == null) best = t;
        }
        if (best == null) continue;

        // The object's OWN sprite as well. Some of these are hit boxes laid over art that
        // belongs to the room, and some are objects that are simply invisible until the
        // story reveals them - and for the second kind the room underneath is empty, so the
        // crop would describe a bare patch of floor. Exporting both and looking at them
        // side by side is the only way to tell which is which.
        if (sp.Textures.Count > 0 && sp.Textures[0].Texture != null && sprDone.Add(name))
            w.ExportAsPNG(sp.Textures[0].Texture, Path.Combine(root, "spr", name + ".png"));

        var bgName = best.BackgroundDefinition.Name.Content;
        if (doneBg.Add(bgName))
            w.ExportAsPNG(best.BackgroundDefinition.Texture, Path.Combine(root, "bg", bgName + ".png"));

        // Room space to background-image space. A tile can be drawn scaled - Bridge_01's
        // art is 1156x600 stretched over a 1280x720 room - so divide by the tile scale
        // before adding its source offset, or every box lands in the wrong place.
        double tsx = best.ScaleX == 0 ? 1 : best.ScaleX;
        double tsy = best.ScaleY == 0 ? 1 : best.ScaleY;
        // And then out of LOGICAL background space into the exported PNG. ExportAsPNG
        // writes the texture page item CROPPED to its non-empty area, so the file is the
        // TargetWidth x TargetHeight rectangle that sits at TargetX,TargetY inside the
        // background's nominal size. B_Bridge_01 is a 1280x720 background whose art is
        // 1156x600 at 56,101 - so without this every box landed about 80 pixels low and
        // 56 to the left, which is exactly what the first sheets showed.
        var tex = best.BackgroundDefinition.Texture;
        double sx = ((ox - best.X) / tsx) + best.SourceX - tex.TargetX;
        double sy = ((oy - best.Y) / tsy) + best.SourceY - tex.TargetY;
        double sw = ow / tsx, sh = oh / tsy;
        log.AppendLine(string.Join("\t", name, r.Name.Content, bgName,
            ((int)Math.Round(sx)).ToString(), ((int)Math.Round(sy)).ToString(),
            ((int)Math.Round(sw)).ToString(), ((int)Math.Round(sh)).ToString()));
        n++;
    }
}
File.WriteAllText(Path.Combine(root, "_crops.txt"), log.ToString());
Console.WriteLine("hotspots: " + n + ", backgrounds exported: " + doneBg.Count);
