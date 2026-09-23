// Qud HUD __VERSION__: second monitor heads-up display for Caves of Qud.
// Each turn the player's state is written to Documents/QudHUD/hud_data.js.
// Open Documents/QudHUD/hud.html in a browser on your second monitor.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
            // During resting and auto-explore, cap writes to a few per second.
            if (!force && R.Bool(R.SCall("XRL.World.Capabilities.AutoAct", "IsActive")) && (now - lastWrite).TotalMilliseconds < 300) return;

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
                UnityEngine.Debug.Log("[QudHUD] Input hook patched " + patched + " method(s).");
            }
            catch (Exception ex) { Hud.Log("hook", ex); }
        }

        public static void BeforePlayerInput()
        {
            try { Hud.Update(The.Player, false); }
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

        public static Dictionary<string, object> Build(GameObject p)
        {
            var d = new Dictionary<string, object>();
            var alerts = new List<Dictionary<string, object>>();

            Section("player", () => BuildPlayer(p, d, alerts));
            Section("place", () => BuildPlace(p, d));
            Section("attributes", () => BuildAttributes(p, d));
            Section("combat", () => BuildCombat(p, d));
            Section("survival", () => BuildSurvival(p, d, alerts));
            Section("effects", () => BuildEffects(p, d, alerts));
            Section("abilities", () => BuildAbilities(p, d));
            Section("gear", () => BuildGear(p, d, alerts));
            Section("scanners", () => Scanners.Scan(p));
            Section("hostiles", () => BuildHostiles(p, d, alerts));

            alerts.Sort((a, b) => ((int)b["sev"]).CompareTo((int)a["sev"]));
            var list = new List<object>();
            foreach (var a in alerts) list.Add(a);
            d["alerts"] = list;
            return d;
        }

        static void Section(string name, Action a)
        {
            try { a(); } catch (Exception ex) { Hud.Log("section " + name, ex); }
        }

        static void Alert(List<Dictionary<string, object>> alerts, int sev, string text)
        {
            alerts.Add(new Dictionary<string, object> { { "sev", sev }, { "text", text } });
        }

        // Stats

        static object StatObj(object o, string name)
        {
            IDictionary dict = R.Get(o, "Statistics") as IDictionary;
            if (dict == null || !dict.Contains(name)) return null;
            return dict[name];
        }
        static int SV(object o, string name, int def = 0) { return R.Int(R.Get(StatObj(o, name), "Value"), def); }
        static int SB(object o, string name, int def = 0) { return R.Int(R.Get(StatObj(o, name), "BaseValue"), def); }
        static object SVOrNull(object o, string name)
        {
            object s = StatObj(o, name);
            return s == null ? null : (object)R.Int(R.Get(s, "Value"), 0);
        }

        static string Name(object o)
        {
            string n = R.Str(R.Get(o, "ShortDisplayName"));
            if (string.IsNullOrEmpty(n)) n = R.Str(R.Get(o, "DisplayName"));
            if (string.IsNullOrEmpty(n)) n = R.Str(R.Get(o, "Blueprint"));
            return n ?? "something";
        }

        static void BuildPlayer(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var pl = new Dictionary<string, object>();
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
            pl["hp"] = hp;
            pl["hpMax"] = max;
            d["player"] = pl;

            if (max > 0)
            {
                double pct = (double)hp / max;
                if (pct <= 0.25) Alert(alerts, 3, "Hit points critical: " + hp + " / " + max);
                else if (pct <= 0.5) Alert(alerts, 2, "Hit points low: " + hp + " / " + max);
            }
        }

        static void BuildPlace(GameObject p, Dictionary<string, object> d)
        {
            var zone = new Dictionary<string, object>();
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

            var clock = new Dictionary<string, object>();
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
                list.Add(new Dictionary<string, object> {
                    { "label", AttrLabels[i] }, { "value", v }, { "base", SB(p, AttrKeys[i]) },
                    { "mod", (int)Math.Floor((v - 16) / 2.0) }
                });
            }
            d["attributes"] = list;
        }

        static void BuildCombat(GameObject p, Dictionary<string, object> d)
        {
            var c = new Dictionary<string, object>();
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

            d["resist"] = new Dictionary<string, object> {
                { "heat", SVOrNull(p, "HeatResistance") }, { "cold", SVOrNull(p, "ColdResistance") },
                { "acid", SVOrNull(p, "AcidResistance") }, { "elec", SVOrNull(p, "ElectricResistance") }
            };
            d["points"] = new Dictionary<string, object> {
                { "ap", SV(p, "AP") }, { "sp", SV(p, "SP") }, { "mp", SV(p, "MP") }
            };
        }

        static void BuildSurvival(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            var s = new Dictionary<string, object>();

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
            if (ap > 0) Alert(alerts, 1, ap + " unspent attribute point" + (ap == 1 ? "" : "s"));
            if (mp > 0) Alert(alerts, 1, mp + " unspent mutation point" + (mp == 1 ? "" : "s"));
            if (sp >= 50) Alert(alerts, 1, sp + " unspent skill points");
        }

        static void EnsureEffectTypes()
        {
            if (typeNegative >= 0) return;
            typeNegative = R.Int(R.SGet("XRL.World.Effect", "TYPE_NEGATIVE"), 0);
            typeDisease = R.Int(R.SGet("XRL.World.Effect", "TYPE_DISEASE"), 0);
        }

        // Returns null for effects the game hides from the player.
        static Dictionary<string, object> DescribeEffect(object fx)
        {
            EnsureEffectTypes();
            string desc = R.Str(R.Call(fx, "GetDescription"));
            if (string.IsNullOrEmpty(desc) || R.Strip(desc).Trim().Length == 0) return null;
            string cls = fx.GetType().Name;
            int type = R.Int(R.Call(fx, "GetEffectType"), 0);
            bool disease = Diseases.Contains(cls) || (typeDisease > 0 && (type & typeDisease) != 0);
            bool negative = disease || Disabling.Contains(cls) || KnownBad.Contains(cls) || (typeNegative > 0 && (type & typeNegative) != 0);
            return new Dictionary<string, object> {
                { "name", desc },
                { "class", cls },
                { "duration", R.Int(R.Get(fx, "Duration"), 0) },
                { "details", R.Str(R.Call(fx, "GetDetails")) },
                { "negative", negative },
                { "disease", disease }
            };
        }

        static List<object> EffectsOf(object o)
        {
            var list = new List<object>();
            IEnumerable fxs = (R.Get(o, "Effects") ?? R.Get(o, "_Effects")) as IEnumerable;
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
            list.Sort((a, b) =>
            {
                var x = (Dictionary<string, object>)a; var y = (Dictionary<string, object>)b;
                int rx = (bool)x["disease"] ? 0 : (bool)x["negative"] ? 1 : 2;
                int ry = (bool)y["disease"] ? 0 : (bool)y["negative"] ? 1 : 2;
                if (rx != ry) return rx.CompareTo(ry);
                return string.Compare(R.Strip((string)x["name"]), R.Strip((string)y["name"]), StringComparison.OrdinalIgnoreCase);
            });
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
                    list.Add(new Dictionary<string, object> {
                        { "name", name },
                        { "enabled", enabledO == null || R.Bool(enabledO) },
                        { "cooldown", cd > 0 || R.Int(turnsO, 0) > 0 },
                        { "cooldownTurns", turnsO == null ? 0 : R.Int(turnsO, 0) },
                        { "toggleable", R.Bool(R.Get(entry, "Toggleable")) },
                        { "toggled", R.Bool(R.Get(entry, "ToggleState")) }
                    });
                }
            }
            list.Sort((a, b) => string.Compare(R.Strip((string)((Dictionary<string, object>)a)["name"]),
                R.Strip((string)((Dictionary<string, object>)b)["name"]), StringComparison.OrdinalIgnoreCase));
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
                    string iname = Name(item);

                    foreach (object fx in EffectsOf(item))
                    {
                        Dictionary<string, object> e = DescribeEffect(fx);
                        if (e == null || !(bool)e["negative"]) continue;
                        issues.Add(new Dictionary<string, object> { { "item", iname }, { "issue", e["name"] } });
                        Alert(alerts, 2, R.Strip(iname) + " is " + R.Strip((string)e["name"]));
                    }

                    if (R.Call(item, "GetPart", "FungalInfection") != null || R.Bool(R.Call(item, "HasTag", "FungalInfection")))
                    {
                        issues.Add(new Dictionary<string, object> { { "item", iname }, { "issue", "{{m|fungal infection}}" } });
                        Alert(alerts, 1, "Fungal infection: " + R.Strip(iname));
                    }

                    object socket = R.Call(item, "GetPart", "EnergyCellSocket");
                    if (socket != null)
                    {
                        object cell = R.Get(socket, "Cell");
                        if (cell == null)
                        {
                            cells.Add(new Dictionary<string, object> { { "item", iname }, { "empty", true } });
                            Alert(alerts, 2, R.Strip(iname) + " has no energy cell");
                        }
                        else
                        {
                            object ec = R.Call(cell, "GetPart", "EnergyCell");
                            int charge = R.Int(R.Get(ec, "Charge"), 0), max = R.Int(R.Get(ec, "MaxCharge"), 0);
                            if (max > 0)
                            {
                                cells.Add(new Dictionary<string, object> { { "item", iname }, { "charge", charge }, { "max", max } });
                                if (charge <= 0) Alert(alerts, 2, R.Strip(iname) + ": energy cell empty");
                                else if (charge < max * 0.15) Alert(alerts, 1, R.Strip(iname) + ": energy cell low (" + (int)Math.Round(100.0 * charge / max) + "%)");
                            }
                        }
                    }
                }
            }

            d["gear"] = new Dictionary<string, object> { { "missing", missing }, { "issues", issues }, { "cells", cells } };
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
            object cellVisible = R.Call(R.Get(o, "CurrentCell"), "IsVisible");
            if (cellVisible is bool) return (bool)cellVisible;
            return R.Bool(R.Call(o, "IsVisible"));
        }

        static readonly string[] Compass = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };

        // 8-way compass direction from p to o, or "here" for the same tile. Null if positions
        // aren't available. hud.html maps this to an arrow glyph.
        static string Direction(GameObject p, object o)
        {
            object pc = R.Get(p, "CurrentCell"), oc = R.Get(o, "CurrentCell");
            if (pc == null || oc == null) return null;
            int dx = R.Int(R.Get(oc, "X"), 0) - R.Int(R.Get(pc, "X"), 0);
            int dy = R.Int(R.Get(oc, "Y"), 0) - R.Int(R.Get(pc, "Y"), 0);
            if (dx == 0 && dy == 0) return "here";
            double deg = Math.Atan2(-dy, dx) * (180.0 / Math.PI);
            if (deg < 0) deg += 360;
            return Compass[(int)Math.Round(deg / 45.0) % 8];
        }

        static void BuildHostiles(GameObject p, Dictionary<string, object> d, List<Dictionary<string, object>> alerts)
        {
            object zone = R.Get(p, "CurrentZone");
            IEnumerable objs = R.Call(zone, "GetObjectsWithPart", "Brain") as IEnumerable;
            if (objs == null) { d["hostiles"] = null; return; }

            var found = new List<Dictionary<string, object>>();
            foreach (object o in objs)
            {
                if (o == null || ReferenceEquals(o, p)) continue;
                // A creature can linger in the zone's Brain list after death (seen with holograms);
                // treat 0-or-below Hitpoints as dead regardless of why it wasn't removed.
                object hpVal = SVOrNull(o, "Hitpoints");
                if (hpVal != null && (int)hpVal <= 0) continue;
                object hostile = R.Call(o, "IsHostileTowards", p);
                if (hostile == null) hostile = R.Call(R.Call(o, "GetPart", "Brain"), "IsHostileTowards", p);
                if (!R.Bool(hostile)) continue;
                if (!CurrentlyVisible(o)) continue;
                int hp = SV(o, "Hitpoints"), hpMax = SB(o, "Hitpoints");
                bool exact = Scanners.Sees(o);
                var entry = new Dictionary<string, object> {
                    { "name", Name(o) },
                    { "level", SV(o, "Level") },
                    { "rating", Rating(o, p) },
                    { "distance", R.Int(R.Call(p, "DistanceTo", o), 99) },
                    { "dir", Direction(p, o) },
                    { "exact", exact }
                };
                // Without a scanner the exact numbers are not sent at all, so the page cannot
                // leak them back through a proportional bar.
                if (exact) { entry["hp"] = hp; entry["hpMax"] = hpMax; }
                else entry["health"] = Health.Describe(hp, hpMax);
                found.Add(entry);
            }
            found.Sort((a, b) => ((int)a["distance"]).CompareTo((int)b["distance"]));

            var list = new List<object>();
            int adjacent = 0;
            for (int i = 0; i < found.Count; i++)
            {
                if ((int)found[i]["distance"] <= 1) adjacent++;
                if (i < 15) list.Add(found[i]);
            }
            d["hostiles"] = list;

            int dangerous = 0;
            foreach (var f in found)
            {
                string r = R.Strip((string)f["rating"]).Trim();
                if ((r == "Impossible" || r == "Very Tough") && dangerous++ < 3)
                    Alert(alerts, 2, R.Strip((string)f["name"]) + " in sight (" + r + ")");
            }

            if (adjacent > 0) Alert(alerts, 3, adjacent + " hostile" + (adjacent == 1 ? "" : "s") + " adjacent to you");
            else if (found.Count > 0) Alert(alerts, 2, found.Count + " hostile" + (found.Count == 1 ? "" : "s") + " in sight, nearest " + found[0]["distance"] + " away");
        }
    }

    // Vanilla shows a word, not a number, unless you carry something that reads exact stats off a
    // creature. These are the game's health states and their thresholds.
    static class Health
    {
        public static string Describe(int hp, int max)
        {
            if (max <= 0) return "";
            if (hp >= max) return "{{G|Perfect}}";
            int pct = (int)(100.0 * hp / max);
            if (pct >= 66) return "{{g|Fine}}";
            if (pct >= 33) return "{{W|Injured}}";
            if (pct >= 15) return "{{o|Wounded}}";
            return "{{R|Badly Wounded}}";
        }
    }

    // The optical bioscanner and its relatives are what reveal exact hit points. Their parts are
    // named *Indexer (BiologicalIndexer, TechnologicalIndexer, ...), so match on that rather than
    // on blueprint names: modded and unreleased scanners then work without being listed here.
    static class Scanners
    {
        static bool bio, techno, any;
        static string lastSig;

        public static void Scan(GameObject player)
        {
            bio = techno = any = false;
            foreach (object item in Worn(player))
            {
                foreach (string part in PartNames(item))
                {
                    if (part.IndexOf("Indexer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    any = true;
                    if (part.IndexOf("Bio", StringComparison.OrdinalIgnoreCase) >= 0) bio = true;
                    if (part.IndexOf("Techno", StringComparison.OrdinalIgnoreCase) >= 0) techno = true;
                }
            }
            string sig = bio + "/" + techno + "/" + any;
            if (sig == lastSig) return;
            lastSig = sig;
            try { UnityEngine.Debug.Log("[QudHUD] exact-stat scanners: bio=" + bio + " techno=" + techno + " any=" + any); }
            catch { }
        }

        // True when the player can read this creature's exact hit points.
        public static bool Sees(object target)
        {
            if (!any) return false;
            if (bio && techno) return true;
            if (!bio && !techno) return true;  // an indexer we don't recognise; assume it applies
            return IsRobot(target) ? techno : bio;
        }

        static bool IsRobot(object o)
        {
            if (R.Call(o, "GetPart", "Robot") != null) return true;
            return R.Bool(R.Call(o, "HasTag", "Robot"));
        }

        // Implants sit in body parts' Cybernetics slots, which GetEquippedObjects does not return.
        static List<object> Worn(GameObject player)
        {
            var list = new List<object>();
            object body = R.Call(player, "GetPart", "Body") ?? R.Get(player, "Body");
            if (body == null) return list;

            IEnumerable eq = R.Call(body, "GetEquippedObjects") as IEnumerable;
            if (eq != null) foreach (object o in eq) if (o != null) list.Add(o);

            IEnumerable parts = R.Call(body, "GetParts") as IEnumerable;
            if (parts != null)
                foreach (object bp in parts)
                {
                    object cyber = R.Get(bp, "Cybernetics");
                    if (cyber != null) list.Add(cyber);
                    object worn = R.Get(bp, "Equipped");
                    if (worn != null && !list.Contains(worn)) list.Add(worn);
                }
            return list;
        }

        static List<string> PartNames(object o)
        {
            var names = new List<string>();
            IEnumerable parts = R.Get(o, "PartsList") as IEnumerable;
            if (parts != null)
            {
                foreach (object p in parts) if (p != null) names.Add(p.GetType().Name);
                return names;
            }
            // No parts list on this build: fall back to asking for the ones we know by name.
            string[] known = { "BiologicalIndexer", "TechnologicalIndexer", "StructuralIndexer" };
            foreach (string k in known) if (R.Call(o, "GetPart", k) != null) names.Add(k);
            return names;
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
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags Stat = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static readonly Dictionary<string, MemberInfo> members = new Dictionary<string, MemberInfo>();
        static readonly Dictionary<string, MethodInfo> methods = new Dictionary<string, MethodInfo>();

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

        static MemberInfo FindMember(Type t, string name, bool isStatic)
        {
            string key = t.FullName + "|" + name + "|" + isStatic;
            MemberInfo m;
            if (members.TryGetValue(key, out m)) return m;
            m = null;
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
            members[key] = m;
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

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            try
            {
                MemberInfo m = FindMember(o.GetType(), name, false);
                return m == null ? null : Read(m, o);
            }
            catch { return null; }
        }

        public static object SGet(string typeName, string name)
        {
            try
            {
                Type t = FindType(typeName);
                if (t == null) return null;
                MemberInfo m = FindMember(t, name, true);
                return m == null ? null : Read(m, null);
            }
            catch { return null; }
        }

        static MethodInfo FindMethod(Type t, string name, object[] args, bool isStatic)
        {
            var kb = new StringBuilder(t.FullName).Append('|').Append(name).Append('|').Append(isStatic);
            foreach (object a in args) kb.Append('|').Append(a == null ? "null" : a.GetType().FullName);
            string key = kb.ToString();
            MethodInfo best;
            if (methods.TryGetValue(key, out best)) return best;

            best = null;
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
            methods[key] = best;
            return best;
        }

        static object Invoke(MethodInfo mi, object target, object[] args)
        {
            ParameterInfo[] ps = mi.GetParameters();
            object[] full = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
                full[i] = i < args.Length ? args[i] : (ps[i].HasDefaultValue ? ps[i].DefaultValue : Type.Missing);
            return mi.Invoke(target, full);
        }

        public static object Call(object o, string name, params object[] args)
        {
            if (o == null) return null;
            try
            {
                MethodInfo mi = FindMethod(o.GetType(), name, args, false);
                return mi == null ? null : Invoke(mi, o, args);
            }
            catch { return null; }
        }

        public static object SCall(string typeName, string name, params object[] args)
        {
            try
            {
                Type t = FindType(typeName);
                if (t == null) return null;
                MethodInfo mi = FindMethod(t, name, args, true);
                return mi == null ? null : Invoke(mi, null, args);
            }
            catch { return null; }
        }

        public static object SCallT(Type t, string name, params object[] args)
        {
            if (t == null) return null;
            try
            {
                MethodInfo mi = FindMethod(t, name, args, true);
                return mi == null ? null : Invoke(mi, null, args);
            }
            catch { return null; }
        }

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
            foreach (char c in s)
            {
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
            sb.Append('"');
        }
    }
}
