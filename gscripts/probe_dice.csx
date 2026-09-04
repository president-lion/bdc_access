using System;
using System.Linq;
EnsureDataLoaded();
foreach (var r in Data.Rooms.Where(r => r.Name.Content.ToLower().Contains("room_02")
                                     || r.Name.Content.ToLower().Contains("dice")
                                     || r.Name.Content.ToLower().Contains("board")))
{
    Console.WriteLine("=== " + r.Name.Content + " ===");
    foreach (var g in r.GameObjects)
    {
        var o = g.ObjectDefinition;
        if (o == null) continue;
        Console.WriteLine($"    {o.Name.Content,-38} parent={(o.ParentId == null ? "-" : o.ParentId.Name.Content),-20} vis={o.Visible}");
    }
}
