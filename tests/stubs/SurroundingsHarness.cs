// Runs the mod's zone scan, which feeds the minimap and the nearby objects list, against a stand-in
// zone. It is compiled together with the mod so it can reach its internals, and run by
// tests/test_build.py when a compiler that can run the result is available. The mod reaches the game
// entirely by reflection, so these stand-ins only need the right member names, not the game's types.
// Whether the real game has those members is what the API probe in `build.py --check` answers.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace QudHUDTests
{
    public class Render { public string ColorString; public int RenderLayer; public bool Visible = true; }
    public class Brain { }
    public class Stat { public int Value; public int BaseValue; }

    public class Cell
    {
        public int X, Y;
        public bool explored, visible;
        public List<object> Objects = new List<object>();
        public bool IsExplored() { return explored; }
        public bool IsVisible() { return visible; }
    }

    public class Zone
    {
        public int Width, Height;
        public Cell[,] grid;
        public Zone(int w, int h)
        {
            Width = w; Height = h; grid = new Cell[w, h];
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) grid[x, y] = new Cell { X = x, Y = y };
        }
        public Cell GetCell(int x, int y) { return grid[x, y]; }
    }

    public class Thing
    {
        public string ShortDisplayName;
        public object CurrentCell;
        public Dictionary<string, object> parts = new Dictionary<string, object>();
        public HashSet<string> tags = new HashSet<string>();
        public bool takeable, hostile, led;
        public Hashtable Statistics;
        public object GetPart(string n) { object v; return parts.TryGetValue(n, out v) ? v : null; }
        public bool HasPart(string n) { return parts.ContainsKey(n); }
        public bool HasTag(string t) { return tags.Contains(t); }
        public bool IsTakeable() { return takeable; }
        public bool IsHostileTowards(XRL.World.GameObject p) { return hostile; }
        public bool IsPlayerLed() { return led; }
    }

    public class Player : XRL.World.GameObject
    {
        public object CurrentZone, CurrentCell;
        public int DistanceTo(object o)
        {
            Cell a = (Cell)CurrentCell, b = (Cell)((Thing)o).CurrentCell;
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
    }

    public static class Program
    {
        static int fails;

        static void Check(string label, bool ok, string got)
        {
            if (!ok) fails++;
            Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + label + (ok ? "" : "  -> " + got));
        }

        static Thing Put(Zone z, int x, int y, string name, string colour, int layer)
        {
            var t = new Thing { ShortDisplayName = name, CurrentCell = z.grid[x, y] };
            t.parts["Render"] = new Render { ColorString = colour, RenderLayer = layer };
            z.grid[x, y].Objects.Add(t);
            return t;
        }

        static Thing Creature(Zone z, int x, int y, string name, string colour)
        {
            Thing t = Put(z, x, y, name, colour, 10);
            t.parts["Brain"] = new Brain();
            return t;
        }

        static void Reset()
        {
            Type t = typeof(QudHUD.Surroundings);
            const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
            t.GetField("zone", S).SetValue(null, null);
            t.GetField("mapCells", S).SetValue(null, null);
            t.GetField("lastScan", S).SetValue(null, DateTime.MinValue);
            t.GetField("gapMs", S).SetValue(null, 150.0);
        }

        static void ForceRescan()
        {
            typeof(QudHUD.Surroundings).GetField("lastScan", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, DateTime.MinValue);
        }

        static string Decode(string rle)
        {
            var o = new StringBuilder();
            for (int i = 0; i < rle.Length; )
            {
                char c = rle[i++];
                int n = 0;
                while (i < rle.Length && char.IsDigit(rle[i])) n = n * 10 + (rle[i++] - '0');
                o.Append(c, n == 0 ? 1 : n);
            }
            return o.ToString();
        }

        static Dictionary<string, object> Scan(Player p)
        {
            var d = new Dictionary<string, object>();
            QudHUD.Surroundings.Build(p, d);
            return d;
        }

        static char At(Dictionary<string, object> d, int x, int y)
        {
            var m = (Dictionary<string, object>)d["map"];
            return Decode((string)m["c"])[y * (int)m["w"] + x];
        }

        static bool InView(Dictionary<string, object> d, int x, int y)
        {
            var m = (Dictionary<string, object>)d["map"];
            return Decode((string)m["v"])[y * (int)m["w"] + x] == 'V';
        }

        static List<string> Names(Dictionary<string, object> d)
        {
            var o = new List<string>();
            foreach (Dictionary<string, object> e in (List<object>)d["nearby"]) o.Add((string)e["name"]);
            return o;
        }

        static Dictionary<string, object> Entry(Dictionary<string, object> d, string name)
        {
            foreach (Dictionary<string, object> e in (List<object>)d["nearby"])
                if ((string)e["name"] == name) return e;
            return null;
        }

        public static int Main()
        {
            // --- encoding
            Check("run-length encodes runs", QudHUD.Surroundings.Rle(new StringBuilder("kkkk-y  ")) == "k4-y 2",
                  QudHUD.Surroundings.Rle(new StringBuilder("kkkk-y  ")));
            Check("colour is the first palette code", QudHUD.Surroundings.Colour("&G^k") == 'G', "");
            Check("unknown colour reads as the default", QudHUD.Surroundings.Colour("{{shader|x}}") == 'y', "");

            // --- a 10x4 zone. Columns 0-5 in view, 6-7 explored but out of sight, 8-9 never explored.
            // The game's explored flag is left false in view, to prove sight alone counts as explored.
            Reset();
            var z = new Zone(10, 4);
            for (int x = 0; x < 10; x++)
                for (int y = 0; y < 4; y++)
                {
                    Cell c = z.grid[x, y];
                    c.visible = x <= 5;
                    c.explored = x == 6 || x == 7;
                    Put(z, x, y, "dirt", "&w", 0);
                    if (y == 0) Put(z, x, y, "wall", "&K", 5);
                }
            var p = new Player { CurrentZone = z, CurrentCell = z.grid[2, 2] };
            z.grid[2, 2].Objects.Add(p);

            Thing dagger = Put(z, 3, 2, "dagger", "&c", 5); dagger.takeable = true;
            Creature(z, 4, 2, "merchant", "&W");
            Creature(z, 5, 2, "snapjaw", "&r").hostile = true;
            Creature(z, 1, 2, "Mehmet", "&G").led = true;
            Thing corpse = Creature(z, 1, 1, "hologram", "&C");
            corpse.Statistics = new Hashtable { { "Hitpoints", new Stat { Value = 0, BaseValue = 5 } } };
            Put(z, 3, 3, "salt pool", "&b", 3).parts["LiquidVolume"] = new object();
            Put(z, 4, 3, "witchwood tree", "&g", 5).tags.Add("Plant");
            Put(z, 5, 3, "chest", "&w", 5).parts["Inventory"] = new object();
            Put(z, 0, 3, "stairs down", "&y", 5).parts["StairsDown"] = new object();
            var hidden = Put(z, 2, 1, "hidden thing", "&M", 9);
            ((Render)hidden.parts["Render"]).Visible = false;
            Creature(z, 6, 2, "unseen hunter", "&R");

            var d = Scan(p);
            List<string> names = Names(d);
            string listed = string.Join(", ", names.ToArray());

            Check("nearby: item listed", names.Contains("dagger"), listed);
            Check("nearby: neutral creature listed", names.Contains("merchant"), listed);
            Check("nearby: liquid pool listed", names.Contains("salt pool"), listed);
            Check("nearby: plant listed", names.Contains("witchwood tree"), listed);
            Check("nearby: container listed", names.Contains("chest"), listed);
            Check("nearby: stairs listed", names.Contains("stairs down"), listed);
            Check("nearby: hostile left out, it has its own panel", !names.Contains("snapjaw"), listed);
            Check("nearby: companion left out, it has its own panel", !names.Contains("Mehmet"), listed);
            Check("nearby: dead creature left out", !names.Contains("hologram"), listed);
            Check("nearby: hidden object left out", !names.Contains("hidden thing"), listed);
            Check("nearby: nothing out of sight", !names.Contains("unseen hunter"), listed);
            Check("nearby: scenery left out", !names.Contains("dirt") && !names.Contains("wall"), listed);
            Check("nearby: nearest first", names[0] == "dagger", listed);
            Check("nearby: kinds are right",
                  (string)Entry(d, "dagger")["kind"] == "item" && (string)Entry(d, "merchant")["kind"] == "creature"
                  && (string)Entry(d, "salt pool")["kind"] == "liquid" && (string)Entry(d, "witchwood tree")["kind"] == "plant"
                  && (string)Entry(d, "chest")["kind"] == "container" && (string)Entry(d, "stairs down")["kind"] == "stairs", "");
            Check("nearby: distance and direction", (int)Entry(d, "dagger")["distance"] == 1 && (string)Entry(d, "dagger")["dir"] == "E",
                  Entry(d, "dagger")["distance"] + " " + Entry(d, "dagger")["dir"]);
            Check("nearby: carries the object's colour", (string)Entry(d, "dagger")["col"] == "c", (string)Entry(d, "dagger")["col"]);

            var m = (Dictionary<string, object>)d["map"];
            Check("map: size", (int)m["w"] == 10 && (int)m["h"] == 4, m["w"] + "x" + m["h"]);
            Check("map: player position", (int)m["px"] == 2 && (int)m["py"] == 2, m["px"] + "," + m["py"]);
            Check("map: in view counts as explored even when the flag says not", At(d, 0, 2) == 'w' && InView(d, 0, 2),
                  At(d, 0, 2).ToString());
            Check("map: the highest layer wins", At(d, 3, 2) == 'c' && At(d, 0, 0) == 'K', At(d, 3, 2) + "" + At(d, 0, 0));
            Check("map: a creature in view shows", At(d, 5, 2) == 'r', At(d, 5, 2).ToString());
            Check("map: a hidden object does not colour its cell", At(d, 2, 1) == 'w', At(d, 2, 1).ToString());
            Check("map: never explored is blank", At(d, 8, 2) == ' ' && At(d, 9, 0) == ' ', "'" + At(d, 8, 2) + "'");
            Check("map: explored out of sight shows ground, not the creature in it",
                  At(d, 6, 2) == 'w' && !InView(d, 6, 2), At(d, 6, 2).ToString());

            // --- the snapjaw walks out of view: the cell must fall back to what was last seen of it
            z.grid[5, 2].visible = false;
            Put(z, 5, 2, "new arrival", "&M", 7);   // arrives unseen; must not show either
            ForceRescan();
            d = Scan(p);
            Check("memory: a creature gone from view vanishes from the map", At(d, 5, 2) == 'w', At(d, 5, 2).ToString());
            Check("memory: what changed unseen is not shown", At(d, 5, 2) != 'M', At(d, 5, 2).ToString());
            Check("memory: the cell reads as remembered, not in view", !InView(d, 5, 2), "");

            // --- reuse: two builds moments apart share one scan
            Put(z, 3, 1, "fresh drop", "&O", 8).takeable = true;
            d = Scan(p);
            Check("throttle: a build moments later reuses the last scan", !Names(d).Contains("fresh drop"),
                  string.Join(", ", Names(d).ToArray()));
            ForceRescan();
            Check("throttle: the next scan picks it up", Names(Scan(p)).Contains("fresh drop"), "");

            // --- leaving the zone forces a fresh scan straight away
            var z2 = new Zone(3, 3);
            for (int x = 0; x < 3; x++) for (int y = 0; y < 3; y++) { z2.grid[x, y].visible = true; Put(z2, x, y, "sand", "&W", 0); }
            p.CurrentZone = z2; p.CurrentCell = z2.grid[1, 1];
            d = Scan(p);
            m = (Dictionary<string, object>)d["map"];
            Check("zone change: rescans at once, with the new size", (int)m["w"] == 3 && Decode((string)m["c"]) == "WWWWWWWWW",
                  m["w"] + " " + Decode((string)m["c"]));

            // --- cost and size on a full 80x25 zone, everything in view
            Reset();
            var big = new Zone(80, 25);
            var rnd = new Random(1);
            for (int x = 0; x < 80; x++)
                for (int y = 0; y < 25; y++)
                {
                    big.grid[x, y].visible = true;
                    Put(big, x, y, "ground", "&w", 0);
                    if (x == 0 || y == 0 || x == 79 || y == 24) Put(big, x, y, "wall", "&K", 5);
                    else if (rnd.Next(12) == 0) Put(big, x, y, "junk", "&c", 5).takeable = true;
                    else if (rnd.Next(40) == 0) Creature(big, x, y, "villager", "&W");
                }
            var bp = new Player { CurrentZone = big, CurrentCell = big.grid[40, 12] };
            Scan(bp);   // first scan also fetches the cells
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { ForceRescan(); d = Scan(bp); }
            double ms = sw.Elapsed.TotalMilliseconds / 20;
            string json = QudHUD.Json.Write(d);
            Console.WriteLine("INFO  full lit 80x25 zone: " + ms.ToString("0.0") + " ms per scan, " + json.Length + " chars of payload");
            Check("cost: a full lit zone scans in well under a frame", ms < 16, ms.ToString("0.0") + " ms");
            Check("size: a busy lit zone still fits a modest payload", json.Length < 6000, json.Length + " chars");

            Console.WriteLine(fails == 0 ? "all green" : fails + " FAILURES");
            return fails == 0 ? 0 : 1;
        }
    }
}
