using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

EnsureDataLoaded();

if (Data.IsYYC())
{
    Console.WriteLine("YYC: no code available.");
    return;
}

string codeFolder = @"e:\modgames\bdc\mod\research\gml";
Directory.CreateDirectory(codeFolder);

GlobalDecompileContext ctx = new(Data);
var settings = Data.ToolInfo.DecompilerSettings;

List<UndertaleCode> toDump = Data.Code.Where(c => c.ParentEntry is null).ToList();
Console.WriteLine($"Dumping {toDump.Count} code entries to {codeFolder}");

int done = 0;
Parallel.ForEach(toDump, code =>
{
    if (code is null) return;
    string safe = string.Join("_", code.Name.Content.Split(Path.GetInvalidFileNameChars()));
    string path = Path.Combine(codeFolder, safe + ".gml");
    try
    {
        File.WriteAllText(path,
            new Underanalyzer.Decompiler.DecompileContext(ctx, code, settings).DecompileToString());
    }
    catch (Exception e)
    {
        File.WriteAllText(path, "/* DECOMPILER FAILED\n" + e.Message + "\n*/");
    }
    System.Threading.Interlocked.Increment(ref done);
});
Console.WriteLine($"DONE: {done} entries written");
