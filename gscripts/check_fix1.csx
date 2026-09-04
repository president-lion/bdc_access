// Targeted check that fix 1 (skipping the inert title buttons) really compiled in.
// Main_Menu_Button is object index 1227; the decompiler renders it numerically in a
// plain comparison, so look for the comparison itself rather than the asset name.
using System;
using UndertaleModLib.Models;

EnsureDataLoaded();

var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var controller = Data.GameObjects.ByName("Controller");
var step = new Underanalyzer.Decompiler.DecompileContext(
    ctx, controller.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data), settings)
    .DecompileToString();

var idx = Data.GameObjects.IndexOf(Data.GameObjects.ByName("Main_Menu_Button"));
Console.WriteLine($"Main_Menu_Button object index = {idx}");
Console.WriteLine();

foreach (var line in step.Split('\n'))
{
    var t = line.Trim();
    if (t.Contains("BLOCK_EXIT") || t.Contains($"== {idx}") || t.Contains("continue")
        || t.Contains("Credits") || t.Contains("Lvl_Main_Menu"))
        Console.WriteLine("  | " + (t.Length > 100 ? t.Substring(0, 100) + " ..." : t));
}
