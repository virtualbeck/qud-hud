// Times the mod's work per update against a busy stand-in game: a player with a full sheet of effects,
// abilities and gear, in an 80 by 25 zone holding sixty creatures and scattered items. Compiled together
// with the built mod and the stubs in tests/stubs/Game.cs, and run by tests/bench/run.py, ideally under
// Mono, which is what the game runs on and where reflection costs several times what it does on modern
// .NET.
//
// Every game call here returns at once, so what this measures is the mod's own overhead: reflection,
// allocation, sorting and serialising. What the real game spends answering those calls comes on top.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using XRL.World;

namespace XRL
{
    public static partial class The
    {
        public static object Game;
        public static object ZoneManager;
    }
}

namespace XRL.Messages
{
    public class MessageQueue
    {
        public List<string> Messages = new List<string>();
        public static void AddPlayerMessage(string s) { }
    }
}

namespace XRL.World
{
    public class Effect
    {
        public const int TYPE_NEGATIVE = 2, TYPE_DISEASE = 4;
        public int Duration = 12;
        public string desc = "{{r|bleeding}}";
        public int type;
        public virtual string GetDescription() { return desc; }
        public virtual int GetEffectType() { return type; }
        public virtual string GetDetails() { return "Takes 1d2 damage per turn."; }
        public virtual bool SuppressInLookDisplay() { return false; }
    }
    public class Bleeding : Effect { }
    public class Hasted : Effect { }

    public static class Calendar
    {
        public static string GetTime() { return "Harvest Dawn"; }
        public static string GetDay() { return "4th"; }
        public static string GetMonth() { return "Nivvun Ut"; }
    }
}

namespace XRL.World.Parts
{
    public static class Leveler { public static int GetXPForLevel(int l) { return l * l * 150; } }
}

namespace XRL.World.Capabilities
{
    public static class Scanning { public static bool HasScanningFor(GameObject a, GameObject b) { return false; } }
    public static class DifficultyEvaluation
    {
        public static string GetDifficultyDescription(GameObject t, GameObject p) { return "{{w|Average}}"; }
    }
    public static class AutoAct { public static bool IsActive() { return false; } }
}

namespace XRL.Rules
{
    public static class Stats
    {
        public static int GetCombatAV(GameObject o) { return 6; }
        public static int GetCombatDV(GameObject o) { return 9; }
        public static int GetCombatMA(GameObject o) { return 4; }
    }
    public static class Strings
    {
        public static string WoundLevel(GameObject o) { return "Fine"; }
        public static string HealthStatusColor(GameObject o) { return "g"; }
    }
}

namespace QudHUDBench
{
    public class Statistic { public int Value, BaseValue; }
    public class Render { public string ColorString = "&y"; public int RenderLayer; public bool Visible = true; }
    public class Brain { public object PartyLeader; public bool IsHostileTowards(GameObject p) { return false; } }
    public class Stomach
    {
        public string WaterStatus() { return "{{g|Quenched}}"; }
        public string FoodStatus() { return "{{g|Sated}}"; }
    }
    public class Physics { public int Temperature = 25, FlameTemperature = 350, FreezeTemperature = -50; }
    public class Ability
    {
        public string DisplayName; public bool Enabled = true, Toggleable, ToggleState; public int Cooldown, CooldownTurns;
    }
    public class ActivatedAbilities { public Dictionary<Guid, Ability> AbilityByGuid = new Dictionary<Guid, Ability>(); }
    public class Body
    {
        public List<object> DismemberedParts = new List<object>();
        public List<GameObject> equipped = new List<GameObject>();
        public List<GameObject> GetEquippedObjects() { return equipped; }
    }
    public class EnergyCell { public int Charge = 800, MaxCharge = 1000; }
    public class EnergyCellSocket { public object Cell; }

    public class Cell
    {
        public int X, Y;
        public bool explored = true, visible;
        public List<object> Objects = new List<object>();
        public bool IsExplored() { return explored; }
        public bool IsVisible() { return visible; }
    }

