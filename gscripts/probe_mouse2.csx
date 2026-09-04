using System;
EnsureDataLoaded();
string[] fns = { "window_mouse_set", "window_mouse_get_x", "window_mouse_get_y",
                 "window_get_width", "window_get_height", "display_get_width" };
foreach (var f in fns)
    Console.WriteLine($"  fn  {(Data.BuiltinList.Functions.ContainsKey(f) ? "OK " : "!! ")} {f}");
string[] vars = { "view_xview", "view_yview", "view_wview", "view_hview",
                  "view_wport", "view_hport", "mouse_x", "mouse_y" };
foreach (var v in vars)
{
    bool ga = Data.BuiltinList.GlobalArrayVars.ContainsKey(v);
    bool g = Data.BuiltinList.GlobalVars.ContainsKey(v);
    bool inst = Data.BuiltinList.InstanceVars.ContainsKey(v);
    bool lim = Data.BuiltinList.InstanceLimitedVars.ContainsKey(v);
    Console.WriteLine($"  var {((ga||g||inst||lim) ? "OK " : "!! ")} {v,-12} globalArray={ga} global={g} inst={inst} limited={lim}");
}
