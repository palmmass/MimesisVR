using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
var dll = args[0]; var refDirs = args.Skip(2).ToArray();
AssemblyLoadContext.Default.Resolving += (ctx, name) => {
    foreach (var d in refDirs) { var p = Path.Combine(d, name.Name + ".dll"); if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p); }
    return null; };
var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
var t = asm.GetType("UnityVRMod.Mimesis.MimesisSettings");
if (Environment.GetEnvironmentVariable("LOCCHECK") == "1") {
    var loc = asm.GetType("UnityVRMod.Mimesis.MimesisLoc");
    loc.GetMethod("Init").Invoke(null, null);
    var langs = (System.Collections.IList)loc.GetField("Langs").GetValue(null);
    Console.WriteLine("langs " + langs.Count);
    loc.GetField("Cur").SetValue(null, 2);
    var tr = loc.GetMethod("Tr", new[] { typeof(string), typeof(string) });
    foreach (var k in new[] { "Controls", "Off", "As the game", "Settings", "Debug tools" }) Console.WriteLine(k + " -> " + tr.Invoke(null, new object[] { k, "ru" }));
    var trf = loc.GetMethod("TrF"); Console.WriteLine(trf.Invoke(null, new object[] { "Done ({0} of {1})", "x", new object[] { 3, 4 } }));
    t.GetMethod("Define", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
    var all2 = (System.Collections.IList)t.GetField("All").GetValue(null); int miss = 0, tot = 0;
    foreach (var d in all2) { var ty = d.GetType(); foreach (var f in new[] { "Label", "Desc" }) { var a = (string[])ty.GetField(f).GetValue(d); if (a[0] == null || a[0].Length == 0) continue; tot++; var r = (string)tr.Invoke(null, new object[] { a[0], a[1] }); if (r == a[0]) { miss++; Console.WriteLine("MISS " + a[0]); } } }
    Console.WriteLine($"labels/descs {tot}, untranslated {miss}");
    return;
}
try { t.GetMethod("Define", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
catch (Exception ex) { Console.Error.WriteLine("Define failed: " + (ex.InnerException ?? ex)); }
var all = (System.Collections.IList)t.GetField("All").GetValue(null);
var tabs = (System.Collections.IList)t.GetField("Tabs").GetValue(null);
var pairs = new List<string[]>();
void Add(string[] a) { if (a != null && a.Length >= 2 && a[0] != null) pairs.Add(new[] { a[0], a[1] }); }
foreach (var tb in tabs) Add((string[])tb.GetType().GetField("Label").GetValue(tb));
foreach (var d in all) {
    var ty = d.GetType();
    Add((string[])ty.GetField("Label").GetValue(d));
    Add((string[])ty.GetField("Desc").GetValue(d));
    var opts = (string[][])ty.GetField("Options").GetValue(d);
    if (opts != null) for (int i = 0; i < opts[0].Length; i++) Add(new[] { opts[0][i], opts[1][i] });
}
File.WriteAllText(args[1], JsonSerializer.Serialize(new { count = all.Count, tabs = tabs.Count, pairs }, new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"defs {all.Count}, tabs {tabs.Count}, pairs {pairs.Count}");
