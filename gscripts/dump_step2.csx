using System; using System.IO;
using UndertaleModLib.Models;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var c = Data.GameObjects.ByName("Controller");
string Dec(UndertaleCode k) => new Underanalyzer.Decompiler.DecompileContext(ctx, k, settings).DecompileToString();
File.WriteAllText(@"e:\modgames\bdc\mod\research\patched_step.gml",
    Dec(c.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data)));
Console.WriteLine("dumped");
