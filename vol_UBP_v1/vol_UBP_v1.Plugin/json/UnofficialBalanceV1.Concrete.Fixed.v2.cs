// Unofficial Balance v1 — Concrete Patcher (Fixed v2, no System.Web, no tuples)
// - Avoids C# 7 value tuples (for older language settings)
// - Ensures balanced braces and valid modifiers
// - Same functionality as prior "Fixed" version

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
#pragma warning disable CS8618, CS8600, CS8601, CS8603, CS8604, CS8625

namespace Volcano.UnofficialBalanceV1
{
    [BepInPlugin("com.volcano1450.unofficialbalancev1", "Unofficial Balance v1", "1.0.2")]
    public class UnofficialBalanceV1 : BaseUnityPlugin
    {
        private Harmony _harmony;

        private void Awake()
        {
            _harmony = new Harmony("com.volcano1450.unofficialbalancev1");
            _harmony.PatchAll();
            Logger.LogInfo("[UnofficialBalanceV1] Awake complete.");
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { /* no-op */ }
        }
    }

    [HarmonyPatch]
    public static class DataFinalizeHook
    {
        private class Candidate
        {
            public string Type;
            public string Method;
            public Candidate(string t, string m) { Type = t; Method = m; }
        }

        static MethodBase TargetMethod()
        {
            var candidates = new List<Candidate> {
                new Candidate("GameDataBuilder", "FinalizeGameData"),
                new Candidate("ContentDatabase", "Build"),
                new Candidate("ContentDatabase", "Finalize")
            };

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var cand in candidates)
                {
                    try
                    {
                        var t = asm.GetType(cand.Type, false);
                        if (t == null) continue;
                        var m = AccessTools.Method(t, cand.Method, new Type[0]);
                        if (m != null) return m;
                    }
                    catch { /* keep searching */ }
                }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var t in asm.GetTypes())
                {
                    try
                    {
                        var m = t.GetMethod("FinalizeGameData", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (m != null) return m;
                    }
                    catch { }
                }
            }

