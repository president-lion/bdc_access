using System;
using System.Linq;
using System.Reflection;

EnsureDataLoaded();

Console.WriteLine("--- CodeImportGroup methods ---");
foreach (var m in typeof(UndertaleModLib.Compiler.CodeImportGroup)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_")))
    Console.WriteLine("   " + m);
