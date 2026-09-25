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
                QudHUD.Hud.Attach(player);
                Check("only the first of the session is noted",
                      UnityEngine.Debug.Lines.FindAll(l => l.StartsWith("[QudHUD] First update: ")).Count == 1, "");

                QudHUD.TurnHook.Hooked = true;
                Thread.Sleep(300);
                QudHUD.Hud.Update(player, true);
                Check("the player getting control always updates", Builds() == 1, Builds() + " builds");
                Check("and writes the data file", File.Exists(Path.Combine(dir, "hud_data.js")), dir);

                for (int i = 0; i < 900; i++) QudHUD.Hud.Update(player, false);
                Check("with the input hook, a world map step's turns cost no extra updates", Builds() == 1, Builds() + " builds");
                Thread.Sleep(300);
                QudHUD.Hud.Update(player, false);
                Check("but time passing without input still moves the page along", Builds() == 2, Builds() + " builds");

                QudHUD.Hud.StepDone();
                Check("counts start again when the player has control", Builds() == 0, Builds() + " builds");

                // without the hook the turn events are all there is, so outside auto-explore every one counts
                QudHUD.TurnHook.Hooked = false;
                for (int i = 0; i < 50; i++) QudHUD.Hud.Update(player, false);
                Check("without the input hook, every turn still updates", Builds() == 50, Builds() + " builds");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
            Console.WriteLine(fails > 0 ? fails + " FAILURES" : "all green");
            return fails > 0 ? 1 : 0;
        }
    }
}