            return AccessTools.Method(typeof(UnityEngine.Application), "get_isPlaying");
        }

        static void Postfix()
        {
            try
            {
                BalanceApplier.Apply();
            }
            catch (Exception e)
            {
                BepInEx.Logging.Logger.CreateLogSource("UnofficialBalanceV1").LogError("Apply error: " + e);
            }
        }
    }

    internal static class BalanceApplier
    {
        private static BepInEx.Logging.ManualLogSource Log
        {
            get { return BepInEx.Logging.Logger.CreateLogSource("UnofficialBalanceV1"); }
        }

        private class ChangeItem
        {
            public string cardName;
            public Statline currentStatline;
            public string currentEffect;
            public Statline newStatline;
            public string newEffect;
        }

        private class Statline
        {
            public int? space;
            public int? cost;
            public int? attack;
            public int? health;
        }

        public static void Apply()
        {
            string jsonPath = FindConfigPath();
            if (jsonPath == null)
            {
                Log.LogWarning("[Balance] balance_changes.json not found; skipping.");
                return;
            }

            var text = File.ReadAllText(jsonPath);
            var root = MiniJson.Deserialize(text) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("changes"))
            {
                Log.LogWarning("[Balance] changes array missing in JSON; skipping.");
                return;
            }

            var changes = ParseChanges(root["changes"]);
            if (changes.Count == 0)
            {
                Log.LogWarning("[Balance] No changes parsed; skipping.");
                return;
            }

            var cards = FindAllCards();
            Log.LogInfo("[Balance] Found " + cards.Count + " card-like objects.");

            foreach (var c in changes)
            {
                object match = null;
                foreach (var x in cards)
                {
                    var dn = GetStringProp(x, "DisplayName");
                    if (dn == c.cardName) { match = x; break; }
                }
                if (match == null)
                {
                    foreach (var x in cards)
                    {
                        var dn = GetStringProp(x, "DisplayName");
                        if (string.Equals(dn, c.cardName, StringComparison.OrdinalIgnoreCase)) { match = x; break; }
                    }
                }
                if (match == null)
                {
                    foreach (var x in cards)
                    {
                        var dn = GetStringProp(x, "DisplayName");
                        if (!string.IsNullOrEmpty(dn) && dn.IndexOf(c.cardName, StringComparison.OrdinalIgnoreCase) >= 0) { match = x; break; }
                    }
                }

                if (match == null)
                {
                    Log.LogWarning("[Balance] Could not find card '" + c.cardName + "'. Skipping.");
                    continue;
                }

                Log.LogInfo("[Balance] Patching '" + GetStringProp(match, "DisplayName") + "'");

                SetIntProp(match, "Cost", c.newStatline != null ? c.newStatline.cost : null);
                SetIntProp(match, "Attack", c.newStatline != null ? c.newStatline.attack : null);
                SetIntProp(match, "Health", c.newStatline != null ? c.newStatline.health : null);
                SetIntProp(match, "Space", c.newStatline != null ? c.newStatline.space : null);

                var newEff = c.newEffect ?? "";
                if (newEff.IndexOf("Sweep", StringComparison.OrdinalIgnoreCase) >= 0) AddKeyword(match, "Sweep");
                if (newEff.IndexOf("Explosive", StringComparison.OrdinalIgnoreCase) >= 0) AddKeyword(match, "Explosive");

                SetStringProp(match, "Description", c.newEffect);

                if (c.cardName == "Fanning the Flame")
                {
                    TrySetDamageTicks(match, 3, 2);
                    SetIntProp(match, "SlayDamageIncrease", 1);
                }
                else if (c.cardName == "Devilish Details")
                {
                    SetIntProp(match, "Cost", 0);
                }
                else if (c.cardName == "Soldier of Fortune")
                {
                    SetIntProp(match, "GoldThreshold", 50);
                    SetIntProp(match, "ArmorAmount", 10);
                    SetIntProp(match, "AvariceStacks", 5);
                    SetIntProp(match, "AttackBuff", 10);
                }
                else if (c.cardName == "Pyreblooded")
                {
                    AddKeyword(match, "Explosive");
                }
            }
        }

        private static string FindConfigPath()
        {
            // Try typical locations under BepInEx/plugins
            try
            {
                var root = Paths.BepInExRootPath;
                var guess1 = Path.Combine(root, "plugins", "UnofficialBalanceV1", "mod", "config", "balance_changes.json");
                if (File.Exists(guess1)) return guess1;
                var guess2 = Directory.GetFiles(root, "balance_changes.json", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(guess2)) return guess2;
            }
            catch { }

            // Fallback to current directory
            try
            {
                var guess3 = Directory.GetFiles(Directory.GetCurrentDirectory(), "balance_changes.json", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(guess3)) return guess3;
            }
            catch { }

            return null;
        }

        private static List<ChangeItem> ParseChanges(object raw)
        {
            var list = new List<ChangeItem>();
            var arr = raw as List<object>;
            if (arr == null) return list;

            foreach (var entry in arr)
            {
                try
                {
                    var d = entry as Dictionary<string, object>;
                    if (d == null) continue;
                    var ci = new ChangeItem();
                    ci.cardName = d.ContainsKey("cardName") ? d["cardName"] as string : null;
                    ci.currentEffect = d.ContainsKey("currentEffect") ? d["currentEffect"] as string : null;
                    ci.newEffect = d.ContainsKey("newEffect") ? d["newEffect"] as string : null;
                    ci.currentStatline = ParseStat(d, "currentStatline");
                    ci.newStatline = ParseStat(d, "newStatline");
                    if (!string.IsNullOrEmpty(ci.cardName)) list.Add(ci);
                }
                catch (Exception ex)
                {
                    Log.LogWarning("[Balance] Could not parse change item: " + ex);
                }
            }
            return list;
        }

        private static Statline ParseStat(Dictionary<string, object> d, string key)
        {
            if (!d.ContainsKey(key)) return null;
            var s = d[key] as Dictionary<string, object>;
            if (s == null) return null;
            var st = new Statline();
            st.space = ToNullableInt(s, "space");
            st.cost = ToNullableInt(s, "cost");
            st.attack = ToNullableInt(s, "attack");
            st.health = ToNullableInt(s, "health");
            return st;
        }

        private static int? ToNullableInt(Dictionary<string, object> d, string k)
        {
            if (!d.ContainsKey(k) || d[k] == null) return null;
            try { return Convert.ToInt32(d[k]); }
            catch { return null; }
        }

        private static List<object> FindAllCards()
        {
            var cards = new List<object>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.IsAbstract) continue;
                    var hasName = t.GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                    var hasCost = t.GetProperty("Cost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                    if (!hasName || !hasCost) continue;

                    var props = t.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    foreach (var prop in props)
                    {
                        try
                        {
                            if (!typeof(System.Collections.IEnumerable).IsAssignableFrom(prop.PropertyType)) continue;
                            var pname = prop.Name.ToLowerInvariant();
                            if (!pname.Contains("cards")) continue;
                            var val = prop.GetValue(null, null) as System.Collections.IEnumerable;
                            if (val == null) continue;
                            foreach (var obj in val)
                            {
                                if (obj != null && obj.GetType() == t) cards.Add(obj);
                            }
                        }
                        catch { }
                    }
                }
            }

            if (cards.Count == 0)
            {
                try
                {
                    var all = Resources.FindObjectsOfTypeAll<ScriptableObject>();
                    foreach (var so in all)
                    {
                        var t = so.GetType();
                        var hasName = t.GetProperty("DisplayName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                        var hasCost = t.GetProperty("Cost", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null;
                        if (hasName && hasCost) cards.Add(so);
                    }
                }
                catch { }
            }

            // De-dup
            var unique = new List<object>();
            var seen = new HashSet<int>();
            foreach (var o in cards)
            {
                int id = o.GetHashCode();
                if (seen.Contains(id)) continue;
                seen.Add(id);
                unique.Add(o);
            }
            return unique;
        }

        private static string GetStringProp(object o, string name)
        {
            var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return null;
            try { return p.GetValue(o, null) as string; } catch { return null; }
        }

        private static void SetStringProp(object o, string name, string value)
        {
            if (value == null) return;
            var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null || !p.CanWrite) return;
            try { p.SetValue(o, value); Log.LogInfo("  - " + name + " = \"" + value + "\""); } catch { }
        }

        private static void SetIntProp(object o, string name, int? value)
        {
            if (!value.HasValue) return;
            var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null || !p.CanWrite) return;
            try
            {
                var v = Convert.ChangeType(value.Value, p.PropertyType);
                p.SetValue(o, v);
                Log.LogInfo("  - " + name + " = " + value.Value);
            }
            catch { }
        }

        private static void AddKeyword(object o, string keyword)
        {
            try
            {
                var p = o.GetType().GetProperty("Keywords", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null) return;
                var list = p.GetValue(o, null) as System.Collections.IList;
                if (list == null) return;
                bool has = false;
                foreach (var item in list)
                {
                    if (item == null) continue;
                    var s = item.ToString();
                    if (!string.IsNullOrEmpty(s) && s.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { has = true; break; }
                }
                if (!has) { list.Add(keyword); Log.LogInfo("  - Added keyword '" + keyword + "'"); }
            }
            catch { }
        }

        private static void TrySetDamageTicks(object card, int baseDamage, int hits)
        {
            try
            {
                var effProp = card.GetType().GetProperty("Effects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (effProp == null) return;
                var effects = effProp.GetValue(card, null) as System.Collections.IList;
                if (effects == null) return;

                int setCount = 0;
                foreach (var eff in effects)
                {
                    var t = eff.GetType();
                    var pAmount = t.GetProperty("Damage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pAmount == null) pAmount = t.GetProperty("DamageAmount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (pAmount != null)
                    {
                        try
                        {
                            pAmount.SetValue(eff, Convert.ChangeType(baseDamage, pAmount.PropertyType), null);
                            setCount++;
                            if (setCount >= hits) break;
                        }
                        catch { }
                    }
                }

                Log.LogInfo("  - Damage ticks set to " + hits + " x " + baseDamage + " (matched " + setCount + " effects)");
            }
            catch { }
        }
    }

    // Minimal JSON parser
    internal static class MiniJson
    {
        public static object Deserialize(string json)
        {
            if (json == null) return null;
            var parser = new Parser(json);
            return parser.ParseValue();
        }

        private class Parser
        {
            private readonly string _json;
            private int _index;

            public Parser(string json)
            {
                _json = json;
                _index = 0;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (End) return null;
                char c = _json[_index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') return ParseLiteral("true", true);
                if (c == 'f') return ParseLiteral("false", false);
                if (c == 'n') return ParseLiteral("null", null);
                return ParseNumber();
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                Expect('{');
                SkipWhitespace();
                if (Peek('}')) { _index++; return dict; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    var val = ParseValue();
                    dict[key] = val;
                    SkipWhitespace();
                    if (Peek('}')) { _index++; break; }
                    Expect(',');
                }
                return dict;
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                Expect('[');
                SkipWhitespace();
                if (Peek(']')) { _index++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (Peek(']')) { _index++; break; }
                    Expect(',');
                }
                return list;
            }

            private string ParseString()
            {
                var sb = new StringBuilder();
                Expect('"');
                while (!End)
                {
                    char c = _json[_index++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        if (End) break;
                        char esc = _json[_index++];
                        if (esc == '"') sb.Append('"');
                        else if (esc == '\\') sb.Append('\\');
                        else if (esc == '/') sb.Append('/');
                        else if (esc == 'b') sb.Append('\b');
                        else if (esc == 'f') sb.Append('\f');
                        else if (esc == 'n') sb.Append('\n');
                        else if (esc == 'r') sb.Append('\r');
                        else if (esc == 't') sb.Append('\t');
                        else if (esc == 'u')
                        {
                            if (_index + 4 <= _json.Length)
                            {
                                string hex = _json.Substring(_index, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                _index += 4;
                            }
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = _index;
                if (Peek('-')) _index++;
                while (!End && char.IsDigit(_json[_index])) _index++;
                if (!End && _json[_index] == '.')
                {
                    _index++;
                    while (!End && char.IsDigit(_json[_index])) _index++;
                }
                if (!End && (_json[_index] == 'e' || _json[_index] == 'E'))
                {
                    _index++;
                    if (!End && (_json[_index] == '+' || _json[_index] == '-')) _index++;
                    while (!End && char.IsDigit(_json[_index])) _index++;
                }
                var s = _json.Substring(start, _index - start);
                double d;
                if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                    return d;
                return 0.0;
            }

            private object ParseLiteral(string literal, object value)
            {
                for (int i = 0; i < literal.Length; i++)
                {
                    if (End || _json[_index + i] != literal[i]) return null;
                }
                _index += literal.Length;
                return value;
            }

            private void SkipWhitespace()
            {
                while (!End)
                {
                    char c = _json[_index];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { _index++; continue; }
                    break;
                }
            }

            private void Expect(char c)
            {
                if (End || _json[_index] != c) throw new Exception("JSON parse error: expected '" + c + "' at pos " + _index);
                _index++;
            }

            private bool Peek(char c) { return (!End && _json[_index] == c); }
            private bool End { get { return _index >= _json.Length; } }
        }
    }
}