    public class Zone
    {
        public int Width = 80, Height = 25, Z = 10;
        public string DisplayName = "Joppa", ZoneID = "JoppaWorld.11.22.1.1.10";
        public Cell[,] grid;
        public List<GameObject> brains = new List<GameObject>();
        public Zone()
        {
            grid = new Cell[Width, Height];
            for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++) grid[x, y] = new Cell { X = x, Y = y };
        }
        public Cell GetCell(int x, int y) { return grid[x, y]; }
        public List<GameObject> GetObjectsWithPart(string part) { return part == "Brain" ? brains : new List<GameObject>(); }
    }

    public class Thing : GameObject
    {
        public string ShortDisplayName, DisplayName, Blueprint = "Thing";
        public Cell CurrentCell;
        public Zone CurrentZone;
        public Dictionary<string, Statistic> Statistics = new Dictionary<string, Statistic>();
        public Dictionary<string, object> parts = new Dictionary<string, object>();
        public List<Effect> Effects = new List<Effect>();
        public HashSet<string> tags = new HashSet<string>();
        public bool takeable, hostile, led;
        public object GetPart(string n) { object v; return parts.TryGetValue(n, out v) ? v : null; }
        public new bool HasPart(string n) { return parts.ContainsKey(n); }
        public bool HasTag(string t) { return tags.Contains(t); }
        public bool IsTakeable() { return takeable; }
        public bool IsHostileTowards(GameObject p) { return hostile; }
        public bool IsPlayerLed() { return led; }
        public void Stat(string name, int v, int b) { Statistics[name] = new Statistic { Value = v, BaseValue = b }; }
    }

    public class Player : Thing
    {
        public string DisplayNameOnly = "Mehmet";
        public string GetGenotype() { return "Mutated Human"; }
        public string GetSubtype() { return "Nomad"; }
        public int GetFreeDrams() { return 32; }
        public bool IsAflame() { return false; }
        public bool IsFrozen() { return false; }
        public int GetCarriedWeight() { return 180; }
        public int GetMaxCarriedWeight() { return 300; }
        public int DistanceTo(object o)
        {
            Cell a = CurrentCell, b = ((Thing)o).CurrentCell;
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
    }

    public class GameStub { public PlayerStub Player = new PlayerStub(); public string PlayerName = "Mehmet"; }
    public class PlayerStub { public object Messages; }

    public static class Program
    {
        static Thing Put(Zone z, int x, int y, string name, string colour, int layer)
        {
            var t = new Thing { ShortDisplayName = name, DisplayName = name, CurrentCell = z.grid[x, y], CurrentZone = z };
            t.parts["Render"] = new Render { ColorString = colour, RenderLayer = layer };
            z.grid[x, y].Objects.Add(t);
            return t;
        }

        static Player World()
        {
            var z = new Zone();
            var rnd = new Random(7);
            for (int x = 0; x < z.Width; x++)
                for (int y = 0; y < z.Height; y++)
                {
                    Put(z, x, y, "dirt", "&w", 0);
                    if (rnd.Next(5) == 0) Put(z, x, y, "shale wall", "&K", 5);
                    // a lit area around the player, and the rest remembered
                    z.grid[x, y].visible = Math.Abs(x - 40) <= 25 && Math.Abs(y - 12) <= 12;
                }
            for (int i = 0; i < 40; i++)
            {
                Thing it = Put(z, rnd.Next(z.Width), rnd.Next(z.Height), "copper nugget", "&w", 3);
                it.takeable = true;
            }
            for (int i = 0; i < 60; i++)
            {
                Thing c = Put(z, rnd.Next(z.Width), rnd.Next(z.Height), "snapjaw scavenger", "&W", 10);
                c.parts["Brain"] = new Brain();
                c.Stat("Hitpoints", 10 + i % 15, 25);
                c.Stat("Level", 3 + i % 6, 3 + i % 6);
                c.hostile = i % 5 != 0;
                c.led = i % 12 == 0;
                if (i % 3 == 0) c.Effects.Add(new Bleeding { type = Effect.TYPE_NEGATIVE });
                z.brains.Add(c);
            }

            var p = new Player { ShortDisplayName = "Mehmet", DisplayName = "Mehmet", CurrentZone = z, CurrentCell = z.grid[40, 12] };
            p.parts["Render"] = new Render { ColorString = "&Y", RenderLayer = 10 };
            p.parts["Brain"] = new Brain();
            z.grid[40, 12].Objects.Add(p);
            z.brains.Add(p);
            foreach (string s in new[] { "Strength", "Agility", "Toughness", "Intelligence", "Willpower", "Ego" }) p.Stat(s, 18, 16);
            foreach (string s in new[] { "HeatResistance", "ColdResistance", "AcidResistance", "ElectricResistance" }) p.Stat(s, 10, 0);
            p.Stat("Hitpoints", 60, 80); p.Stat("Level", 14, 14); p.Stat("XP", 30000, 0);
            p.Stat("AP", 1, 0); p.Stat("SP", 120, 0); p.Stat("MP", 0, 0); p.Stat("Speed", 100, 100); p.Stat("MoveSpeed", 100, 100);
            p.parts["Stomach"] = new Stomach();
            p.parts["Physics"] = new Physics();
            var aa = new ActivatedAbilities();
            for (int i = 0; i < 30; i++)
                aa.AbilityByGuid[Guid.NewGuid()] = new Ability { DisplayName = "Ability " + i, CooldownTurns = i % 4 == 0 ? 12 : 0, Toggleable = i % 7 == 0 };
            p.parts["ActivatedAbilities"] = aa;
            for (int i = 0; i < 12; i++)
                p.Effects.Add(i % 2 == 0 ? (Effect)new Bleeding { type = Effect.TYPE_NEGATIVE } : new Hasted { desc = "{{G|hasted}}" });
            var body = new Body();
            for (int i = 0; i < 15; i++)
            {
                var item = new Thing { ShortDisplayName = "{{c|item " + i + "}}", DisplayName = "item " + i };
                if (i % 4 == 0)
                {
                    var cell = new Thing { ShortDisplayName = "cell" };
                    cell.parts["EnergyCell"] = new EnergyCell();
                    item.parts["EnergyCellSocket"] = new EnergyCellSocket { Cell = cell };
                }
                body.equipped.Add(item);
            }
            p.parts["Body"] = body;

            var queue = new XRL.Messages.MessageQueue();
            for (int i = 0; i < 300; i++) queue.Messages.Add("You hit {{r|the snapjaw}} for " + i + " damage.");
            var game = new GameStub();
            game.Player.Messages = queue;
            XRL.The.Game = game;
            XRL.The.Player = p;
            return p;
        }

        static double Time(int n, Action a)
        {
            for (int i = 0; i < Math.Max(5, n / 10); i++) a();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < n; i++) a();
            return sw.Elapsed.TotalMilliseconds * 1000.0 / n;   // microseconds each
        }

        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        public static int Main(string[] args)
        {
            int n = args.Length > 0 ? int.Parse(args[0]) : 300;
            Player p = World();
            Type snap = typeof(QudHUD.Hud).Assembly.GetType("QudHUD.Snapshot");
            Type sur = typeof(QudHUD.Hud).Assembly.GetType("QudHUD.Surroundings");
            FieldInfo lastScan = sur.GetField("lastScan", Hidden);

            string dir = Path.Combine(Path.GetTempPath(), "QudHUD-bench-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            typeof(QudHUD.Hud).GetField("dir", Hidden).SetValue(null, dir);
            try
            {
                // the very first update also resolves every member the mod uses, once for the session
                var first = Stopwatch.StartNew();
                QudHUD.Hud.Update(p, true);
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} ms  (once, as the game loads)", "first update", first.Elapsed.TotalMilliseconds));

                var d = new Dictionary<string, object>();
                var alerts = new List<Dictionary<string, object>>();
                string[] sections = { "BuildPlayer", "BuildZone", "BuildClock", "BuildAttributes", "BuildCombat", "BuildSurvival",
                                      "BuildEffects", "BuildAbilities", "BuildGear", "BuildHostiles", "BuildCompanions" };
                Console.WriteLine("runtime: " + (Type.GetType("Mono.Runtime") != null ? "Mono" : "CoreCLR") + ", " + n + " runs each");
                double total = 0;
                foreach (string s in sections)
                {
                    MethodInfo mi = snap.GetMethod(s, Hidden);
                    ParameterInfo[] ps = mi.GetParameters();
                    object[] a = ps.Length == 3 ? new object[] { p, d, alerts } : ps.Length == 2 ? new object[] { p, d } : new object[] { d };
                    double us = Time(n, () => { alerts.Clear(); mi.Invoke(null, a); });
                    total += us;
                    Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us", s, us));
                }
                MethodInfo recent = typeof(QudHUD.Hud).Assembly.GetType("QudHUD.MessageLog").GetMethod("Recent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                double msg = Time(n, () => recent.Invoke(null, new object[] { 12 }));
                total += msg;
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us", "messages", msg));
                MethodInfo build = sur.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                double scan = Time(n, () => { lastScan.SetValue(null, DateTime.MinValue); build.Invoke(null, new object[] { p, d }); });
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us  (at most every 150ms)", "zone scan", scan));

                Dictionary<string, object> whole = QudHUD.Snapshot.Build(p);
                double json = Time(n, () => QudHUD.Json.Write(whole));
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us  ({2} bytes)", "json", json, QudHUD.Json.Write(whole).Length));

                // the whole update as the input hook runs it: snapshot, serialise, write the file. A turn
                // passes between updates, so the data differs each time and is really written.
                Statistic hp = p.Statistics["Hitpoints"];
                double update = Time(n, () => { hp.Value = hp.Value == 60 ? 59 : 60; lastScan.SetValue(null, DateTime.MinValue); QudHUD.Hud.Update(p, true); });
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us  (snapshot, zone scan, json, file)", "whole update", update));
                Console.WriteLine(string.Format("  {0,-18}{1,9:0.0} us", "sections total", total));
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine("all green");   // it ran start to finish; the numbers are for reading
            return 0;
        }
    }
}
