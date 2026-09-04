// Probe: how does UTMT's compiler decide a function name is legal?
using System;
using System.Linq;
using System.Reflection;

EnsureDataLoaded();

var bl = Data.BuiltinList;
Console.WriteLine("BuiltinList type: " + (bl == null ? "<null>" : bl.GetType().FullName));

if (bl != null)
{
    foreach (var p in bl.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine("  prop  " + p.PropertyType.Name + " " + p.Name);
    foreach (var f in bl.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        Console.WriteLine("  field " + f.FieldType.Name + " " + f.Name);

    var fn = bl.GetType().GetProperty("Functions") ?? (MemberInfo)null;
    var fnf = bl.GetType().GetField("Functions");
    object funcs = fn != null ? ((PropertyInfo)fn).GetValue(bl) : (fnf != null ? fnf.GetValue(bl) : null);
    Console.WriteLine("Functions container: " + (funcs == null ? "<null>" : funcs.GetType().FullName));

    if (funcs is System.Collections.IDictionary d)
    {
        Console.WriteLine("count = " + d.Count);
        int n = 0;
        foreach (System.Collections.DictionaryEntry e in d)
        {
            if (n++ < 3)
                Console.WriteLine("  sample: " + e.Key + " -> " + e.Value.GetType().FullName);
            var k = e.Key as string;
            if (k != null && k.StartsWith("variable_"))
                Console.WriteLine("  HAS: " + k);
        }
        var vt = null as Type;
        foreach (System.Collections.DictionaryEntry e in d) { vt = e.Value.GetType(); break; }
        if (vt != null)
        {
            Console.WriteLine("value type ctors:");
            foreach (var c in vt.GetConstructors())
                Console.WriteLine("   " + c);
        }
    }
}
