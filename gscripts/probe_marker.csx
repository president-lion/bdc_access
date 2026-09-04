// Is the candidate marker object referenced ANYWHERE in the compiled bytecode?
// Object references compile to a plain integer push, so scan every code entry for the
// object's asset index. This closes the gap the GML text dump cannot: room creation code.
using System;
using System.Linq;
using UndertaleModLib.Models;

EnsureDataLoaded();

string[] candidates = { "Worm_February", "Test_Back" };

foreach (var name in candidates)
{
    var obj = Data.GameObjects.ByName(name);
    if (obj == null) { Console.WriteLine(name + ": NOT FOUND"); continue; }
    int idx = Data.GameObjects.IndexOf(obj);
    int hits = 0;
    string where = "";

    foreach (var code in Data.Code)
    {
        if (code.Instructions == null) continue;
        foreach (var ins in code.Instructions)
        {
            bool match = false;
            if (ins.ValueInt == idx &&
                (ins.Kind == UndertaleInstruction.Opcode.Push ||
                 ins.Kind == UndertaleInstruction.Opcode.PushI))
            {
                match = true;
            }
            if (match)
            {
                hits++;
                if (where.Length < 300) where += code.Name.Content + " ";
                break;
            }
        }
    }
    Console.WriteLine($"{name}: asset index {idx}, events={obj.Events.Sum(e => e.Count)}, " +
                      $"sprite={(obj.Sprite == null ? "none" : obj.Sprite.Name.Content)}, " +
                      $"parent={(obj.ParentId == null ? "none" : obj.ParentId.Name.Content)}");
    Console.WriteLine($"   code entries pushing {idx}: {hits}   {where}");
}

Console.WriteLine();
Console.WriteLine("room creation-code entries: " +
    Data.Code.Count(c => c.Name.Content.StartsWith("gml_RoomCC") || c.Name.Content.StartsWith("gml_Room_")));
