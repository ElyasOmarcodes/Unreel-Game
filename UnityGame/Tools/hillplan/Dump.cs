// Runs the Hill Village plan (Assets/Editor/HillVillagePlan.cs) outside Unity and writes it as JSON for render.py.
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
public static partial class HillVillageBuilder
{
    public static string Dump(int seed)
    {
        rng = new System.Random(seed);
        Plan();
        var ci = CultureInfo.InvariantCulture;
        string F(float f) => f.ToString("0.###", ci);
        var sb = new StringBuilder("{\"houses\":[");
        sb.Append(string.Join(",", houses.Select(h => $"[{F(h.X)},{F(h.Z)},{F(h.Yaw)},{F(h.W)},{F(h.D)},{h.Floors},\"{h.Kind}\",{h.Roof},{h.Colour},{(h.Yard ? 1 : 0)},{F(h.YardDepth)},{(h.Gallery ? 1 : 0)},{(h.Ladder ? 1 : 0)}]")));
        sb.Append("],\"plots\":[");
        sb.Append(string.Join(",", plots.Select(q => $"[{F(q.Area.X)},{F(q.Area.Z)},{F(q.Area.Yaw)},{F(q.Area.W)},{F(q.Area.D)},{(q.Big ? 1 : 0)}]")));
        sb.Append("],\"lanes\":[");
        sb.Append(string.Join(",", lanes.Select(l => "{\"w\":" + F(l.Width) + ",\"p\":[" + string.Join(",", l.P.Select(p => $"[{F(p.x)},{F(p.y)}]")) + "]}")));
        sb.Append("],\"tracks\":[");
        sb.Append(string.Join(",", tracks.Select(l => "{\"w\":" + F(l.width) + ",\"p\":[" + string.Join(",", l.line.Select(p => $"[{F(p.x)},{F(p.y)}]")) + "]}")));
        sb.Append("],\"gardens\":[");
        sb.Append(string.Join(",", gardens.Select(g => $"[{F(g.A.x)},{F(g.A.y)},{F(g.B.x)},{F(g.B.y)},{F(g.Tree.x)},{F(g.Tree.y)}]")));
        sb.Append("],\"compounds\":[");
        sb.Append(string.Join(",", compounds.Select(c => $"[{F(c.X)},{F(c.Z)},{F(c.Yaw)},{F(c.W)},{F(c.D)},{F(c.Gate)}]")));
        sb.Append("],\"windmills\":[");
        sb.Append(string.Join(",", windmills.Select(w => $"[{F(w.x)},{F(w.y)},{F(w.z)}]")));
        sb.Append("],\"keep\":[");
        sb.Append(string.Join(",", keepOut.Select(k => $"[{F(k.x0)},{F(k.z0)},{F(k.x1)},{F(k.z1)}]")));
        var poles = lanes.Where(l => l.Main).SelectMany(l => AlongLane(l, 26f, 2, 0.35f, 4f));
        sb.Append("],\"poles\":[");
        sb.Append(string.Join(",", poles.Select(p => $"[{F(p.x)},{F(p.y)}]")));
        sb.Append("],\"height\":[");
        var rows = new System.Collections.Generic.List<string>();
        for (float z = 165; z >= -165; z -= 1f) rows.Add("[" + string.Join(",", Enumerable.Range(0, 331).Select(i => F(GroundHeight(-165 + i, z)))) + "]");
        sb.Append(string.Join(",", rows));
        sb.Append("]}");
        return sb.ToString();
    }
}
public static class Program
{
    public static void Main(string[] a)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var s = HillVillageBuilder.Dump(a.Length > 1 ? int.Parse(a[1]) : 46);
        System.IO.File.WriteAllText(a[0], s);
        System.Console.WriteLine($"plan in {sw.ElapsedMilliseconds} ms");
    }
}
