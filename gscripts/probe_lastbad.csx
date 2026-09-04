using System;
using System.IO;
using System.Text;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

var sb = new StringBuilder();

foreach (var r in Data.Rooms)
{
    string rn = r.Name?.Content ?? "?";
    if (!rn.Contains("Last_Screen")) continue;
    sb.AppendLine("=== ROOM " + rn + "  w=" + r.Width + " h=" + r.Height);
    foreach (var g in r.GameObjects)
    {
        var od = g.ObjectDefinition;
        if (od == null) { sb.AppendLine("  ?"); continue; }
        var evs = new System.Collections.Generic.List<string>();
        var cur = od;
        while (cur != null)
        {
            for (int t = 0; t < cur.Events.Count; t++)
                foreach (var e in cur.Events[t])
                    evs.Add((cur == od ? "" : cur.Name.Content + ":") + ((EventType)t) + "/" + e.EventSubtype);
            cur = cur.ParentId;
        }
        sb.AppendLine("  " + od.Name.Content
            + "  vis=" + g.ObjectDefinition.Visible + "/inst=" + (g.InstanceID)
            + " x=" + g.X + " y=" + g.Y
            + " spr=" + (od.Sprite?.Name?.Content ?? "-")
            + " parent=" + (od.ParentId?.Name?.Content ?? "-")
            + " ev=[" + string.Join(" ", evs) + "]");
    }
    sb.AppendLine();
}
File.WriteAllText(@"e:\modgames\bdc\mod\research\lastbad.txt", sb.ToString());
Console.WriteLine("done");
