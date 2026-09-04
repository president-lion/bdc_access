using System;
EnsureDataLoaded();
string[] want = {
    "ds_list_find_index", "ds_list_copy", "ds_list_delete", "ds_list_clear",
    "ds_map_add", "ds_map_destroy", "ds_map_clear", "ds_map_find_value",
    "ds_map_delete", "ds_map_size", "instance_number", "object_get_name",
};
foreach (var f in want)
    Console.WriteLine($"  {(Data.BuiltinList.Functions.ContainsKey(f) ? "OK " : "!! ")} {f}");
