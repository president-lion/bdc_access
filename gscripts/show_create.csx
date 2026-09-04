using System;
using UndertaleModLib.Models;

EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var controller = Data.GameObjects.ByName("Controller");
var code = controller.EventHandlerFor(EventType.Create, Data);
var src = new Underanalyzer.Decompiler.DecompileContext(ctx, code, settings).DecompileToString();
var lines = src.Split('\n');

Console.WriteLine("total lines: " + lines.Length);
Console.WriteLine("=== ANY line containing '[' ===");
for (int i = 0; i < lines.Length; i++)
    if (lines[i].Contains("["))
        Console.WriteLine($"{i,4}: {lines[i].TrimEnd()}");

Console.WriteLine("=== injected block (from first a11y line) ===");
int start = Array.FindIndex(lines, l => l.Contains("a11y"));
if (start < 0) { Console.WriteLine("no a11y found!"); return; }
for (int i = Math.Max(0, start - 3); i < Math.Min(lines.Length, start + 22); i++)
    Console.WriteLine($"{i,4}: {lines[i].TrimEnd()}");
