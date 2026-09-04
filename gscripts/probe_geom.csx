using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms)
{
    if (r.Name.Content != "Lvl_Flat_Board") continue;
    var pts = r.GameObjects.Where(o => { var n = o.ObjectDefinition?.Name?.Content;
        return n == "Dead_Pointer" || n == "Checkpoint_Pointer" || n == "Position_Pointer"; }).ToList();
    var haz = r.GameObjects.Where(o => { var n = o.ObjectDefinition?.Name?.Content;
        return n != null && n.StartsWith("Board_") && n != "Board_Controller" && n != "Board_Button" && n != "Board_Dice"; }).ToList();
    Console.WriteLine("hazards: " + haz.Count + ", pointers: " + pts.Count);
    foreach (var h in haz)
    {
        var best = pts.OrderBy(p => (p.X - h.X) * (p.X - h.X) + (p.Y - h.Y) * (p.Y - h.Y)).First();
        double d = Math.Sqrt((best.X - h.X) * (best.X - h.X) + (best.Y - h.Y) * (best.Y - h.Y));
        Console.WriteLine($"  {h.ObjectDefinition.Name.Content,-28} at ({h.X},{h.Y})  nearest {best.ObjectDefinition.Name.Content} dist {d:F0}");
    }
}
