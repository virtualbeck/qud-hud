// Qud HUD __VERSION__: second monitor heads-up display for Caves of Qud.
// Each turn the player's state is written to Documents/QudHUD/hud_data.js.
// Open Documents/QudHUD/hud.html in a browser on your second monitor.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using XRL;
using XRL.World;

namespace XRL.World.Parts
{
    [Serializable]
    public class QudHUD_Tracker : IPart
    {
        public override bool WantEvent(int ID, int cascade)
        {
            return base.WantEvent(ID, cascade) || ID == EndTurnEvent.ID || ID == BeginTakeActionEvent.ID;
        }

        public override bool HandleEvent(EndTurnEvent E)
        {
            QudHUD.Hud.Update(The.Player ?? ParentObject, false);
            return base.HandleEvent(E);
        }

        public override bool HandleEvent(BeginTakeActionEvent E)
        {
            QudHUD.Hud.Update(The.Player ?? ParentObject, false);
            return base.HandleEvent(E);
        }
    }
}

namespace QudHUD
{
    [PlayerMutator]
    public class HudPlayerMutator : IPlayerMutator
    {
        public void mutate(GameObject player)
        {
            Hud.Attach(player);
        }
    }

    [HasCallAfterGameLoaded]
    public class HudLoadHook
    {
        [CallAfterGameLoaded]
        public static void OnGameLoaded()
        {
            Hud.Attach(The.Player);
        }
    }

    public static class Hud
    {
        public const string Version = "__VERSION__";
        static string dir;
        static string lastJson;
        static long seq;
        static DateTime lastWrite = DateTime.MinValue;
        static bool announced;
        // what the mod cost the game since the player last had control, for the slow step note
        static int turnEvents, builds;
        static double spentMs;
        static DateTime lastSlowNote = DateTime.MinValue;
        static readonly HashSet<string> logged = new HashSet<string>();

        public static string Dir
        {
            get
            {
                if (dir != null) return dir;
                string root = null;
                try { root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); } catch { }
                if (string.IsNullOrEmpty(root))
                {
                    try { root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch { }
                }
                if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                dir = Path.Combine(root, "QudHUD");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static void Attach(GameObject player)
        {
            if (player == null) return;
            try
            {
                if (!player.HasPart("QudHUD_Tracker")) player.AddPart(new XRL.World.Parts.QudHUD_Tracker());
            }
            catch (Exception ex) { Log("attach", ex); }

            TurnHook.Install();

            try { File.WriteAllText(Path.Combine(Dir, "hud.html"), HtmlPage, new UTF8Encoding(false)); }
            catch (Exception ex) { Log("html", ex); }

            if (!announced)
            {
                announced = true;
                string path = Path.Combine(Dir, "hud.html");
                R.SCall("XRL.Messages.MessageQueue", "AddPlayerMessage", "{{C|Qud HUD}}: open " + path.Replace("&", "&&").Replace("^", "^^") + " on your second monitor.");
                UnityEngine.Debug.Log("[QudHUD] Display page: " + path);
            }
            Update(player, true);
        }

        public static void Update(GameObject player, bool force)
        {
            if (player == null) return;
            DateTime now = DateTime.UtcNow;
            if (!force)
            {
                turnEvents++;
                double since = (now - lastWrite).TotalMilliseconds;
                // The turn events fire every game turn, and one step on the world map passes hundreds of
                // them. When the input hook is in, it already catches every turn the player sees, so the
                // events only have to keep the page moving while time passes without input, and a few
                // updates a second does that. Without the hook they are all there is, and only resting
                // and auto-explore are capped.
                if (TurnHook.Hooked ? since < 250
                    : since < 300 && R.Bool(R.SCall("XRL.World.Capabilities.AutoAct", "IsActive"))) return;
            }

            var took = System.Diagnostics.Stopwatch.StartNew();
            try { Write(player, now); }
            finally { builds++; spentMs += took.Elapsed.TotalMilliseconds; }
        }

        // Called as the player gets control back. A step that cost the game noticeable time is noted in
        // Player.log, at most once a minute, with enough to tell whether the mod or the turn count did it.
        public static void StepDone()
        {
            DateTime now = DateTime.UtcNow;
            if (spentMs >= 100 && (now - lastSlowNote).TotalSeconds >= 60)
            {
                lastSlowNote = now;
                UnityEngine.Debug.Log("[QudHUD] Slow step: " + spentMs.ToString("0", CultureInfo.InvariantCulture) + " ms in " + builds +
                    " update(s) over " + turnEvents + " turn event(s).");
            }
            turnEvents = builds = 0;
            spentMs = 0;
        }

        static void Write(GameObject player, DateTime now)
        {
            string json;
            try { json = Json.Write(Snapshot.Build(player)); }
            catch (Exception ex) { Log("build", ex); return; }
            lastWrite = now;
            if (json == lastJson) return;

            try
            {
                seq++;
                string payload = "window.QUD_HUD={\"version\":\"" + Version + "\",\"seq\":" + seq + ",\"stamp\":\"" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "\",\"data\":" + json + "};\n";
                string path = Path.Combine(Dir, "hud_data.js");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, payload, new UTF8Encoding(false));
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                }
                catch
                {
                    File.Copy(tmp, path, true);
                    File.Delete(tmp);
                }
                lastJson = json;
            }
            catch (Exception ex) { Log("write", ex); }
        }

        public static void Log(string key, Exception ex)
        {
            if (!logged.Add(key)) return;
            try { UnityEngine.Debug.LogWarning("[QudHUD] " + key + ": " + ex); } catch { }
        }

        const string HtmlPage = @"__HTML__";
    }

    // Updates the HUD right before the game waits for player input, after all
    // creatures and effects have resolved. Patched via Harmony at runtime; if the
    // target method is missing in a future version, the turn events still work.
    public static class TurnHook
    {
        static bool tried;
        public static bool Hooked;

        public static void Install()
        {
            if (tried) return;
            tried = true;
            try
            {
                Type harmonyType = R.FindType("HarmonyLib.Harmony");
                Type methodType = R.FindType("HarmonyLib.HarmonyMethod");
                Type core = R.FindType("XRL.Core.XRLCore");
                if (harmonyType == null || methodType == null || core == null)
                {
                    UnityEngine.Debug.Log("[QudHUD] Input hook unavailable, using turn events only.");
                    return;
                }

                object harmony = Activator.CreateInstance(harmonyType, "QudHUD.TurnHook");
                MethodInfo hook = typeof(TurnHook).GetMethod("BeforePlayerInput", BindingFlags.Public | BindingFlags.Static);
                object prefix = Activator.CreateInstance(methodType, hook);

                int patched = 0;
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MethodInfo mi in core.GetMethods(all))
                {
                    if (mi.Name != "PlayerTurn" || mi.IsGenericMethodDefinition || mi.IsAbstract) continue;
                    if (R.Call(harmony, "Patch", mi, prefix) != null) patched++;
                }
                Hooked = patched > 0;
                UnityEngine.Debug.Log("[QudHUD] Input hook patched " + patched + " method(s).");
            }
            catch (Exception ex) { Hud.Log("hook", ex); }
        }

