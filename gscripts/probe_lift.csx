using System;
using System.Linq;
EnsureDataLoaded();
string[] want = { "Hosptital_Hall_Light", "Hospital_Hall_Light_02", "Hospital_Hall_Light",
                  "Hospital_Elevator_Button", "Hospital_Elevator_Btn", "Hospital_Elevator_Door",
                  "Hospital_Hall_03_Elevator_Info", "Hospital_Controller" };
foreach (var r in Data.Rooms)
{
    var hits = r.GameObjects.Select(g => g.ObjectDefinition?.Name?.Content)
                .Where(n => n != null && want.Contains(n)).Distinct().ToList();
    if (hits.Count > 0)
        Console.WriteLine($"{r.Name.Content}: {string.Join(", ", hits)}");
}
