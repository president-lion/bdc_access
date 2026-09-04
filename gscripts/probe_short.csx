using System;
using System.Linq;
using UndertaleModLib.Models;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var c = Data.GameObjects.ByName("Controller");
var create = new Underanalyzer.Decompiler.DecompileContext(
    ctx, c.EventHandlerFor(EventType.Create, Data), Data.ToolInfo.DecompilerSettings).DecompileToString();
var lines = create.Split('\n').Where(l => l.Contains("a11y_short")).ToList();
Console.WriteLine("a11y_short entries: " + lines.Count);
foreach (var l in lines.Take(12)) Console.WriteLine("  " + l.Trim());
Console.WriteLine("--- picks ---");
foreach (var l in create.Split('\n').Where(l => l.Contains("a11y_pick,")).Take(5)) Console.WriteLine("  " + l.Trim());
Console.WriteLine("--- clutter ---");
foreach (var l in create.Split('\n').Where(l => l.Contains("a11y_clutter,")).Take(4)) Console.WriteLine("  " + l.Trim());