        public static void BeforePlayerInput()
        {
            try { Hud.Update(The.Player, true); Hud.StepDone(); }
            catch (Exception ex) { Hud.Log("hook update", ex); }
        }
    }

    static class Snapshot
    {
        static readonly string[] AttrKeys = { "Strength", "Agility", "Toughness", "Intelligence", "Willpower", "Ego" };
        static readonly string[] AttrLabels = { "STR", "AGI", "TOU", "INT", "WIL", "EGO" };

        static readonly HashSet<string> Diseases = new HashSet<string> {
            "Glotrot", "GlotrotOnset", "Ironshank", "IronshankOnset", "Monochrome", "MonochromeOnset",
            "FungalSporeInfection", "SporeCloudPoison"
        };
        static readonly HashSet<string> Disabling = new HashSet<string> {
            "Stun", "Stunned", "Paralyzed", "Asleep", "Frozen", "CardiacArrest", "Dominated", "Confused", "Terrified"
        };
        static readonly HashSet<string> KnownBad = new HashSet<string> {
            "Bleeding", "Poisoned", "PoisonGasPoison", "Blind", "Dazed", "Prone", "Stuck", "Shaken", "Exhausted",
            "Lost", "Overburdened", "Hobbled", "Ill", "Nosebleed", "Broken", "Rusted", "ShatteredArmor", "Disoriented"
        };

        static int typeNegative = -1, typeDisease = -1;

        // Every creature in the zone, asked of the game once per update. The game walks the whole zone to
        // answer, and both the hostile and the companion lists need it.
        static List<object> creatures;
        static bool creaturesRead;

        static List<object> Creatures(GameObject p)
        {
            if (creaturesRead) return creatures;
            creaturesRead = true;
            IEnumerable objs = R.Call(R.Get(p, CurrentZone), "GetObjectsWithPart", "Brain") as IEnumerable;
            if (objs == null) return creatures = null;
            creatures = new List<object>();
            foreach (object o in objs) if (o != null && !ReferenceEquals(o, p)) creatures.Add(o);
            return creatures;
        }

        public static Dictionary<string, object> Build(GameObject p)
        {
            var d = new Dictionary<string, object>(R.Keys);
            var alerts = new List<Dictionary<string, object>>();
            creaturesRead = false;
            try { Sections(p, d, alerts); }
            finally { creatures = null; creaturesRead = false; }   // never hold the zone's creatures between turns

            alerts.Sort((a, b) => ((int)b["sev"]).CompareTo((int)a["sev"]));
            var list = new List<object>();
            foreach (var a in alerts) list.Add(a);
            d["alerts"] = list;
            return d;
        }

        static void Sections(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {

            Section("player", () => BuildPlayer(p, d, alerts));
            Section("place", () => BuildPlace(p, d));
            Section("attributes", () => BuildAttributes(p, d));
            Section("combat", () => BuildCombat(p, d));
            Section("survival", () => BuildSurvival(p, d, alerts));
            Section("effects", () => BuildEffects(p, d, alerts));
            Section("abilities", () => BuildAbilities(p, d));
            Section("gear", () => BuildGear(p, d, alerts));
            Section("hostiles", () => BuildHostiles(p, d, alerts));
            Section("companions", () => BuildCompanions(p, d));
            Section("messages", () => d["messages"] = MessageLog.Recent(12));
            Section("surroundings", () => Surroundings.Build(p, d));
        }

        // Orders entries by rank, then by a text field with its markup removed. Each entry is stripped
        // once rather than on every comparison, and equals keep the order they came in.
        static void SortBy(List<object> list, Func<Dictionary<string, object>, int> rank, Func<Dictionary<string, object>, string> text)
        {
            int n = list.Count;
            var order = new int[n];
            var ranks = new int[n];
            var keys = new string[n];
            for (int i = 0; i < n; i++)
            {
                var e = (Dictionary<string, object>)list[i];
                order[i] = i;
                ranks[i] = rank == null ? 0 : rank(e);
                keys[i] = R.Strip(text(e));
            }
            Array.Sort(order, (x, y) =>
            {
                int c = ranks[x].CompareTo(ranks[y]);
                if (c == 0) c = string.Compare(keys[x], keys[y], StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : x.CompareTo(y);
            });
            var sorted = new object[n];
            for (int i = 0; i < n; i++) sorted[i] = list[order[i]];
            list.Clear();
            list.AddRange(sorted);
        }

        static void Section(string name, Action a)
        {
            try { a(); } catch (Exception ex) { Hud.Log("section " + name, ex); }
        }

        // dismiss names a reminder the page may let the player silence, for things that are not
        // going anywhere and are shown elsewhere on the display anyway. It is a stable name rather than
        // the wording, which changes with the number in it.
        static void Alert(List<Dictionary<string, object>> alerts, int sev, string text, string dismiss = null)
        {
            var a = new Dictionary<string, object>(R.Keys) { { "sev", sev }, { "text", text } };
            if (dismiss != null) a["dis"] = dismiss;
            alerts.Add(a);
        }

        // Stats

        // read for every creature every turn, so held rather than looked up by name
        static readonly R.Member Statistics = R.M("Statistics"), Value = R.M("Value"), BaseValue = R.M("BaseValue"),
            CurrentCell = R.M("CurrentCell"), CurrentZone = R.M("CurrentZone"), CellVisible = R.M("IsVisible"),
            X = R.M("X"), Y = R.M("Y"), Effects = R.M("Effects"), ShortDisplayName = R.M("ShortDisplayName");

        static object StatObj(object o, string name)
        {
            IDictionary dict = R.Get(o, Statistics) as IDictionary;
            if (dict == null) return null;
            // one lookup rather than Contains and then the indexer; a missing key reads as null
            return dict[name];
        }
        static int SV(object o, string name, int def = 0) { return R.Int(R.Get(StatObj(o, name), Value), def); }
        static int SB(object o, string name, int def = 0) { return R.Int(R.Get(StatObj(o, name), BaseValue), def); }
        static object SVOrNull(object o, string name)
        {
            object s = StatObj(o, name);
            return s == null ? null : (object)R.Int(R.Get(s, Value), 0);
        }

        public static string Name(object o)
        {
            string n = R.Str(R.Get(o, ShortDisplayName));
            if (string.IsNullOrEmpty(n)) n = R.Str(R.Get(o, "DisplayName"));
            if (string.IsNullOrEmpty(n)) n = R.Str(R.Get(o, "Blueprint"));
            return n ?? "something";
        }

        static void BuildPlayer(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var pl = new Dictionary<string, object>(R.Keys);
            string name = R.Str(R.Get(R.SGet("XRL.The", "Game"), "PlayerName"));
            if (string.IsNullOrEmpty(name)) name = R.Str(R.Get(p, "DisplayNameOnly"));
            if (string.IsNullOrEmpty(name)) name = Name(p);
            pl["name"] = name;
            pl["genotype"] = R.Str(R.Call(p, "GetGenotype"));
            pl["subtype"] = R.Str(R.Call(p, "GetSubtype"));

            int level = SV(p, "Level", 1);
            pl["level"] = level;
            pl["xp"] = SV(p, "XP");
            object next = R.SCall("XRL.World.Parts.Leveler", "GetXPForLevel", level + 1);
            pl["xpNext"] = next == null ? null : (object)R.Int(next, 0);
            object prev = R.SCall("XRL.World.Parts.Leveler", "GetXPForLevel", level);
            pl["xpPrev"] = prev == null ? null : (object)R.Int(prev, 0);

            int hp = SV(p, "Hitpoints"), max = SB(p, "Hitpoints");
            bool exact = Perception.SelfExact(p);
            pl["exact"] = exact;
            if (exact)
            {
                pl["hp"] = hp;
                pl["hpMax"] = max;
                pl["hcol"] = Health.Color(p, hp, max);
            }
            else pl["health"] = Health.Describe(p, hp, max);
            d["player"] = pl;

            if (max > 0)
            {
                double pct = (double)hp / max;
                string text = exact ? hp + " / " + max : R.Strip(Health.Describe(p, hp, max));
                if (pct <= 0.25) Alert(alerts, 3, "Hit points critical: " + text);
                else if (pct <= 0.5) Alert(alerts, 2, "Hit points low: " + text);
            }
        }

        static void BuildPlace(GameObject p, Dictionary<string, object> d)
        {
            var zone = new Dictionary<string, object>(R.Keys);
            object z = R.Get(p, "CurrentZone");
            if (z != null)
            {
                string zn = R.Str(R.Get(z, "DisplayName"));
                if (string.IsNullOrEmpty(zn))
                {
                    object zm = R.SGet("XRL.The", "ZoneManager");
                    zn = R.Str(R.Call(zm, "GetZoneDisplayName", R.Str(R.Get(z, "ZoneID"))));
                }
                if (string.IsNullOrEmpty(zn)) zn = R.Str(R.Get(z, "BaseDisplayName"));
                zone["name"] = zn;
                int zz = R.Int(R.Get(z, "Z"), 10);
                zone["depth"] = zz > 10 ? zz - 10 : 0;
            }
            d["zone"] = zone;

            var clock = new Dictionary<string, object>(R.Keys);
            clock["time"] = R.Str(R.SCall("XRL.World.Calendar", "GetTime"));
            clock["day"] = R.Str(R.SCall("XRL.World.Calendar", "GetDay"));
            clock["month"] = R.Str(R.SCall("XRL.World.Calendar", "GetMonth"));
            d["clock"] = clock;
        }

        static void BuildAttributes(GameObject p, Dictionary<string, object> d)
        {
            var list = new List<object>();
            for (int i = 0; i < AttrKeys.Length; i++)
            {
                if (StatObj(p, AttrKeys[i]) == null) continue;
                int v = SV(p, AttrKeys[i]);
                list.Add(new Dictionary<string, object>(R.Keys) {
                    { "label", AttrLabels[i] }, { "value", v }, { "base", SB(p, AttrKeys[i]) },
                    { "mod", (int)Math.Floor((v - 16) / 2.0) }
                });
            }
            d["attributes"] = list;
        }

        static void BuildCombat(GameObject p, Dictionary<string, object> d)
        {
            var c = new Dictionary<string, object>(R.Keys);
            object av = R.SCall("XRL.Rules.Stats", "GetCombatAV", p);
            object dv = R.SCall("XRL.Rules.Stats", "GetCombatDV", p);
            object ma = R.SCall("XRL.Rules.Stats", "GetCombatMA", p);
            c["av"] = av != null ? (object)R.Int(av, 0) : SVOrNull(p, "AV");
            c["dv"] = dv != null ? (object)R.Int(dv, 0) : SVOrNull(p, "DV");
            c["ma"] = ma != null ? (object)R.Int(ma, 0) : SVOrNull(p, "MA");
            c["quickness"] = SVOrNull(p, "Speed");
            object ms = SVOrNull(p, "MoveSpeed");
            c["moveSpeed"] = ms == null ? null : (object)(200 - (int)ms);
            d["combat"] = c;

            d["resist"] = new Dictionary<string, object>(R.Keys) {
                { "heat", SVOrNull(p, "HeatResistance") }, { "cold", SVOrNull(p, "ColdResistance") },
                { "acid", SVOrNull(p, "AcidResistance") }, { "elec", SVOrNull(p, "ElectricResistance") }
            };
            d["points"] = new Dictionary<string, object>(R.Keys) {
                { "ap", SV(p, "AP") }, { "sp", SV(p, "SP") }, { "mp", SV(p, "MP") }
            };
        }

        static void BuildSurvival(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var s = new Dictionary<string, object>(R.Keys);

            object stomach = R.Call(p, "GetPart", "Stomach");
            if (stomach != null)
            {
                string water = R.Str(R.Call(stomach, "WaterStatus"));
                string food = R.Str(R.Call(stomach, "FoodStatus"));
                s["water"] = water;
                s["food"] = food;
                string w = R.Strip(water).ToLowerInvariant(), f = R.Strip(food).ToLowerInvariant();
                if (w.Contains("dehydrated")) Alert(alerts, 3, "Dehydrated. Drink water now.");
                else if (w.Contains("parched")) Alert(alerts, 2, "Parched. Drink soon.");
                if (f.Contains("starving")) Alert(alerts, 3, "Starving. Eat now.");
                else if (f.Contains("famished")) Alert(alerts, 2, "Famished. Eat soon.");
            }

            object drams = R.Call(p, "GetFreeDrams");
            if (drams != null) s["drams"] = R.Int(drams, 0);

            object phys = R.Call(p, "GetPart", "Physics");
            if (phys != null)
            {
                object t = R.Get(phys, "Temperature");
                if (t != null)
                {
                    int temp = R.Int(t, 25);
                    object flameO = R.Get(phys, "FlameTemperature"), freezeO = R.Get(phys, "FreezeTemperature");
                    s["temp"] = temp;
                    s["flame"] = flameO == null ? null : (object)R.Int(flameO, 0);
                    s["freeze"] = freezeO == null ? null : (object)R.Int(freezeO, 0);

                    object aflame = R.Call(p, "IsAflame"), frozen = R.Call(p, "IsFrozen");
                    bool onFire = aflame != null ? R.Bool(aflame) : (flameO != null && temp >= R.Int(flameO, int.MaxValue));
                    bool isFrozen = frozen != null ? R.Bool(frozen) : (freezeO != null && temp <= R.Int(freezeO, int.MinValue));
                    if (onFire) Alert(alerts, 3, "You are on fire! (" + temp + "\u00B0)");
                    else if (flameO != null && R.Int(flameO, 0) > 0 && temp >= R.Int(flameO, 0) * 0.8) Alert(alerts, 2, "Dangerously hot: " + temp + "\u00B0");
                    if (isFrozen) Alert(alerts, 3, "You are frozen solid (" + temp + "\u00B0)");
                    else if (freezeO != null && temp <= R.Int(freezeO, 0) + 15) Alert(alerts, 2, "Dangerously cold: " + temp + "\u00B0");
                }
            }

            object cw = R.Call(p, "GetCarriedWeight"), mw = R.Call(p, "GetMaxCarriedWeight");
            if (cw != null && mw != null)
            {
                s["weight"] = R.Int(cw, 0);
                s["maxWeight"] = R.Int(mw, 0);
            }
            d["survival"] = s;

            int ap = SV(p, "AP"), sp = SV(p, "SP"), mp = SV(p, "MP");
            if (ap > 0) Alert(alerts, 1, ap + " unspent attribute point" + (ap == 1 ? "" : "s"), "ap");
            if (mp > 0) Alert(alerts, 1, mp + " unspent mutation point" + (mp == 1 ? "" : "s"), "mp");
            if (sp >= 50) Alert(alerts, 1, sp + " unspent skill points", "sp");
        }

        static void EnsureEffectTypes()
        {
            if (typeNegative >= 0) return;
            typeNegative = R.Int(R.SGet("XRL.World.Effect", "TYPE_NEGATIVE"), 0);
            typeDisease = R.Int(R.SGet("XRL.World.Effect", "TYPE_DISEASE"), 0);
        }

        // Returns null for effects the game hides from the player. Brief leaves out the duration and the
        // details text, which only the player's own Effects panel shows, and which cost the game a
        // formatted string each.
        static Dictionary<string, object> DescribeEffect(object fx, bool brief = false)
        {
            EnsureEffectTypes();
            string desc = R.Str(R.Call(fx, "GetDescription"));
            if (string.IsNullOrEmpty(desc) || R.Strip(desc).Trim().Length == 0) return null;
            string cls = fx.GetType().Name;
            int type = R.Int(R.Call(fx, "GetEffectType"), 0);
            bool disease = Diseases.Contains(cls) || (typeDisease > 0 && (type & typeDisease) != 0);
            bool negative = disease || Disabling.Contains(cls) || KnownBad.Contains(cls) || (typeNegative > 0 && (type & typeNegative) != 0);
            var e = new Dictionary<string, object>(R.Keys) {
                { "name", desc },
                { "class", cls },
                { "negative", negative },
                { "disease", disease }
            };
            if (brief) return e;
            e["duration"] = R.Int(R.Get(fx, "Duration"), 0);
            e["details"] = R.Str(R.Call(fx, "GetDetails"));
            return e;
        }

        static List<object> EffectsOf(object o)
        {
            var list = new List<object>();
            IEnumerable fxs = (R.Get(o, Effects) ?? R.Get(o, "_Effects")) as IEnumerable;
            if (fxs == null) return list;
            foreach (object fx in fxs) if (fx != null) list.Add(fx);
            return list;
        }

        static void BuildEffects(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var list = new List<object>();
            foreach (object fx in EffectsOf(p))
            {
                Dictionary<string, object> e = DescribeEffect(fx);
                if (e == null) continue;
                list.Add(e);
                string cls = (string)e["class"];
                string nm = R.Strip((string)e["name"]);
                if ((bool)e["disease"]) Alert(alerts, 3, "Afflicted: " + nm);
                else if (Disabling.Contains(cls)) Alert(alerts, 3, "Incapacitated: " + nm);
                else if ((bool)e["negative"]) Alert(alerts, 2, "Suffering: " + nm);
            }
            // Harmful first, then by name.
            SortBy(list, e => (bool)e["disease"] ? 0 : (bool)e["negative"] ? 1 : 2, e => (string)e["name"]);
            d["effects"] = list;
        }

        static void BuildAbilities(GameObject p, Dictionary<string, object> d)
        {
            var list = new List<object>();
            object aa = R.Call(p, "GetPart", "ActivatedAbilities");
            IDictionary byGuid = R.Get(aa, "AbilityByGuid") as IDictionary;
            if (byGuid != null)
            {
                foreach (object entry in byGuid.Values)
                {
                    if (entry == null) continue;
                    string name = R.Str(R.Get(entry, "DisplayName"));
                    if (string.IsNullOrEmpty(name)) continue;
                    object enabledO = R.Get(entry, "Enabled");
                    object turnsO = R.Get(entry, "CooldownTurns") ?? R.Get(entry, "CooldownRounds");
                    int cd = R.Int(R.Get(entry, "Cooldown"), 0);
                    list.Add(new Dictionary<string, object>(R.Keys) {
                        { "name", name },
                        { "enabled", enabledO == null || R.Bool(enabledO) },
                        { "cooldown", cd > 0 || R.Int(turnsO, 0) > 0 },
                        { "cooldownTurns", turnsO == null ? 0 : R.Int(turnsO, 0) },
                        { "toggleable", R.Bool(R.Get(entry, "Toggleable")) },
                        { "toggled", R.Bool(R.Get(entry, "ToggleState")) }
                    });
                }
            }
            SortBy(list, null, e => (string)e["name"]);
            d["abilities"] = list;
        }

        static void BuildGear(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var missing = new List<object>();
            var issues = new List<object>();
            var cells = new List<object>();

            object body = R.Call(p, "GetPart", "Body") ?? R.Get(p, "Body");

            IEnumerable dism = R.Get(body, "DismemberedParts") as IEnumerable;
            if (dism != null)
            {
                foreach (object dp in dism)
                {
                    object part = R.Get(dp, "Part");
                    string pn = R.Str(R.Get(part, "Name"));
                    if (string.IsNullOrEmpty(pn)) pn = R.Str(R.Get(part, "Description"));
                    if (string.IsNullOrEmpty(pn)) continue;
                    missing.Add(pn);
                    Alert(alerts, 2, "Missing body part: " + R.Strip(pn));
                }
            }

            IEnumerable eq = R.Call(body, "GetEquippedObjects") as IEnumerable;
            if (eq == null)
            {
                var fallback = new List<object>();
                IEnumerable parts = R.Call(body, "GetParts") as IEnumerable;
                if (parts != null)
                    foreach (object bp in parts)
                    {
                        object equipped = R.Get(bp, "Equipped");
                        if (equipped != null) fallback.Add(equipped);
                    }
                eq = fallback;
            }
            if (eq != null)
            {
                var seen = new HashSet<object>();
                foreach (object item in eq)
                {
                    if (item == null || !seen.Add(item)) continue;
                    // an item's display name costs the game real work, so it is only made for an item
                    // that has something to report
                    string known = null;
                    Func<string> itemName = () => known ?? (known = Name(item));

                    foreach (object fx in EffectsOf(item))
                    {
                        Dictionary<string, object> e = DescribeEffect(fx, true);
                        if (e == null || !(bool)e["negative"]) continue;
                        issues.Add(new Dictionary<string, object>(R.Keys) { { "item", itemName() }, { "issue", e["name"] } });
                        Alert(alerts, 2, R.Strip(itemName()) + " is " + R.Strip((string)e["name"]));
                    }

                    if (R.Call(item, "GetPart", "FungalInfection") != null || R.Bool(R.Call(item, "HasTag", "FungalInfection")))
                    {
                        issues.Add(new Dictionary<string, object>(R.Keys) { { "item", itemName() }, { "issue", "{{m|fungal infection}}" } });
                        Alert(alerts, 1, "Fungal infection: " + R.Strip(itemName()));
                    }

                    object socket = R.Call(item, "GetPart", "EnergyCellSocket");
                    if (socket != null)
                    {
                        object cell = R.Get(socket, "Cell");
                        if (cell == null)
                        {
                            cells.Add(new Dictionary<string, object>(R.Keys) { { "item", itemName() }, { "empty", true } });
                            Alert(alerts, 2, R.Strip(itemName()) + " has no energy cell");
                        }
                        else
                        {
                            object ec = R.Call(cell, "GetPart", "EnergyCell");
                            int charge = R.Int(R.Get(ec, "Charge"), 0), max = R.Int(R.Get(ec, "MaxCharge"), 0);
                            if (max > 0)
                            {
                                cells.Add(new Dictionary<string, object>(R.Keys) { { "item", itemName() }, { "charge", charge }, { "max", max } });
                                if (charge <= 0) Alert(alerts, 2, R.Strip(itemName()) + ": energy cell empty");
                                else if (charge < max * 0.15) Alert(alerts, 1, R.Strip(itemName()) + ": energy cell low (" + (int)Math.Round(100.0 * charge / max) + "%)");
                            }
                        }
                    }
                }
            }

            d["gear"] = new Dictionary<string, object>(R.Keys) { { "missing", missing }, { "issues", issues }, { "cells", cells } };
        }

        // Rank for ordering only, never shown. Deliberately our own ladder rather than the game's
        // GetDifficultyFromDescription: that returns a number whose direction we cannot check from
        // here, and guessing it wrong would silently sort the list backwards. Wording the ladder
        // does not know simply ranks 0, which drops the tiebreak rather than misordering it.
        static int DangerRank(string rating)
        {
            switch (R.Strip(rating).Trim().ToLowerInvariant())
            {
                case "impossible": return 6;
                case "very tough": return 5;
                case "tough": return 4;
                case "average": return 3;
                case "easy": return 2;
                case "trivial": return 1;
                default: return 0;
            }
        }

        static string Rating(object o, GameObject p)
        {
            try { return Difficulty.Describe(o, p); }
            catch (Exception ex) { Hud.Log("rating", ex); return ""; }
        }

        // GameObject.IsVisible() reflects things like stealth/invisibility, not real line of sight -
        // a creature behind a closed door still reads as visible. Cell.IsVisible() is the actual
        // per-turn FOV/light check the game itself renders from, so prefer that when it's available.
        static bool CurrentlyVisible(object o)
        {
            object cellVisible = R.Call(R.Get(o, CurrentCell), CellVisible);
            if (cellVisible is bool) return (bool)cellVisible;
            return R.Bool(R.Call(o, "IsVisible"));
        }

        static readonly string[] Compass = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        // 8-way compass direction from p to o, or "here" for the same tile. Null if positions
        // aren't available. hud.html maps this to an arrow glyph.
        internal static string Direction(GameObject p, object o)
        {
            object pc = R.Get(p, CurrentCell), oc = R.Get(o, CurrentCell);
            if (pc == null || oc == null) return null;
            int dx = R.Int(R.Get(oc, X), 0) - R.Int(R.Get(pc, X), 0);
            int dy = R.Int(R.Get(oc, Y), 0) - R.Int(R.Get(pc, Y), 0);
            if (dx == 0 && dy == 0) return "here";
            double deg = Math.Atan2(-dy, dx) * (180.0 / Math.PI);
            if (deg < 0) deg += 360;
            return Compass[(int)Math.Round(deg / 45.0) % 8];
        }

        // Health and effects as the player can perceive them, shared by hostiles and companions.
        static void AddCondition(GameObject p, object o, Dictionary<string, object> entry, int hp, int hpMax)
        {
            bool exact = Perception.Sees(p, o);
            entry["exact"] = exact;
            // Without a scanner the exact numbers are not sent at all, so the page cannot leak them
            // back through a proportional bar.
            if (exact) { entry["hp"] = hp; entry["hpMax"] = hpMax; entry["hcol"] = Health.Color(o, hp, hpMax); }
            else entry["health"] = Health.Describe(o, hp, hpMax);

            // What is wrong with it, filtered by the game's own rule for what shows when you look at
            // a creature rather than by listing everything it happens to be carrying.
            var fx = new List<object>();
            foreach (object e in EffectsOf(o))
            {
                if (R.Bool(R.Call(e, "SuppressInLookDisplay"))) continue;
                Dictionary<string, object> described = DescribeEffect(e, true);
                if (described == null) continue;
                fx.Add(new Dictionary<string, object>(R.Keys) {
                    { "name", described["name"] },
                    { "negative", described["negative"] },
                    { "disease", described["disease"] }
                });
                if (fx.Count >= 6) break;
            }
            if (fx.Count > 0) entry["effects"] = fx;
        }

        // Anyone following the player, directly or through another follower. There is no published
        // API, so this asks the likeliest calls in turn; none answering means no companions, and the
        // panel simply says so rather than guessing.
        // A creature can linger in the zone's Brain list after death (seen with holograms); treat 0 or
        // below as dead regardless of why it was not removed.
        internal static bool IsDead(object o)
        {
            object hp = SVOrNull(o, "Hitpoints");
            return hp != null && (int)hp <= 0;
        }

        internal static bool IsHostile(object o, GameObject p)
        {
            object hostile = R.Call(o, "IsHostileTowards", p);
            if (hostile == null) hostile = R.Call(R.Call(o, "GetPart", "Brain"), "IsHostileTowards", p);
            return R.Bool(hostile);
        }

        internal static bool IsCompanion(object o, GameObject p)
        {
            object led = R.Call(o, "IsPlayerLed");
            if (led is bool) return (bool)led;
            object leader = R.Get(R.Call(o, "GetPart", "Brain"), "PartyLeader");
            if (leader != null) return ReferenceEquals(leader, p);
            return R.Bool(R.Call(o, "IsLedBy", p));
        }

        static void BuildCompanions(GameObject p, Dictionary<string, object> d)
        {
            List<object> objs = Creatures(p);
            if (objs == null) { d["companions"] = null; return; }

            var found = new List<Dictionary<string, object>>();
            foreach (object o in objs)
            {
                if (IsDead(o) || !IsCompanion(o, p)) continue;
                var entry = new Dictionary<string, object>(R.Keys) { { "name", Name(o) }, { "level", SV(o, "Level") } };
                // Out of sight, you know they exist but not where they are or how they are doing.
                bool seen = CurrentlyVisible(o);
                entry["seen"] = seen;
                if (seen)
                {
                    entry["distance"] = R.Int(R.Call(p, "DistanceTo", o), 99);
                    entry["dir"] = Direction(p, o);
                    AddCondition(p, o, entry, SV(o, "Hitpoints"), SB(o, "Hitpoints"));
                }
                found.Add(entry);
            }

            // Those in sight first and nearest first, then the rest by name.
            var list = new List<object>();
            foreach (var f in found) list.Add(f);
            SortBy(list, e => (bool)e["seen"] ? (int)e["distance"] : int.MaxValue, e => (string)e["name"]);
            d["companions"] = list;
        }

        // What the hostile list is sorted by, kept beside the entry rather than in it. Hit points order
        // the list even for creatures whose numbers the player cannot read, and never reach the page.
        sealed class Foe
        {
            public Dictionary<string, object> Entry;
            public string Rating, Name;
            public int Danger, Hp, HpMax, Distance, Level, NearRank, DangerRank;
        }

        static void BuildHostiles(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            List<object> objs = Creatures(p);
            if (objs == null) { d["hostiles"] = null; return; }

            var found = new List<Foe>();
            foreach (object o in objs)
            {
                // in view before hostile: the game answers the first from the cell it already lit,
                // and has to weigh up feelings for the second
                if (IsDead(o) || !CurrentlyVisible(o) || !IsHostile(o, p)) continue;
                var f = new Foe
                {
                    Hp = SV(o, "Hitpoints"), HpMax = SB(o, "Hitpoints"), Level = SV(o, "Level"),
                    Distance = R.Int(R.Call(p, "DistanceTo", o), 99)
                };
                string rating = Rating(o, p);
                f.Name = Name(o);
                f.Rating = R.Strip(rating).Trim();
                f.Danger = DangerRank(f.Rating);
                f.Entry = new Dictionary<string, object>(R.Keys) {
                    { "name", f.Name },
                    { "level", f.Level },
                    { "rating", rating },
                    { "distance", f.Distance },
                    { "dir", Direction(p, o) }
                };
                AddCondition(p, o, f.Entry, f.Hp, f.HpMax);
                found.Add(f);
            }

            // Two orderings. Nearest is what gets sent; the dangerous one is sent as a position per
            // creature so the page can switch without ever receiving the hit points behind it.
            // The difficulty rating alone is too blunt to order by: a high level character reads
            // everything as Trivial, which is why the size of the creature breaks the tie.
            var byDanger = new List<Foe>(found);
            byDanger.Sort((a, b) =>
            {
                int c = b.Danger.CompareTo(a.Danger);
                if (c != 0) return c;
                c = b.HpMax.CompareTo(a.HpMax);
                if (c != 0) return c;
                c = a.Distance.CompareTo(b.Distance);
                if (c != 0) return c;
                c = b.Level.CompareTo(a.Level);
                if (c != 0) return c;
                return b.Hp.CompareTo(a.Hp);
            });
            for (int i = 0; i < byDanger.Count; i++) byDanger[i].DangerRank = i;

            // Nearest first, then the more dangerous of equals, then whichever has more left in it.
            found.Sort((a, b) =>
            {
                int c = a.Distance.CompareTo(b.Distance);
                if (c != 0) return c;
                c = b.Danger.CompareTo(a.Danger);
                if (c != 0) return c;
                return b.Hp.CompareTo(a.Hp);
            });

            // Trivial creatures are left out of the two proximity alarms. To a character who has
            // outgrown them they are not a pressing matter, and an alarm that pulses red for every
            // snapjaw teaches you to stop looking at it. They still appear in the panel. A rating
            // the ladder does not recognise ranks 0 and still counts, so unfamiliar wording errs
            // toward raising the alarm rather than hiding it.
            int adjacent = 0, threats = 0, nearest = -1;
            for (int i = 0; i < found.Count; i++)
            {
                found[i].NearRank = i;
                if (found[i].Danger == 1) continue;
                threats++;
                int dist = found[i].Distance;
                if (nearest < 0) nearest = dist;
                if (dist <= 1) adjacent++;
            }

            // The page shows 15 at a time. Cutting the list in nearest order before sending would
            // mean a dangerous creature beyond the fifteenth nearest could never appear, whichever
            // sort was chosen, so send the nearest 15 and the most dangerous 15. In small fights
            // they are the same creatures.
            const int Shown = 15;
            var list = new List<object>();
            foreach (Foe f in found)
            {
                if (f.NearRank >= Shown && f.DangerRank >= Shown) continue;
                f.Entry["nearRank"] = f.NearRank;
                f.Entry["dangerRank"] = f.DangerRank;
                list.Add(f.Entry);
            }
            d["hostiles"] = list;

            int dangerous = 0;
            foreach (Foe f in found)
            {
                if ((f.Rating == "Impossible" || f.Rating == "Very Tough") && dangerous++ < 3)
                    Alert(alerts, 2, R.Strip(f.Name) + " in sight (" + f.Rating + ")");
            }

            if (adjacent > 0) Alert(alerts, 3, adjacent + " hostile" + (adjacent == 1 ? "" : "s") + " adjacent to you");
            else if (threats > 0) Alert(alerts, 2, threats + " hostile" + (threats == 1 ? "" : "s") + " in sight, nearest " + nearest + " away");
        }
    }

    // Vanilla shows a word, not a number, unless you carry something that reads exact stats off a
    // creature. These are the game's health states and their thresholds.
    // The game already works out the word and the colour it shows for a creature's health, so ask
    // it rather than reproducing the thresholds here. The ladder below is only a fallback for a
    // build where those functions have moved.
    static class Health
    {
        static readonly char[] ColourMarks = { '&', '{', '}', '|' };
        static Type strings;
        static bool searched;

        static void Ensure()
        {
            if (searched) return;
            searched = true;
            strings = R.FindTypeBySimpleName("Strings", "XRL.Rules.Strings");
        }

        static bool HasFigures(string s)
        {
            string t = R.Strip(s);
            for (int i = 0; i < t.Length; i++) if (char.IsDigit(t[i])) return true;
            return false;
        }

        static string Clean(string col)
        {
            if (col == null) return "";
            // usually a bare letter already, which needs no copy
            if (col.IndexOfAny(ColourMarks) < 0) return col.Trim();
            var sb = new StringBuilder(col.Length);
            foreach (char c in col) if (Array.IndexOf(ColourMarks, c) < 0) sb.Append(c);
            return sb.ToString().Trim();
        }

        public static string Describe(object o, int hp, int max)
        {
            Ensure();
            string word = R.Str(R.SCallT(strings, "WoundLevel", o));
            // A wound level is a word. Figures mean the game handed back a scan readout instead,
            // hit points followed by armour and dodge, which belongs on the exact path and would
            // otherwise arrive here as loose numbers with its glyphs stripped out.
            if (!string.IsNullOrEmpty(word) && R.Strip(word).Trim().Length > 0 && !HasFigures(word))
            {
                if (word.IndexOf("{{", StringComparison.Ordinal) >= 0) return word;  // already carries its own colour
                string col = Clean(R.Str(R.SCallT(strings, "HealthStatusColor", o)));
                return col.Length > 0 ? "{{" + col + "|" + word + "}}" : word;
            }
            string w, c;
            Ladder(hp, max, out w, out c);
            return w.Length > 0 ? "{{" + c + "|" + w + "}}" : "";
        }

        // The colour the game gives this creature's health, so the figures and the bar match what
        // the game would show. Its bands follow the wound levels, not a plain percentage: 52% of
        // maximum is Injured and reads amber, where halving a percentage would still call it green.
        public static string Color(object o, int hp, int max)
        {
            Ensure();
            string col = Clean(R.Str(R.SCallT(strings, "HealthStatusColor", o)));
            if (col.Length > 0) return col;
            string w, c;
            Ladder(hp, max, out w, out c);
            return c;
        }

        static void Ladder(int hp, int max, out string word, out string col)
        {
            if (max <= 0) { word = ""; col = ""; return; }
            if (hp >= max) { word = "Perfect"; col = "G"; return; }
            int pct = (int)(100.0 * hp / max);
            if (pct >= 66) { word = "Fine"; col = "g"; }
            else if (pct >= 33) { word = "Injured"; col = "W"; }
            else if (pct >= 15) { word = "Wounded"; col = "o"; }
            else { word = "Badly Wounded"; col = "R"; }
        }
    }

    // Whether the character can read a creature's exact hit points. The game computes this for
    // everything that grants it (VISAGE once booted, the optical scanner implants, anything modded)
    // so the only correct implementation is to ask it. Failing to find it leaves the vanilla word,
    // which is the safe direction: the HUD never shows more than the character can see.
    static class Perception
    {
        static Type scanning;
        static bool searched;

        // Whether the player reads figures on their own sheet. Nerve poppy is the Analgesia defect
        // in code, which is why the display name appears nowhere in the assembly. It is checked by
        // name because the game exposes no general "can I read my own hit points" call: scanning is
        // about other creatures, and HasScanningFor(you, you) is false for everyone.
        public static bool SelfExact(GameObject player)
        {
            if (R.Call(player, "GetPart", "Analgesia") == null) return true;
            // Analgesia takes the figures off your own sheet, unless something puts them back by
            // letting you scan yourself, which is what a powered VISAGE does.
            return Sees(player, player);
        }

        public static bool Sees(GameObject player, object target)
        {
            if (!searched)
            {
                searched = true;
                scanning = R.FindTypeBySimpleName("Scanning", "XRL.World.Capabilities.Scanning");
                if (scanning == null) UnityEngine.Debug.LogWarning("[QudHUD] no Scanning capability found; hostiles will show health words only.");
            }
            return R.Bool(R.SCallT(scanning, "HasScanningFor", player, target));
        }
    }

    // What is around the player, as the game's own minimap and nearby objects window show it. One pass
    // over the zone feeds both. Every lookup goes through reflection and fails soft, and
    // `build.py --check` compiles tests/probe/GameApi.cs against the real game to say which of these
    // guesses hold.
    //
    // The map shows only what the character knows: nothing for unexplored cells, the current view for
    // visible ones, and for cells explored but out of sight what they looked like when last seen, with
    // no creatures. That memory is also what keeps the scan cheap, since out-of-sight cells need no
    // looking at.
    static class Surroundings
    {
        const string Palette = "kKrRgGbBcCmMwWyYoO";
        const int NearbyMax = 20;

        // asked of every cell and every object in view, so held here rather than looked up by name
        static readonly R.Member IsVisible = R.M("IsVisible"), IsExplored = R.M("IsExplored"), ExploredFlag = R.M("Explored"),
            Objects = R.M("Objects"), GetPart = R.M("GetPart"), Visible = R.M("Visible"), RenderLayer = R.M("RenderLayer"),
            ColorString = R.M("ColorString");

        // What never changes about an object: its render part, whether it is a creature, and what kind
        // of nearby thing it is, if any. Held weakly, so the cache cannot keep a destroyed object (a
        // spent gas cloud, a projectile) alive.
        class Info { public object Render; public bool Creature; public string Kind; }
        struct Candidate { public object O; public Info Info; public char Col; public int Dist; }
        static readonly ConditionalWeakTable<object, Info> infos = new ConditionalWeakTable<object, Info>();

        static object zone;          // the zone everything below belongs to
        static int width, height;
        static object[] cells;
        static bool[] explored;      // once explored always explored, so never asked about again
        static char[] remembered;    // each cell as last seen, creatures left out
        static DateTime lastScan = DateTime.MinValue;
        static double gapMs = 150;
        static string mapCells, mapSeen;
        static List<object> nearby;

        public static void Build(GameObject p, Dictionary<string, object> d)
        {
            object z = R.Get(p, "CurrentZone"), here = R.Get(p, "CurrentCell");
            if (z == null || here == null) { d["map"] = null; d["nearby"] = null; return; }
            int px = R.Int(R.Get(here, "X"), -1), py = R.Int(R.Get(here, "Y"), -1);

            // The turn hooks fire within milliseconds of each other and a scan is the costliest thing
            // the mod does, so one taken moments ago is reused. The gap stretches if scans turn out
            // slow, so a large lit zone cannot eat into the game's own frame time.
            DateTime now = DateTime.UtcNow;
            if (!ReferenceEquals(z, zone) || mapCells == null || (now - lastScan).TotalMilliseconds >= gapMs)
            {
                Scan(p, z, px, py);
                DateTime done = DateTime.UtcNow;
                gapMs = Math.Max(150, (done - now).TotalMilliseconds * 4);
                lastScan = done;
            }

            d["nearby"] = nearby;
            if (mapCells == null) { d["map"] = null; return; }
            d["map"] = new Dictionary<string, object>(R.Keys) {
                { "w", width }, { "h", height }, { "px", px }, { "py", py }, { "c", mapCells }, { "v", mapSeen }
            };
        }

        static void Scan(GameObject p, object z, int px, int py)
        {
            if (!ReferenceEquals(z, zone))
            {
                zone = z;
                width = R.Int(R.Get(z, "Width"), 80);
                height = R.Int(R.Get(z, "Height"), 25);
                cells = new object[width * height];
                explored = new bool[cells.Length];
                remembered = new char[cells.Length];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        cells[y * width + x] = R.Call(z, "GetCell", x, y);
            }

            var c = new StringBuilder(cells.Length);
            var v = new StringBuilder(cells.Length);
            var found = new List<Candidate>();
            bool any = false;
            for (int i = 0; i < cells.Length; i++)
            {
                object cell = cells[i];
                char top = ' ', ground;
                bool visible = false;
                if (cell != null)
                {
                    any = true;
                    visible = R.Bool(R.Call(cell, IsVisible));
                    // anything in view is explored, even if the game's own flag cannot be read
                    if (visible) explored[i] = true;
                    else if (!explored[i]) explored[i] = Explored(cell);
                }
                if (visible)
                {
                    int dist = Math.Max(Math.Abs(i % width - px), Math.Abs(i / width - py));
                    Look(p, cell, found, dist, out top, out ground);
                    remembered[i] = ground;
                }
                else if (explored[i])
                {
                    // explored before this session, so never seen by the mod: remember it now
                    if (remembered[i] == '\0') { Look(p, cell, null, 0, out top, out ground); remembered[i] = ground; }
                    top = remembered[i];
                }
                c.Append(top);
                v.Append(visible ? 'V' : '.');
            }

            if (!any) { mapCells = mapSeen = null; nearby = null; return; }
            mapCells = Rle(c);
            mapSeen = Rle(v);
            nearby = Nearest(p, found);
        }

        // Nearest first. Only now are creatures checked for being dead, hostile or a companion, and only
        // until the list is full, since those checks are the expensive ones.
        static List<object> Nearest(GameObject p, List<Candidate> found)
        {
            found.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            var list = new List<object>();
            foreach (Candidate f in found)
            {
                if (list.Count >= NearbyMax) break;
                if (f.Info.Creature && (Snapshot.IsDead(f.O) || Snapshot.IsHostile(f.O, p) || Snapshot.IsCompanion(f.O, p)))
                    continue;
                list.Add(new Dictionary<string, object>(R.Keys) {
                    { "name", Snapshot.Name(f.O) },
                    { "kind", f.Info.Creature ? "creature" : f.Info.Kind },
                    { "col", f.Col.ToString() },
                    { "distance", R.Int(R.Call(p, "DistanceTo", f.O), f.Dist) },
                    { "dir", Snapshot.Direction(p, f.O) }
                });
            }
            return list;
        }

        static bool Explored(object cell)
        {
            object e = R.Call(cell, IsExplored);
            if (e is bool) return (bool)e;
            return R.Bool(R.Get(cell, ExploredFlag));
        }

        static Info About(object o)
        {
            Info info;
            if (infos.TryGetValue(o, out info)) return info;
            info = new Info { Render = R.Call(o, GetPart, "Render"), Creature = R.Call(o, GetPart, "Brain") != null };
            if (!info.Creature) info.Kind = Kind(o);
            infos.Add(o, info);
            return info;
        }

        // The kinds of thing the game's nearby list shows, besides creatures. Walls, floors and the like
        // are scenery and come back as null.
        static string Kind(object o)
        {
            if (R.Bool(R.Call(o, "HasPart", "StairsUp")) || R.Bool(R.Call(o, "HasPart", "StairsDown"))) return "stairs";
            if (R.Bool(R.Call(o, "IsTakeable"))) return "item";
            if (R.Call(o, "GetPart", "LiquidVolume") != null) return "liquid";
            if (R.Bool(R.Call(o, "HasTag", "Plant"))) return "plant";
            if (R.Call(o, "GetPart", "Inventory") != null) return "container";
            return null;
        }

        // The colour on top of a cell, and the one with creatures left out, which is what gets
        // remembered. Collects anything worth listing as nearby when given somewhere to put it.
        static void Look(GameObject p, object cell, List<Candidate> found, int dist, out char top, out char ground)
        {
            top = ground = '-';
            int topLayer = int.MinValue, groundLayer = int.MinValue;
            object held = R.Get(cell, Objects);
            IList list = held as IList;
            IEnumerable objs = list ?? held as IEnumerable;
            if (objs == null) return;
            // a list is walked by index, which spares allocating an enumerator for every cell
            int count = list != null ? list.Count : -1;
            IEnumerator each = list == null ? objs.GetEnumerator() : null;
            for (int i = 0; list != null ? i < count : each.MoveNext(); i++)
            {
                object o = list != null ? list[i] : each.Current;
                if (o == null) continue;
                Info info = About(o);
                if (info.Render == null) continue;
                object shown = R.Get(info.Render, Visible);
                if (shown is bool && !(bool)shown) continue;
                int layer = R.Int(R.Get(info.Render, RenderLayer), 0);
                char col = Colour(R.Str(R.Get(info.Render, ColorString)));
                if (layer >= topLayer) { topLayer = layer; top = col; }
                if (!info.Creature && layer >= groundLayer) { groundLayer = layer; ground = col; }
                if (found != null && !ReferenceEquals(o, p) && (info.Creature || info.Kind != null))
                    found.Add(new Candidate { O = o, Info = info, Col = col, Dist = dist });
            }
        }

        // The foreground colour in a string such as "&y" or "&G^k", as one palette letter.
        internal static char Colour(string s)
        {
            if (s != null)
                for (int i = 0; i + 1 < s.Length; i++)
                    if (s[i] == '&' && Palette.IndexOf(s[i + 1]) >= 0) return s[i + 1];
            return 'y';
        }

        // Run-length encoding: each symbol, then how many times it repeats when that is more than once.
        // Symbols are never digits, so the counts read back unambiguously. A zone is mostly long runs
        // of wall, ground and unexplored, which is what keeps the map small enough to send every turn.
        internal static string Rle(StringBuilder s)
        {
            var o = new StringBuilder();
            for (int i = 0; i < s.Length; )
            {
                int j = i + 1;
                while (j < s.Length && s[j] == s[i]) j++;
                o.Append(s[i]);
                if (j - i > 1) o.Append(j - i);
                i = j;
            }
            return o.ToString();
        }
    }

    // The game's message log. There is no published way to read it, so the first call looks for it by
    // reflection on the game's message queue: a list of strings called Messages if there is one,
    // otherwise the longest list of strings it holds. Which member it settled on goes to Player.log
    // once, so a wrong guess is easy to spot. Finding none leaves the panel saying so.
    static class MessageLog
    {
        static bool searched, searchedInstance;
        static MemberInfo member;
        static bool isStatic;
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static List<object> Recent(int count)
        {
            object queue = R.Get(R.Get(R.SGet("XRL.The", "Game"), "Player"), "Messages");
            // search again once a queue exists, if the first look came before the game made one
            if (!searched || (member == null && queue != null && !searchedInstance)) Find(queue);
            if (member == null || (!isStatic && queue == null)) return null;
            IList log = Read(member, isStatic ? null : queue) as IList;
            if (log == null) return null;

            // newest last, as the game shows them; one entry can hold several lines
            var lines = new List<object>();
            for (int i = log.Count - 1; i >= 0 && lines.Count < count; i--)
            {
                string entry = log[i] as string;
                if (string.IsNullOrEmpty(entry)) continue;
                string[] parts = entry.Split('\n');
                for (int j = parts.Length - 1; j >= 0 && lines.Count < count; j--)
                    if (R.Strip(parts[j]).Trim().Length > 0) lines.Add(parts[j].TrimEnd('\r'));
            }
            lines.Reverse();
            return lines;
        }

        static void Find(object queue)
        {
            searched = true;
            searchedInstance = queue != null;
            Type t = queue != null ? queue.GetType() : R.FindType("XRL.Messages.MessageQueue");
            if (t == null) { Say("message log: no message queue found"); return; }
            int best = -1;
            foreach (MemberInfo m in t.GetMembers(Any))
            {
                FieldInfo f = m as FieldInfo;
                PropertyInfo p = m as PropertyInfo;
                if (f == null && (p == null || !p.CanRead || p.GetIndexParameters().Length > 0)) continue;
                bool st = f != null ? f.IsStatic : p.GetGetMethod(true).IsStatic;
                if (!st && queue == null) continue;
                IList list;
                try { list = Read(m, st ? null : queue) as IList; }
                catch { continue; }
                if (list == null || !OfStrings(list)) continue;
                int score = list.Count + (m.Name == "Messages" ? 1000000 : 0);
                if (score <= best) continue;
                best = score;
                member = m;
                isStatic = st;
            }
            Say(member == null ? "message log: nothing readable on " + t.FullName
                               : "message log: reading " + t.FullName + "." + member.Name);
        }

        static bool OfStrings(IList list)
        {
            Type lt = list.GetType();
            if (lt.IsArray) return lt.GetElementType() == typeof(string);
            if (lt.IsGenericType)
            {
                Type[] args = lt.GetGenericArguments();
                return args.Length == 1 && args[0] == typeof(string);
            }
            return list.Count > 0 && list[0] is string;
        }

        static object Read(MemberInfo m, object target)
        {
            FieldInfo f = m as FieldInfo;
            if (f != null) return f.GetValue(target);
            PropertyInfo p = m as PropertyInfo;
            return p == null ? null : p.GetValue(target, null);
        }

        static void Say(string line)
        {
            try { UnityEngine.Debug.Log("[QudHUD] " + line); } catch { }
        }
    }

    static class Difficulty
    {
        static Type evaluator;
        static bool searched;

        // Uses the game's own difficulty text (same wording and colors as the look screen).
        // Falls back to a level-difference estimate if that function is not found.
        public static string Describe(object target, GameObject player)
        {
            if (!searched)
            {
                searched = true;
                evaluator = R.FindTypeBySimpleName("DifficultyEvaluation",
                    "XRL.World.Capabilities.DifficultyEvaluation", "XRL.World.DifficultyEvaluation", "XRL.Rules.DifficultyEvaluation");
            }
            string s = R.Str(R.SCallT(evaluator, "GetDifficultyDescription", target, player));
            if (!string.IsNullOrEmpty(s) && R.Strip(s).Trim().Length > 0) return s;

            int diff = R.Int(R.Get(StatOf(target, "Level"), "Value"), 0) - R.Int(R.Get(StatOf(player, "Level"), "Value"), 0);
            if (diff >= 15) return "{{R|Impossible}}";
            if (diff >= 10) return "{{r|Very Tough}}";
            if (diff >= 5) return "{{W|Tough}}";
            if (diff > -5) return "{{w|Average}}";
            if (diff >= -10) return "{{g|Easy}}";
            return "{{G|Trivial}}";
        }

        static object StatOf(object o, string name)
        {
            IDictionary dict = R.Get(o, "Statistics") as IDictionary;
            return dict != null && dict.Contains(name) ? dict[name] : null;
        }
    }

    // Reflection helpers. Every lookup fails soft so a game update cannot break the whole HUD.
    static class R
    {
        static readonly char[] Markup = { '{', '}', '&', '^' };
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        // Each member is looked up once per type and name, and compiled to a delegate where it can be, so
        // after the first turn a Get or Call costs about what the direct call would. Plain reflection
        // built a string key and went through MethodInfo.Invoke on every call, which on the game's Mono
        // made a turn with a few dozen creatures in view cost milliseconds; see tests/bench.
        //
        // The caches are keyed by member name first, and each name remembers the type it was last used
        // with, since a loop asks the same thing of the same kind of object over and over. Names are
        // string literals at the call sites, so they hash on a few characters rather than with the
        // runtime's randomised string hash, which on Mono cost more than the rest of the lookup.
        internal sealed class Slot<T> where T : class
        {
            public Type Last;
            public T LastValue;
            public Dictionary<Type, T> Others;
        }

        sealed class NameComparer : IEqualityComparer<string>
        {
            public bool Equals(string a, string b) { return ReferenceEquals(a, b) || string.Equals(a, b); }
            public int GetHashCode(string s)
            {
                int n = s.Length;
                return n == 0 ? 0 : ((n * 31 + s[0]) * 31 + s[n >> 1]) * 31 + s[n - 1];
            }
        }

        // Also used for every dictionary the snapshot builds: its keys are all short literals too.
        public static readonly IEqualityComparer<string> Keys = new NameComparer();
        static readonly Dictionary<string, Member> members = new Dictionary<string, Member>(Keys);
        static readonly Dictionary<Type, Dictionary<string, MemberInfo>> statics = new Dictionary<Type, Dictionary<string, MemberInfo>>();
        static readonly object[] NoArgs = new object[0];

        // Everything cached under one name. Code that asks the same thing thousands of times a turn,
        // like the zone scan, holds one of these in a static field and skips even the name lookup.
        public sealed class Member
        {
            public readonly string Name;
            internal readonly Slot<Getter> Read = new Slot<Getter>();
            internal readonly Slot<List<Site>> Calls = new Slot<List<Site>>();
            internal readonly Slot<List<Site>> StaticCalls = new Slot<List<Site>>();
            internal Member(string name) { Name = name; }
        }

        public static Member M(string name)
        {
            Member m;
            if (!members.TryGetValue(name, out m)) members[name] = m = new Member(name);
            return m;
        }

        // What is cached for this type, or false when nothing is yet. A cached null counts.
        static bool Lookup<T>(Slot<T> slot, Type t, out T value) where T : class
        {
            if (ReferenceEquals(slot.Last, t)) { value = slot.LastValue; return true; }
            if (slot.Others != null && slot.Others.TryGetValue(t, out value))
            {
                slot.Last = t;
                slot.LastValue = value;
                return true;
            }
            value = null;
            return false;
        }

        static void Store<T>(Slot<T> slot, Type t, T value) where T : class
        {
            if (slot.Others == null) slot.Others = new Dictionary<Type, T>();
            slot.Others[t] = value;
            slot.Last = t;
            slot.LastValue = value;
        }

        public static Type FindType(string name)
        {
            Type t;
            if (types.TryGetValue(name, out t)) return t;
            t = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(name, false); } catch { }
                if (t != null) break;
            }
            types[name] = t;
            return t;
        }

        static Dictionary<string, T> Of<T>(Dictionary<Type, Dictionary<string, T>> cache, Type t)
        {
            Dictionary<string, T> byName;
            if (!cache.TryGetValue(t, out byName)) cache[t] = byName = new Dictionary<string, T>();
            return byName;
        }

        static MemberInfo FindMember(Type t, string name, bool isStatic)
        {
            MemberInfo m = null;
            BindingFlags f = (isStatic ? Stat : Inst) | BindingFlags.DeclaredOnly;
            for (Type c = t; c != null && m == null; c = c.BaseType)
            {
                FieldInfo fi = c.GetField(name, f);
                if (fi != null) { m = fi; break; }
                foreach (PropertyInfo pi in c.GetProperties(f))
                {
                    if (pi.Name == name && pi.CanRead && pi.GetIndexParameters().Length == 0) { m = pi; break; }
                }
            }
            return m;
        }

        static object Read(MemberInfo m, object target)
        {
            FieldInfo fi = m as FieldInfo;
            if (fi != null) return fi.GetValue(target);
            PropertyInfo pi = m as PropertyInfo;
            if (pi != null) return pi.GetValue(target, null);
            return null;
        }

        // A field or property read. Compiled where possible; if the runtime refuses the compiled
        // access, it falls back to reflection for good.
        internal sealed class Getter
        {
            readonly MemberInfo member;
            Func<object, object> compiled;

            public Getter(MemberInfo m)
            {
                member = m;
                try
                {
                    var o = Expression.Parameter(typeof(object), "o");
                    Expression access = Expression.MakeMemberAccess(Expression.Convert(o, m.DeclaringType), m);
                    compiled = Expression.Lambda<Func<object, object>>(Expression.Convert(access, typeof(object)), o).Compile();
                }
                catch { compiled = null; }
            }

            public object Read(object target)
            {
                if (compiled != null)
                {
                    try { return compiled(target); }
                    catch (MemberAccessException) { compiled = null; }
                }
                return R.Read(member, target);
            }
        }

        public static object Get(object o, string name) { return o == null ? null : Get(o, M(name)); }

        public static object Get(object o, Member member)
        {
            if (o == null) return null;
            try
            {
                Type t = o.GetType();
                Getter g;
                if (!Lookup(member.Read, t, out g))
                {
                    MemberInfo m = FindMember(t, member.Name, false);
                    g = m == null ? null : new Getter(m);
                    Store(member.Read, t, g);
                }
                return g == null ? null : g.Read(o);
            }
            catch { return null; }
        }

        public static object SGet(string typeName, string name)
        {
            try
            {
                Type t = FindType(typeName);
                if (t == null) return null;
                Dictionary<string, MemberInfo> byName = Of(statics, t);
                MemberInfo m;
                if (!byName.TryGetValue(name, out m)) byName[name] = m = FindMember(t, name, true);
                return m == null ? null : Read(m, null);
            }
            catch { return null; }
        }

        // One method resolved for one set of argument types. Calls of up to two arguments that fill
        // every parameter are compiled; anything else, such as optional parameters left to their
        // defaults, goes through reflection with the defaults worked out once.
        internal sealed class Site
        {
            public Type[] Args;       // runtime types it was resolved for, null standing for a null argument
            public MethodInfo Method; // null when nothing matched
            object[] defaults;
            Func<object, object> f0;
            Func<object, object, object> f1;
            Func<object, object, object, object> f2;

            public Site(Type[] args, MethodInfo mi, bool isStatic)
            {
                Args = args;
                Method = mi;
                if (mi == null) return;
                ParameterInfo[] ps = mi.GetParameters();
                defaults = new object[ps.Length];
                for (int i = args.Length; i < ps.Length; i++)
                    defaults[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : Type.Missing;
                if (ps.Length != args.Length || ps.Length > 2) return;
                try
                {
                    var target = Expression.Parameter(typeof(object), "o");
                    var pars = new List<ParameterExpression> { target };
                    var callArgs = new Expression[ps.Length];
                    for (int i = 0; i < ps.Length; i++)
                    {
                        if (ps[i].ParameterType.IsByRef) return;
                        var a = Expression.Parameter(typeof(object), "a" + i);
                        pars.Add(a);
                        callArgs[i] = Expression.Convert(a, ps[i].ParameterType);
                    }
                    Expression call = isStatic ? Expression.Call(mi, callArgs)
                                               : Expression.Call(Expression.Convert(target, mi.DeclaringType), mi, callArgs);
                    Expression body = mi.ReturnType == typeof(void)
                        ? (Expression)Expression.Block(call, Expression.Constant(null, typeof(object)))
                        : Expression.Convert(call, typeof(object));
                    if (ps.Length == 0) f0 = Expression.Lambda<Func<object, object>>(body, pars).Compile();
                    else if (ps.Length == 1) f1 = Expression.Lambda<Func<object, object, object>>(body, pars).Compile();
                    else f2 = Expression.Lambda<Func<object, object, object, object>>(body, pars).Compile();
                }
                catch { f0 = null; f1 = null; f2 = null; }
            }

            public bool Matches(int n, object a, object b, object[] rest)
            {
                if (Args.Length != n) return false;
                for (int i = 0; i < n; i++)
                {
                    object v = rest != null ? rest[i] : i == 0 ? a : b;
                    if (v == null ? Args[i] != null : v.GetType() != Args[i]) return false;
                }
                return true;
            }

            public object Invoke(object target, int n, object a, object b, object[] rest)
            {
                if (Method == null) return null;
                try
                {
                    if (f0 != null) return f0(target);
                    if (f1 != null) return f1(target, a);
                    if (f2 != null) return f2(target, a, b);
                }
                catch (MemberAccessException) { f0 = null; f1 = null; f2 = null; }
                object[] full = (object[])defaults.Clone();
                for (int i = 0; i < n; i++) full[i] = rest != null ? rest[i] : i == 0 ? a : b;
                return Method.Invoke(target, full);
            }
        }

        static Site Resolve(Member member, Type t, bool isStatic, int n, object a, object b, object[] rest)
        {
            Slot<List<Site>> slot = isStatic ? member.StaticCalls : member.Calls;
            List<Site> sites;
            if (!Lookup(slot, t, out sites)) Store(slot, t, sites = new List<Site>(1));
            for (int i = 0; i < sites.Count; i++)
                if (sites[i].Matches(n, a, b, rest)) return sites[i];

            object[] args = rest ?? (n == 0 ? NoArgs : n == 1 ? new[] { a } : new[] { a, b });
            var types = new Type[n];
            for (int i = 0; i < n; i++) types[i] = args[i] == null ? null : args[i].GetType();
            var site = new Site(types, FindMethod(t, member.Name, args, isStatic), isStatic);
            sites.Add(site);
            return site;
        }

        // The overload taking these arguments with the fewest optional parameters left over.
        static MethodInfo FindMethod(Type t, string name, object[] args, bool isStatic)
        {
            MethodInfo best = null;
            int bestExtra = int.MaxValue;
            foreach (MethodInfo mi in t.GetMethods((isStatic ? Stat : Inst) | BindingFlags.FlattenHierarchy))
            {
                if (mi.Name != name || mi.IsGenericMethodDefinition) continue;
                ParameterInfo[] ps = mi.GetParameters();
                if (ps.Length < args.Length) continue;
                bool ok = true;
                for (int i = 0; i < ps.Length && ok; i++)
                {
                    Type pt = ps[i].ParameterType;
                    if (i < args.Length)
                    {
                        if (pt.IsByRef) ok = false;
                        else if (args[i] == null) ok = !pt.IsValueType;
                        else ok = pt.IsInstanceOfType(args[i]);
                    }
                    else ok = ps[i].IsOptional && !pt.IsByRef;
                }
                if (ok && ps.Length - args.Length < bestExtra)
                {
                    best = mi;
                    bestExtra = ps.Length - args.Length;
                }
            }
            return best;
        }

        static object Invoke(object o, Member m, int n, object a, object b, object[] rest)
        {
            if (o == null) return null;
            try { return Resolve(m, o.GetType(), false, n, a, b, rest).Invoke(o, n, a, b, rest); }
            catch { return null; }
        }

        static object InvokeStatic(Type t, Member m, int n, object a, object b)
        {
            if (t == null) return null;
            try { return Resolve(m, t, true, n, a, b, null).Invoke(null, n, a, b, null); }
            catch { return null; }
        }

        // Fixed arities so the common calls allocate nothing; more arguments than two take the array.
        public static object Call(object o, Member m) { return Invoke(o, m, 0, null, null, null); }
        public static object Call(object o, Member m, object a) { return Invoke(o, m, 1, a, null, null); }
        public static object Call(object o, string name) { return o == null ? null : Invoke(o, M(name), 0, null, null, null); }
        public static object Call(object o, string name, object a) { return o == null ? null : Invoke(o, M(name), 1, a, null, null); }
        public static object Call(object o, string name, object a, object b) { return o == null ? null : Invoke(o, M(name), 2, a, b, null); }
        public static object Call(object o, string name, object a, object b, object c, params object[] more)
        {
            if (o == null) return null;
            var all = new object[3 + more.Length];
            all[0] = a; all[1] = b; all[2] = c; more.CopyTo(all, 3);
            return Invoke(o, M(name), all.Length, null, null, all);
        }

        public static object SCallT(Type t, string name) { return t == null ? null : InvokeStatic(t, M(name), 0, null, null); }
        public static object SCallT(Type t, string name, object a) { return t == null ? null : InvokeStatic(t, M(name), 1, a, null); }
        public static object SCallT(Type t, string name, object a, object b) { return t == null ? null : InvokeStatic(t, M(name), 2, a, b); }

        public static object SCall(string typeName, string name) { return SCallT(FindType(typeName), name); }
        public static object SCall(string typeName, string name, object a) { return SCallT(FindType(typeName), name, a); }
        public static object SCall(string typeName, string name, object a, object b) { return SCallT(FindType(typeName), name, a, b); }

        // Tries likely full names first, then scans loaded assemblies once.
        public static Type FindTypeBySimpleName(string simple, params string[] likely)
        {
            foreach (string full in likely)
            {
                Type t = FindType(full);
                if (t != null) return t;
            }
            string key = "?" + simple;
            Type found;
            if (types.TryGetValue(key, out found)) return found;
            found = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] all;
                try { all = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { all = ex.Types; }
                catch { continue; }
                foreach (Type t in all)
                {
                    if (t != null && t.Name == simple) { found = t; break; }
                }
                if (found != null) break;
            }
            types[key] = found;
            return found;
        }

        public static int Int(object o, int def)
        {
            if (o == null) return def;
            if (o is int) return (int)o;
            try { return Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { return def; }
        }

        public static bool Bool(object o)
        {
            return o is bool && (bool)o;
        }

        public static string Str(object o)
        {
            return o == null ? null : o.ToString();
        }

        // Removes Qud color markup: {{shader|text}}, &X, ^X.
        public static string Strip(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";
            // most text carries no markup at all, and needs no copy made
            if (s.IndexOfAny(Markup) < 0) return s;
            var sb = new StringBuilder(s.Length);
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                char n = i + 1 < s.Length ? s[i + 1] : '\0';
                if (c == '{' && n == '{')
                {
                    int bar = s.IndexOf('|', i + 2), close = s.IndexOf('}', i + 2);
                    if (bar > i && bar - i < 66 && (close < 0 || close > bar)) { i = bar; depth++; continue; }
                }
                if (c == '}' && n == '}' && depth > 0) { i++; depth--; continue; }
                if ((c == '&' || c == '^') && n != '\0')
                {
                    if (n == c) { sb.Append(c); i++; continue; }
                    if ("kKrRgGbBcCmMwWyYoO".IndexOf(n) >= 0) { i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }

    static class Json
    {
        public static string Write(object v)
        {
            var sb = new StringBuilder(4096);
            Val(sb, v);
            return sb.ToString();
        }

        static void Val(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            string s = v as string;
            if (s != null) { Str(sb, s); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int || v is long || v is short || v is byte)
            {
                sb.Append(Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (v is float || v is double)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            // the snapshot is built from these two concrete types, which test far faster than the interfaces
            Dictionary<string, object> dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, kv.Key);
                    sb.Append(':');
                    Val(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            List<object> items = v as List<object>;
            if (items != null)
            {
                sb.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Val(sb, items[i]);
                }
                sb.Append(']');
                return;
            }
            IDictionary<string, object> map = v as IDictionary<string, object>;
            if (map != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Str(sb, kv.Key);
                    sb.Append(':');
                    Val(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            IEnumerable list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (object o in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Val(sb, o);
                }
                sb.Append(']');
                return;
            }
            Str(sb, v.ToString());
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            // plain runs go in whole; only the characters that need escaping are handled one at a time
            int run = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0x20 && c != '"' && c != '\\' && c != '<' && c != '\u2028' && c != '\u2029') continue;
                if (i > run) sb.Append(s, run, i - run);
                run = i + 1;
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            if (s.Length > run) sb.Append(s, run, s.Length - run);
            sb.Append('"');
        }
    }
}
