// Fires turn events at the mod the way one step on the world map does, hundreds of them before the
// player gets control back, and counts how many times it rebuilt the page's data. It is compiled
// together with the mod and run by tests/test_build.py when a compiler that can run the result is
// available.

using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace QudHUDTests
{
    public class NamedZone
    {
        public string ZoneID;
        public int asked;
        public string name = "Joppa";
        public string DisplayName { get { asked++; return name; } }
    }

    public class ZonedPlayer : XRL.World.GameObject { public NamedZone CurrentZone; }

    public static class TurnEventHarness
    {
        static int fails;
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        static void Check(string label, bool ok, string got)
        {
            if (!ok) fails++;
            Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + label + (ok ? "" : "  -> " + got));
        }

        static int Builds() { return (int)typeof(QudHUD.Hud).GetField("builds", Hidden).GetValue(null); }

        public static int Main()
        {
            // write into a scratch folder, never the real Documents one
            string dir = Path.Combine(Path.GetTempPath(), "QudHUD-harness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            typeof(QudHUD.Hud).GetField("dir", Hidden).SetValue(null, dir);
            var player = new XRL.World.GameObject();
            try
            {
                // loading: the first update gets its own note, and does not count toward a slow step
                QudHUD.Hud.Attach(player);
                string note = UnityEngine.Debug.Lines.Find(l => l.StartsWith("[QudHUD] First update: "));
                Check("the first update is noted on its own", note != null && note.Contains("Slowest parts: "), note);
                if (note != null) Console.WriteLine("INFO  " + note.Substring(9));
                Check("and leaves nothing counted toward a slow step", Builds() == 0, Builds() + " builds");

                // a step that cost too much says which parts it went on
                QudHUD.Hud.Update(player, true);
                typeof(QudHUD.Hud).GetField("spentMs", Hidden).SetValue(null, 150.0);
                QudHUD.Hud.StepDone();
                string slow = UnityEngine.Debug.Lines.Find(l => l.StartsWith("[QudHUD] Slow step: "));
                Check("a slow step is noted with its slowest parts",
                      slow != null && slow.Contains("Slowest parts: ") && slow.Contains(" ms, "), slow);
                if (slow != null) Console.WriteLine("INFO  " + slow.Substring(9));
                Check("and counting starts afresh", Builds() == 0, Builds() + " builds");
                QudHUD.Hud.Attach(player);
                Check("only the first of the session is noted",
                      UnityEngine.Debug.Lines.FindAll(l => l.StartsWith("[QudHUD] First update: ")).Count == 1, "");

                QudHUD.TurnHook.Hooked = true;
                Thread.Sleep(300);
                QudHUD.Hud.Update(player, true);
                Check("the player getting control always updates", Builds() == 1, Builds() + " builds");
                Check("and writes the data file", QudHUD.Writer.Flush(5000) && File.Exists(Path.Combine(dir, "hud_data.js")), dir);

                for (int i = 0; i < 900; i++) QudHUD.Hud.Update(player, false);
                Check("with the input hook, a world map step's turns cost no extra updates", Builds() == 1, Builds() + " builds");
                Thread.Sleep(300);
                QudHUD.Hud.Update(player, false);
                Check("but time passing without input still moves the page along", Builds() == 2, Builds() + " builds");

                QudHUD.Hud.StepDone();
                Check("counts start again when the player has control", Builds() == 0, Builds() + " builds");

                // the zone's name is asked for on entering a zone and then every few seconds, not every update
                var zoned = new ZonedPlayer { CurrentZone = new NamedZone { ZoneID = "JoppaWorld.11.22.1.1.10" } };
                for (int i = 0; i < 20; i++) QudHUD.Hud.Update(zoned, true);
                Check("zone name asked once while staying put", zoned.CurrentZone.asked == 1, zoned.CurrentZone.asked + "");
                var before = zoned.CurrentZone;
                zoned.CurrentZone = new NamedZone { ZoneID = "JoppaWorld.11.22.1.2.10", name = "some forgotten ruins" };
                QudHUD.Hud.Update(zoned, true);
                Check("and asked again on entering another zone", zoned.CurrentZone.asked == 1, zoned.CurrentZone.asked + "");
                zoned.CurrentZone.name = "Bethesda Susa";
                Type snap = typeof(QudHUD.Hud).Assembly.GetType("QudHUD.Snapshot");
                snap.GetField("namedAt", Hidden).SetValue(null, DateTime.UtcNow.AddSeconds(-6));
                QudHUD.Hud.Update(zoned, true);
                Check("and again after a few seconds, catching a rename",
                      zoned.CurrentZone.asked == 2 && QudHUD.Writer.Flush(5000) && File.ReadAllText(Path.Combine(dir, "hud_data.js")).Contains("Bethesda Susa"), zoned.CurrentZone.asked + "");
                Check("the zone left behind was not asked again", before.asked == 1, before.asked + "");

                // once a session, the cost of an ordinary step, averaged over the first fifty; the checks
                // above took some steps already, so count from none
                QudHUD.Hud.StepDone();   // close the step the zone checks above were counted into
                typeof(QudHUD.Hud).GetField("typicalSteps", Hidden).SetValue(null, 0);
                typeof(QudHUD.Hud).GetField("typicalBuilds", Hidden).SetValue(null, 0);
                typeof(QudHUD.Hud).GetField("typicalMs", Hidden).SetValue(null, 0.0);
                ((System.Collections.IDictionary)typeof(QudHUD.Hud).Assembly.GetType("QudHUD.Snapshot")
                    .GetField("Typical", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
                for (int i = 0; i < 49; i++) { QudHUD.Hud.Update(player, true); QudHUD.Hud.StepDone(); }
                Check("no typical step note before fifty steps",
                      !UnityEngine.Debug.Lines.Exists(l => l.StartsWith("[QudHUD] Typical step")), "");
                QudHUD.Hud.Update(player, true); QudHUD.Hud.StepDone();
                string typical = UnityEngine.Debug.Lines.Find(l => l.StartsWith("[QudHUD] Typical step"));
                Check("a typical step note after fifty, naming parts", typical != null && typical.Contains("Slowest parts: ") && typical.Contains(" ms, "), typical);
                if (typical != null) Console.WriteLine("INFO  " + typical.Substring(9));
                for (int i = 0; i < 60; i++) { QudHUD.Hud.Update(player, true); QudHUD.Hud.StepDone(); }
                Check("and it says what the writer's thread spent", typical != null && typical.Contains("Off the game's thread: "), typical);
                Check("and only once", UnityEngine.Debug.Lines.FindAll(l => l.StartsWith("[QudHUD] Typical step")).Count == 1, "");

                // the file is written off the game's thread: posts pile up faster than a disk can take
                // them, and the newest one is what ends up on disk, with no temporary file left behind
                string target = Path.Combine(dir, "writer-test.js");
                var posting = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 500; i++) QudHUD.Writer.Post(target, "payload " + i + new string('x', 20000));
                double postMs = posting.Elapsed.TotalMilliseconds;
                Check("five hundred handovers take the game thread next to no time", postMs < 50, postMs.ToString("0.0") + " ms");
                Check("everything handed over reaches the disk", QudHUD.Writer.Flush(10000), "");
                Check("and the newest is what is on disk", File.ReadAllText(target).StartsWith("payload 499x"), File.ReadAllText(target).Substring(0, 12));
                Check("with no temporary file left", !File.Exists(target + ".tmp"), "");

                // without the hook the turn events are all there is, so outside auto-explore every one counts
                QudHUD.TurnHook.Hooked = false;
                for (int i = 0; i < 50; i++) QudHUD.Hud.Update(player, false);
                Check("without the input hook, every turn still updates", Builds() == 50, Builds() + " builds");
            }
            finally { QudHUD.Writer.Flush(5000); try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(fails > 0 ? fails + " FAILURES" : "all green");
            return fails > 0 ? 1 : 0;
        }
    }
}
