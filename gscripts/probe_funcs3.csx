using System;
EnsureDataLoaded();
string[] want = { "draw_clear_alpha", "draw_clear", "surface_create", "surface_exists",
                  "surface_set_target", "surface_reset_target", "event_perform" };
foreach (var f in want)
    Console.WriteLine($"  {(Data.BuiltinList.Functions.ContainsKey(f) ? "OK " : "!! ")} {f}");
