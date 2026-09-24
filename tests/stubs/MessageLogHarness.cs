// Runs the mod's message log reader against stand-in message queues. It is compiled together with
// the mod so it can reach the mod's internals, and run by tests/test_build.py when a compiler that
// can also run the result is available. These queues are shapes the reader should cope with, not
// the game's own; whether the real game matches is something only the game can say.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace XRL
{
    public static partial class The
    {
        public static object Game;
    }
}

namespace XRL.Messages
{
    // the shape the reader looks for first: a list of strings called Messages
    public class MessageQueue
    {
        public List<string> Messages = new List<string>();
        public List<string> Pending = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" };
    }
}

namespace QudHUDTests
{
    public class GameStub { public PlayerStub Player = new PlayerStub(); }
    public class PlayerStub { public object Messages; }

    // no member called Messages, so the reader has to fall back to the longest list of strings
    public class OtherQueue
    {
        public List<int> Numbers = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        public List<string> Short = new List<string> { "not this one" };
        public List<string> Log = new List<string> { "older", "newer" };
        public string[] Longer = { "x", "y", "z", "p", "q" };
    }

    public class NothingReadable { public List<int> Numbers = new List<int> { 1 }; }

    public static class Program
    {
        static int fails;

        static void Check(string label, bool ok, string got)
        {
            if (!ok) fails++;
            Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + label + (ok ? "" : "  -> " + got));
        }

        static string Show(List<object> lines)
        {
            if (lines == null) return "null";
            var parts = new List<string>();
            foreach (object l in lines) parts.Add((string)l);
            return "[" + string.Join(" | ", parts.ToArray()) + "]";
        }

        // the reader remembers what it found, so each scenario starts it afresh
        static void Reset()
        {
            Type t = typeof(QudHUD.MessageLog);
            foreach (string f in new[] { "searched", "searchedInstance", "isStatic" })
                t.GetField(f, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, false);
            t.GetField("member", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
        }

        public static int Main()
        {
            Reset();
            XRL.The.Game = null;
            Check("before the game exists there is nothing to read", QudHUD.MessageLog.Recent(12) == null, "not null");

            // the first look found no queue; it must look again once there is one
            var game = new GameStub();
            var queue = new XRL.Messages.MessageQueue();
            queue.Messages.AddRange(new[] { "one", "two", "{{R|three}}", "four\nfive", "", "six\r\nseven" });
            game.Player.Messages = queue;
            XRL.The.Game = game;
            List<object> got = QudHUD.MessageLog.Recent(12);
            Check("finds the log once the game exists", got != null, Show(got));
            Check("reads Messages, not the longer list beside it, newest last",
                  Show(got) == "[one | two | {{R|three}} | four | five | six | seven]", Show(got));
            Check("keeps only the newest few", Show(QudHUD.MessageLog.Recent(3)) == "[five | six | seven]",
                  Show(QudHUD.MessageLog.Recent(3)));
            queue.Messages.Add("{{R|}}");
            queue.Messages.Add("   ");
            Check("skips lines that are blank or only colour codes",
                  Show(QudHUD.MessageLog.Recent(2)) == "[six | seven]", Show(QudHUD.MessageLog.Recent(2)));
            queue.Messages.Clear();
            got = QudHUD.MessageLog.Recent(12);
            Check("an empty log is empty, not missing", got != null && got.Count == 0, Show(got));

            Reset();
            game.Player.Messages = new OtherQueue();
            got = QudHUD.MessageLog.Recent(12);
            Check("without a Messages member, falls back to the longest list of strings",
                  Show(got) == "[x | y | z | p | q]", Show(got));

            Reset();
            game.Player.Messages = new NothingReadable();
            Check("a queue with nothing readable gives nothing", QudHUD.MessageLog.Recent(12) == null, "not null");

            Console.WriteLine(fails == 0 ? "all green" : fails + " FAILURES");
            return fails == 0 ? 0 : 1;
        }
    }
}
