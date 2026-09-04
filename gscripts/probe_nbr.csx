// How does a store to self.nbr differ in bytecode from a store to Other_Object.nbr?
using System;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

string[] look = { "Bridge_Controller_Button", "Hospital_Phone_Btn", "Difference_Object" };

foreach (var name in look)
{
    var o = Data.GameObjects.ByName(name);
    var lst = o.Events[(int)EventType.Create];
    UndertaleCode code = null;
    foreach (var ev in lst) if (ev.Actions.Count > 0) { code = ev.Actions[0].CodeId; break; }
    Console.WriteLine();
    Console.WriteLine("=== " + name + " ===");
    if (code?.Instructions == null) { Console.WriteLine("  no code"); continue; }
    foreach (var ins in code.Instructions)
    {
        if (ins.ValueVariable?.Name?.Content != "nbr") continue;
        Console.WriteLine($"  {ins.Kind,-6} instr.TypeInst={ins.TypeInst,-14} " +
                          $"var.InstanceType={ins.ValueVariable.InstanceType,-14} " +
                          $"refType={ins.ReferenceType}");
    }
}
