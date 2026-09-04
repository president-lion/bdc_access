// The two standing safety sweeps, run against the PATCHED file.
//
//  1. no a11y_ variable is READ in Controller's Create before it is assigned there
//  2. no instance_find(...).field is dereferenced without an instance-level guard
//
// Each of these caught a crash that had already shipped, so they are cheap insurance.
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UndertaleModLib.Models;

EnsureDataLoaded();
var ctx = new GlobalDecompileContext(Data);
var settings = Data.ToolInfo.DecompilerSettings;
var controller = Data.GameObjects.ByName("Controller");
string Dec(UndertaleCode c) =>
    new Underanalyzer.Decompiler.DecompileContext(ctx, c, settings).DecompileToString();

var create = Dec(controller.EventHandlerFor(EventType.Create, Data));
var step = Dec(controller.EventHandlerFor(EventType.Step, EventSubtypeStep.Step, Data));

int bad = 0;

// ---- 1. read-before-assign in Create --------------------------------------
var assigned = new System.Collections.Generic.HashSet<string>();
int line = 0;
foreach (var raw in create.Replace("\r", "").Split('\n'))
{
    line++;
    var m = Regex.Match(raw, @"^\s*(a11y_[A-Za-z0-9_]+)\s*=");
    var lhs = m.Success ? m.Groups[1].Value : null;
    foreach (Match r in Regex.Matches(raw, @"\ba11y_[A-Za-z0-9_]+"))
    {
        if (lhs != null && r.Index == m.Groups[1].Index) continue;   // the assignment itself
        if (!assigned.Contains(r.Value))
        {
            Console.WriteLine($"  [!!] Create line {line} reads {r.Value} before assigning it");
            Console.WriteLine($"       {raw.Trim()}");
            bad++;
        }
    }
    if (lhs != null) assigned.Add(lhs);
}
Console.WriteLine($"sweep 1: a11y_ names assigned in Create = {assigned.Count}");

// ---- 2. instance_find(...).field ------------------------------------------
foreach (var (name, body) in new[] { ("Create", create), ("Step", step) })
{
    int ln = 0;
    foreach (var raw in body.Replace("\r", "").Split('\n'))
    {
        ln++;
        if (Regex.IsMatch(raw, @"instance_find\s*\([^()]*\)\s*\."))
        {
            Console.WriteLine($"  [!!] {name} line {ln} dereferences instance_find directly");
            Console.WriteLine($"       {raw.Trim()}");
            bad++;
        }
    }
}

// ---- 3. running an object's own event, then reading it again ---------------
//
// The third crash of this family, and the one neither sweep above catches: the guard is
// there, it passes, and THEN the instance is destroyed by code this patch itself invoked.
// Running an object's user event can delete it - Bridge_01_Bird_Mask's hover spawns its
// birds and calls instance_destroy - so every dereference after a with (x) event_user(...)
// needs a fresh instance_exists(x) between the two.
foreach (var (name, body) in new[] { ("Create", create), ("Step", step) })
{
    var lines2 = body.Replace("\r", "").Split('\n');
    for (int i = 0; i < lines2.Length; i++)
    {
        var m = Regex.Match(lines2[i], @"\bwith\s*\(\s*(a_[A-Za-z0-9_]+)\s*\)");
        if (!m.Success) continue;
        var who = m.Groups[1].Value;

        // Only the calls that can run arbitrary code belonging to the object.
        bool invokes = false;
        for (int k = i; k < lines2.Length && k <= i + 2; k++)
            if (Regex.IsMatch(lines2[k], @"\b(event_user|event_perform)\s*\(")) invokes = true;
        if (!invokes) continue;

        // Stop at the end of the block the with is in. A dereference in a SIBLING branch
        // - the else of the same if - can never run in the same pass as the call, and
        // treating those as findings buried the real ones.
        int ind = lines2[i].Length - lines2[i].TrimStart().Length;

        for (int j = i + 1; j < lines2.Length && j < i + 80; j++)
        {
            if (lines2[j].Trim().Length > 0 &&
                lines2[j].Length - lines2[j].TrimStart().Length < ind) break;
            if (Regex.IsMatch(lines2[j], @"instance_exists\(\s*" + Regex.Escape(who) + @"\s*\)")) break;
            if (Regex.IsMatch(lines2[j], @"\b" + Regex.Escape(who) + @"\s*=")) break;
            if (Regex.IsMatch(lines2[j], @"\b" + Regex.Escape(who) + @"\.[A-Za-z_]"))
            {
                Console.WriteLine($"  [!!] {name}: {who} runs its own event at line {i + 1} " +
                                  $"and is dereferenced at line {j + 1} with no re-check");
                Console.WriteLine($"       {lines2[j].Trim()}");
                bad++;
                break;
            }
        }
    }
}

Console.WriteLine(bad == 0 ? "SWEEPS CLEAN" : $"SWEEPS FOUND {bad} PROBLEM(S)");
