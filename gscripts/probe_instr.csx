using System;
using System.Linq;
using System.Reflection;
using UndertaleModLib.Models;

EnsureDataLoaded();

var t = typeof(UndertaleInstruction);
Console.WriteLine("--- UndertaleInstruction properties ---");
foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    Console.WriteLine($"   {p.PropertyType.Name,-34} {p.Name}");

// Show a real call instruction and how to reach its function name.
var code = Data.GameObjects.ByName("Controller")
               .EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data);
foreach (var ins in code.Instructions)
{
    if (ins.Kind != UndertaleInstruction.Opcode.Call) continue;
    Console.WriteLine();
    Console.WriteLine("sample call instruction: " + ins.ToString());
    foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        object v = null;
        try { v = p.GetValue(ins); } catch { }
        if (v != null && p.Name.ToLower().Contains("func"))
            Console.WriteLine($"   {p.Name} = {v}  ({v.GetType().Name})");
    }
    break;
}
