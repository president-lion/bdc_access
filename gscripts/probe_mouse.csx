using System;
EnsureDataLoaded();
string[] want = { "window_mouse_set", "display_mouse_set", "window_view_mouse_set",
                  "window_mouse_get_x", "window_views_mouse_set", "view_xview" };
foreach (var f in want)
    Console.WriteLine($"  {(Data.BuiltinList.Functions.ContainsKey(f) ? "OK " : "!! ")} {f}");
Console.WriteLine("view_xview as a variable: " +
    (Data.BuiltinList.GlobalArrayVars.ContainsKey("view_xview") ||
     Data.BuiltinList.GlobalVars.ContainsKey("view_xview")));
