// Is the recovery check compiling to what I think it is?
using System;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

Console.WriteLine("--- is ds_type_map a CONSTANT or did it become a VARIABLE? ---");
var v = Data.Variables.FirstOrDefault(x => x.Name?.Content == "ds_type_map");
Console.WriteLine("VARI entry for ds_type_map: " + (v == null ? "none (good - it is a constant)" : "PRESENT (bad)"));
Console.WriteLine("BuiltinList knows ds_type_map: " + Data.BuiltinList.Constants.ContainsKey("ds_type_map"));

Console.WriteLine();
Console.WriteLine("--- FUNC entry we added ---");
var f = Data.Functions.FirstOrDefault(x => x.Name?.Content == "variable_local_exists");
if (f == null) Console.WriteLine("variable_local_exists: ABSENT");
else Console.WriteLine($"variable_local_exists: present, occurrences={f.Occurrences}");

Console.WriteLine();
Console.WriteLine("--- first 40 instructions of Controller Step ---");
var code = Data.GameObjects.ByName("Controller")
               .EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data);
int n = 0;
foreach (var inst in code.Instructions)
{
    Console.WriteLine("  " + inst.ToString());
    if (++n >= 40) break;
}
