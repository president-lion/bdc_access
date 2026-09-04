using System;
EnsureDataLoaded();
foreach (var f in new[] { "place_meeting", "position_meeting", "collision_rectangle" })
    Console.WriteLine($"  {(Data.BuiltinList.Functions.ContainsKey(f) ? "OK " : "!! ")} {f}");
