using System;
using UndertaleModLib.Models;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var code = Data.GameObjects.ByName("Controller").EventHandlerFor(EventType.KeyPress, (uint)27, Data);
Console.WriteLine(new Underanalyzer.Decompiler.DecompileContext(ctx, code, settings).DecompileToString());
