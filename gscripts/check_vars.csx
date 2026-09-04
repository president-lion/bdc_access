using System;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

Console.WriteLine($"bytecode version : {Data.GeneralInfo.BytecodeVersion}");
Console.WriteLine($"VARI count       : {Data.Variables.Count}");
Console.WriteLine($"VarCount1/2      : {Data.VarCount1} / {Data.VarCount2}");
Console.WriteLine($"MaxLocalVarCount : {Data.GeneralInfo.Build}");
Console.WriteLine();

// Compare a variable the GAME already had against one we introduced.
string[] probe = { "a11y_ready", "a11y_scan", "a11y_lbl", "STEAM", "language", "cursor_image" };
Console.WriteLine($"{"name",-18} {"instType",-14} {"varID",-8} {"occurrences",-12}");
foreach (var name in probe)
{
    var v = Data.Variables.FirstOrDefault(x => x.Name?.Content == name);
    if (v == null) { Console.WriteLine($"{name,-18} <not in VARI>"); continue; }
    Console.WriteLine($"{name,-18} {v.InstanceType,-14} {v.VarID,-8} {v.Occurrences,-12}");
}

Console.WriteLine();
Console.WriteLine("--- how the game's own globals are declared (first 8 with InstanceType Global) ---");
foreach (var v in Data.Variables.Where(x => x.InstanceType == UndertaleInstruction.InstanceType.Global).Take(8))
    Console.WriteLine($"    {v.Name?.Content,-28} varID={v.VarID,-6} occ={v.Occurrences}");
