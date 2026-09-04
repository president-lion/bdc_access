using System;
using System.IO;
using System.Text;
using System.Linq;

EnsureDataLoaded();

var sb = new StringBuilder();
var interesting = new[] { "Controller", "Main_Menu_New_Game", "SLOTS_NEW", "Slot_Menu", "Game_Menu" };

for (int i = 0; i < Data.Rooms.Count; i++)
{
    var r = Data.Rooms[i];
    var names = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content ?? "?").ToList();
    bool hot = names.Any(n => interesting.Contains(n));
    if (i < 12 || hot)
    {
        sb.AppendLine($"[{i}] {r.Name?.Content}  instances={names.Count}");
        // Only list the ones we care about, plus the first few, to keep this readable.
        var show = names.Where(n => interesting.Contains(n)).Distinct().ToList();
        if (show.Count > 0) sb.AppendLine("     HOT: " + string.Join(", ", show));
        if (i < 12) sb.AppendLine("     all: " + string.Join(", ", names.Distinct().Take(14)));
    }
}
File.WriteAllText(@"e:\modgames\bdc\mod\research\room_instances.txt", sb.ToString());
Console.WriteLine("rooms scanned: " + Data.Rooms.Count);

// Which room does the game start in?
Console.WriteLine("first room: " + Data.Rooms[0].Name?.Content);
