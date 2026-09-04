// Which of the functions I want are in UTMT's compiler builtin list?
// That list - not the game's own usage - is what decides whether a call compiles, and
// (variable_local_exists aside) membership has matched what the 1.4 runner supports.
using System;
using System.Linq;

EnsureDataLoaded();

string[] want = {
    "string_replace_all", "string_replace", "string_pos", "string_count",
    "string_char_at", "string_copy", "string_delete", "string_length", "string_upper",
    "object_get_name", "instance_number", "instance_find", "instance_exists",
    "ds_map_exists", "ds_map_create", "ds_list_create", "keyboard_check_pressed",
    "event_user", "event_perform", "external_call",
    // known-bad control: compiled but killed the runner
    "variable_local_exists",
};

foreach (var f in want)
{
    bool inCompiler = Data.BuiltinList.Functions.ContainsKey(f);
    bool inFunc = Data.Functions.ByName(f) != null;
    Console.WriteLine($"  {(inCompiler ? "OK " : "!! ")} {f,-24} compiler={inCompiler,-5} FUNC={inFunc}");
}
