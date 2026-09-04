using System;
using UndertaleModLib.Models;
EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var code = Data.GameObjects.ByName("Info").EventHandlerFor(EventType.Draw, EventSubtypeDraw.Draw, Data);
Console.WriteLine(new Underanalyzer.Decompiler.DecompileContext(ctx, code, settings).DecompileToString());
