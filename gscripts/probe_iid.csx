using System; using System.Linq; using System.Text; using System.IO;
EnsureDataLoaded();
var sb = new StringBuilder();
foreach (var r in Data.Rooms)
    foreach (var g in r.GameObjects)
        if (g.InstanceID >= 100060 && g.InstanceID <= 100080)
            sb.AppendLine(g.InstanceID + "\t" + r.Name.Content + "\t" +
                          (g.ObjectDefinition?.Name?.Content ?? "?") + "\tx=" + g.X + " y=" + g.Y);
Console.WriteLine(sb.ToString());
