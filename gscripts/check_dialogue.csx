// Confirms the dialogue support compiled into Controller's Step event.
using System;
using UndertaleModLib.Models;

EnsureDataLoaded();

var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var controller = Data.GameObjects.ByName("Controller");

var step = new Underanalyzer.Decompiler.DecompileContext(
    ctx, controller.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data), settings)
    .DecompileToString();
var create = new Underanalyzer.Decompiler.DecompileContext(
    ctx, controller.EventHandlerFor(EventType.Create, Data), settings)
    .DecompileToString();

Console.WriteLine($"Create {create.Length} chars, Step {step.Length} chars");
Console.WriteLine($"VarCount1 (must be 0): {Data.VarCount1}");
Console.WriteLine();

var dlgIdx = Data.GameObjects.IndexOf(Data.GameObjects.ByName("Dialogue"));
Console.WriteLine($"Dialogue object index = {dlgIdx}");
Console.WriteLine();

foreach (var line in step.Split('\n'))
{
    var t = line.Trim();
    if (t.Contains("current_text") || t.Contains("a11y_dlg_last")
        || t.Contains("ev_mouse, 53") || t.Contains("event_perform(ev_mouse")
        || t.Contains($"instance_find({dlgIdx}") || t.Contains("Dialogue"))
        Console.WriteLine("  | " + (t.Length > 96 ? t.Substring(0, 96) + " ..." : t));
}
