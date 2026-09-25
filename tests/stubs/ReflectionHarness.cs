// Pins down how the mod's reflection layer (R) reads and calls, and how its JSON writer escapes, since
// every game lookup goes through them. R caches and compiles each member on first use, so each case is
// asked twice: the first answer comes from resolving the member, the second from the cache. Compiled
// together with the mod and run by tests/test_build.py when a compiler that can run the result is
// available.

using System;
using System.Collections.Generic;

namespace QudHUDTests
{
    public class Base { public int Inherited = 7; }

    public class Sample : Base
    {
        public int Field = 3;
        public string Prop { get { return "prop"; } }
        private int hidden = 11;
        public int Throws { get { throw new InvalidOperationException(); } }
        public int Hidden() { return hidden; }

        public string Pick(string s) { return "string " + s; }
        public string Pick(int i) { return "int " + i; }
        public string Pick(object o) { return "object"; }
        public string Two(string a, int b) { return a + b; }
        public string Optional(string a, int b = 5, string c = "c") { return a + b + c; }
        public string Many(int a, int b, int c, int d) { return "" + a + b + c + d; }
        public int calls;
        public void Nothing() { calls++; }
        public string Boom() { throw new InvalidOperationException(); }
        public bool IsNull(object o) { return o == null; }
        public string Value(int i) { return "value"; }
        public T Generic<T>() { return default(T); }
    }

    public class Other { public int Field = 42; public string Pick(string s) { return "other " + s; } }

    public struct Point
    {
        public int X;
        public int Twice() { return X * 2; }
    }

    public static class Statics
    {
        public static int Count = 9;
        public static string Hello(string who) { return "hello " + who; }
        public static int Add(int a, int b) { return a + b; }
    }

    public static class Program
    {
        static int fails;

        static void Check(string label, bool ok, object got)
        {
            if (!ok) fails++;
            Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + label + (ok ? "" : "  -> " + (got ?? "null")));
        }

        // asked twice: once resolving, once from the cache
        static void Same(string label, Func<object> ask, object want)
        {
            for (int i = 0; i < 2; i++)
            {
                object got = ask();
                Check(label + (i == 0 ? "" : " (cached)"), Equals(got, want), got);
            }
        }

