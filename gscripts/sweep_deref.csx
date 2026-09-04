// Every <local>.<field> read in the patched Step, with the nearest preceding guard for
// that local, so an unguarded dereference is visible at a glance.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UndertaleModLib.Models;

EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var controller = Data.GameObjects.ByName("Controller");
string Dec(UndertaleCode c) =>
    new Underanalyzer.Decompiler.DecompileContext(ctx, c, settings).DecompileToString();

var step = Dec(controller.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data));
var lines = step.Replace("\r", "").Split('\n');

// Locals that hold an instance: anything assigned from instance_find / a list / a map.
var holders = new System.Collections.Generic.HashSet<string>();
foreach (var l in lines)
{
    var m = Regex.Match(l, @"\b(a_[A-Za-z0-9_]+|a11y_[A-Za-z0-9_]+)\s*=\s*(instance_find|ds_list_find_value|ds_map_find_value|instance_nearest|collision_point|_interactive_get_id)\b");
    if (m.Success) holders.Add(m.Groups[1].Value);
    var m2 = Regex.Match(l, @"\b(a_[A-Za-z0-9_]+)\s*=\s*(a_[A-Za-z0-9_]+|a11y_[A-Za-z0-9_]+)\s*;");
    if (m2.Success && holders.Contains(m2.Groups[2].Value)) holders.Add(m2.Groups[1].Value);
}

int flagged = 0;
for (int i = 0; i < lines.Length; i++)
{
    foreach (Match d in Regex.Matches(lines[i], @"\b(a_[A-Za-z0-9_]+|a11y_[A-Za-z0-9_]+)\.[A-Za-z_]"))
    {
        var name = d.Groups[1].Value;
        if (!holders.Contains(name)) continue;
        // Look back for a guard on this exact local, within the block above it.
        bool guarded = false;
        for (int j = i; j >= 0 && j > i - 40; j--)
        {
            if (Regex.IsMatch(lines[j], @"instance_exists\(\s*" + Regex.Escape(name) + @"\s*\)")) { guarded = true; break; }
            if (Regex.IsMatch(lines[j], @"\b" + Regex.Escape(name) + @"\s*=\s*(instance_find|ds_list_find_value|ds_map_find_value)")) break;
        }
        if (!guarded)
        {
            Console.WriteLine($"  [!!] line {i + 1}: {name} dereferenced with no instance_exists above it");
            Console.WriteLine($"       {lines[i].Trim()}");
            flagged++;
        }
    }
}
Console.WriteLine("instance-holding locals: " + holders.Count);
Console.WriteLine(flagged == 0 ? "DEREF SWEEP CLEAN" : $"DEREF SWEEP FOUND {flagged}");
