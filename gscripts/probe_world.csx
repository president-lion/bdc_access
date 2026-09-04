using System;
using System.Linq;
using UndertaleModLib.Models;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var c = Data.GameObjects.ByName("Controller");
var step = new Underanalyzer.Decompiler.DecompileContext(
    ctx, c.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data), Data.ToolInfo.DecompilerSettings).DecompileToString();
var lines = step.Split('\n');
int at = Array.FindIndex(lines, l => l.Contains("a11y_w_mode + 1"));
Console.WriteLine("mode-switch at decompiled line " + at);
for (int i = Math.Max(0, at - 30); i <= at + 2; i++)
    Console.WriteLine(i + ": " + lines[i].TrimEnd());