        public static int Main()
        {
            var s = new Sample();
            Same("field", () => QudHUD.R.Get(s, "Field"), 3);
            Same("property", () => QudHUD.R.Get(s, "Prop"), "prop");
            Same("private field", () => QudHUD.R.Get(s, "hidden"), 11);
            Same("inherited field", () => QudHUD.R.Get(s, "Inherited"), 7);
            Same("missing member reads null", () => QudHUD.R.Get(s, "Nope"), null);
            Same("a throwing getter reads null", () => QudHUD.R.Get(s, "Throws"), null);
            Same("null object reads null", () => QudHUD.R.Get(null, "Field"), null);
            Same("same name on another type", () => QudHUD.R.Get(new Other(), "Field"), 42);
            Same("and back to the first type", () => QudHUD.R.Get(s, "Field"), 3);
            QudHUD.R.Member field = QudHUD.R.M("Field");
            Same("held member", () => QudHUD.R.Get(s, field), 3);
            Same("held member on another type", () => QudHUD.R.Get(new Other(), field), 42);
            Same("struct field", () => QudHUD.R.Get(new Point { X = 4 }, "X"), 4);

            Same("no arguments", () => QudHUD.R.Call(s, "Hidden"), 11);
            Same("overload for a string", () => QudHUD.R.Call(s, "Pick", "a"), "string a");
            Same("overload for an int", () => QudHUD.R.Call(s, "Pick", 5), "int 5");
            Same("overload for anything else", () => QudHUD.R.Call(s, "Pick", 2.5), "object");
            Same("string again after the others", () => QudHUD.R.Call(s, "Pick", "b"), "string b");
            Same("same method name on another type", () => QudHUD.R.Call(new Other(), "Pick", "x"), "other x");
            Same("two arguments", () => QudHUD.R.Call(s, "Two", "a", 1), "a1");
            Same("optional parameters take their defaults", () => QudHUD.R.Call(s, "Optional", "a"), "a5c");
            Same("some optional parameters given", () => QudHUD.R.Call(s, "Optional", "a", 9), "a9c");
            Same("four arguments", () => QudHUD.R.Call(s, "Many", 1, 2, 3, 4), "1234");
            Same("a null argument", () => QudHUD.R.Call(s, "IsNull", (object)null), true);
            Same("a non-null argument after a null one", () => QudHUD.R.Call(s, "IsNull", "x"), false);
            Same("null cannot fill a value parameter", () => QudHUD.R.Call(s, "Value", (object)null), null);
            Same("wrong argument type finds nothing", () => QudHUD.R.Call(s, "Two", 1, 1), null);
            Same("a throwing method returns null", () => QudHUD.R.Call(s, "Boom"), null);
            Same("a missing method returns null", () => QudHUD.R.Call(s, "Nope"), null);
            Same("a generic method is not guessed at", () => QudHUD.R.Call(s, "Generic"), null);
            Same("null target returns null", () => QudHUD.R.Call(null, "Hidden"), null);
            Same("struct method", () => QudHUD.R.Call(new Point { X = 4 }, "Twice"), 8);
            QudHUD.R.Call(s, "Nothing");
            Same("a void method runs and returns null", () => QudHUD.R.Call(s, "Nothing"), null);
            Check("  and ran each time", s.calls == 3, s.calls);

            Same("static field", () => QudHUD.R.SGet("QudHUDTests.Statics", "Count"), 9);
            Same("static call by type name", () => QudHUD.R.SCall("QudHUDTests.Statics", "Hello", "you"), "hello you");
            Same("static call on a type", () => QudHUD.R.SCallT(typeof(Statics), "Add", 2, 3), 5);
            Same("static call on a missing type", () => QudHUD.R.SCall("QudHUDTests.Nowhere", "Hello", "you"), null);
            Same("instance lookups do not find statics", () => QudHUD.R.Call(s, "Hello", "you"), null);

            Check("strip leaves plain text alone", QudHUD.R.Strip("plain text") == "plain text", QudHUD.R.Strip("plain text"));
            Check("strip removes markup", QudHUD.R.Strip("{{r|bleeding}} &Wnow^k") == "bleeding now", QudHUD.R.Strip("{{r|bleeding}} &Wnow^k"));
            Check("strip keeps doubled marks", QudHUD.R.Strip("a && b") == "a & b", QudHUD.R.Strip("a && b"));

            var data = new Dictionary<string, object> {
                { "plain", "text" }, { "empty", "" }, { "quote", "say \"hi\"" }, { "slash", "a\\b" },
                { "lines", "one\ntwo\r\tend" }, { "tag", "</script>" }, { "seps", "a\u2028b\u2029c" }, { "control", "x\u0001y" },
                { "n", 5 }, { "neg", -3 }, { "big", 5000000000L }, { "d", 1.5 }, { "nan", double.NaN }, { "t", true }, { "f", false },
                { "none", null }, { "list", new List<object> { 1, "two", null } }, { "arr", new[] { 3, 4 } },
                { "nested", new Dictionary<string, object>(QudHUD.R.Keys) { { "k", "v" } } }
            };
            string json = QudHUD.Json.Write(data);
            string want = "{\"plain\":\"text\",\"empty\":\"\",\"quote\":\"say \\\"hi\\\"\",\"slash\":\"a\\\\b\"," +
                "\"lines\":\"one\\ntwo\\r\\tend\",\"tag\":\"\\u003c/script>\",\"seps\":\"a\\u2028b\\u2029c\",\"control\":\"x\\u0001y\"," +
                "\"n\":5,\"neg\":-3,\"big\":5000000000,\"d\":1.5,\"nan\":null,\"t\":true,\"f\":false," +
                "\"none\":null,\"list\":[1,\"two\",null],\"arr\":[3,4],\"nested\":{\"k\":\"v\"}}";
            Check("json escapes and shapes", json == want, json);

            Console.WriteLine(fails > 0 ? fails + " FAILURES" : "all green");
            return fails > 0 ? 1 : 0;
        }
    }
}
