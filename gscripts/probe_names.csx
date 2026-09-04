using System;
using System.Linq;
EnsureDataLoaded();
var names = Data.GameObjects.Select(o => o.Name.Content).ToList();
Console.WriteLine("ends with _Mask: " + names.Count(n => n.EndsWith("_Mask")));
Console.WriteLine("contains Mask, not ending: ");
foreach (var n in names.Where(n => n.Contains("Mask") && !n.EndsWith("_Mask"))) Console.WriteLine("  " + n);
Console.WriteLine("--- suffix histogram (last _token) ---");
foreach (var g in names.Where(n=>n.Contains("_")).GroupBy(n => n.Substring(n.LastIndexOf('_')+1)).OrderByDescending(g=>g.Count()).Take(30))
    Console.WriteLine($"  {g.Count(),4}  {g.Key}");
