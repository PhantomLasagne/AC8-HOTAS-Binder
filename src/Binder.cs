// AC8 HOTAS Binder - point-and-press binding editor for the AC8HOTAS mod.
// Copyright (c) 2026 PhantomLasagne (Nexus Mods: Ph4ntomLasagne)
// SPDX-License-Identifier: MIT. See LICENSE.txt; keep this notice in copies and derivatives.
//
// Reads controller input directly through the Windows HID API (read-only; it never
// acquires devices or injects input) and edits the binding lines in AC8HOTAS's
// bindings.lua. Device GUIDs and supported inputs come from the catalog the mod
// writes to Scripts\devices.lua when the game starts or F5 is pressed.
//
// Written for the C# 5 compiler that ships with .NET Framework 4.x; see build.cmd.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("AC8 HOTAS Binder")]
[assembly: AssemblyDescription("Point-and-press binding editor for the AC8HOTAS mod")]
[assembly: AssemblyProduct("AC8 HOTAS Binder")]
[assembly: AssemblyCompany("PhantomLasagne")]
[assembly: AssemblyCopyright("Copyright (c) 2026 PhantomLasagne. MIT License.")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace AC8HotasBinder
{
    static class Program
    {
        public const string Version = "1.1.0";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--selftest")
                return SelfTest.Run(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);

            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "AC8 HOTAS Binder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            Application.Run(new MainForm());
            return 0;
        }
    }

    // ------------------------------------------------------------------------------------
    // Game actions
    // ------------------------------------------------------------------------------------

    class ActionDef
    {
        public string Name, Group, Hint, Prompt;
        public bool IsAxis, Optional;
        // Direct weapon selection; AC8HOTAS 0.3.2+ only (older versions reject the file).
        public bool DirectSelect;
        // The game's own defaults, for reference: keyboard & mouse, controller (Expert), and a tooltip note.
        public string Keys, Pad, DefaultsNote;

        public const string OptionalGroup = "OPTIONAL & UNTESTED";
        public static readonly List<ActionDef> All = Build();
        public static readonly Dictionary<string, ActionDef> ByName = All.ToDictionary(a => a.Name);

        static List<ActionDef> Build()
        {
            var l = new List<ActionDef>();
            string g = "WEAPONS & SYSTEMS";
            B(l, g, "Gun", "Fire gun");
            B(l, g, "Missile", "Fire missile");
            B(l, g, "Weapon", "Change weapon");
            W(l, g, "SelectMissiles", "Select standard missiles");
            W(l, g, "SelectSpecialWeapon1", "Select special weapon 1");
            W(l, g, "SelectSpecialWeapon2", "Select special weapon 2");
            B(l, g, "Target", "Switch target");
            B(l, g, "Radar", "Radar / map");
            B(l, g, "Flare", "Flares");
            B(l, g, "View", "Change view");
            B(l, g, "Gear", "Gear");
            B(l, g, "AccelDecel", "High-G turn (idle throttle also triggers it)");

            g = "FLIGHT AXES";
            A(l, g, "Pitch", "Nose up / down", "Push the stick FORWARD (nose down)");
            A(l, g, "Roll", "Bank left / right", "Push the stick RIGHT");
            A(l, g, "Yaw", "Rudder", "Push the RIGHT rudder pedal (or twist the stick right)");
            A(l, g, "Throttle", "Throttle lever", "Start at IDLE, then push the throttle to FULL");

            g = "LOOK / CAMERA";
            B(l, g, "HatUp", "Look up");
            B(l, g, "HatDown", "Look down");
            B(l, g, "HatLeft", "Look left");
            B(l, g, "HatRight", "Look right");
            A(l, g, "CameraPitch", "Camera up / down (axis)", "Push your camera axis UP");
            A(l, g, "CameraYaw", "Camera left / right (axis)", "Push your camera axis RIGHT");

            g = "MENUS";
            B(l, g, "FaceButtonBottom", "Confirm (A / Cross)");
            B(l, g, "FaceButtonRight", "Back (B / Circle)");
            B(l, g, "FaceButtonLeft", "X / Square");
            B(l, g, "FaceButtonTop", "Y / Triangle");
            B(l, g, "DPadUp", "Menu up");
            B(l, g, "DPadDown", "Menu down");
            B(l, g, "DPadLeft", "Menu left");
            B(l, g, "DPadRight", "Menu right");
            B(l, g, "Pause", "Pause / Start");
            B(l, g, "SpecialRight", "Hold to skip prompts (can share Pause)");

            g = OptionalGroup;
            B(l, g, "AutoPilot", "Autopilot");
            B(l, g, "Platform", "Platform / home button");
            B(l, g, "LeftShoulder", "LB / L1");
            B(l, g, "RightShoulder", "RB / R1");
            B(l, g, "LeftTrigger", "LT / L2 (as a button)");
            B(l, g, "RightTrigger", "RT / R2 (as a button)");
            B(l, g, "LeftThumbstick", "Left stick click");
            B(l, g, "RightThumbstick", "Right stick click");
            B(l, g, "SpecialLeft", "View / Share");
            B(l, g, "TouchPad", "Touchpad click");
            for (int i = 1; i <= 8; i++) B(l, g, "ExtraButton_0" + i, "Extra button " + i);
            B(l, g, "OnlineLobbyAvatarDash", "Online lobby: dash");
            B(l, g, "OnlineLobbyAvatarJump", "Online lobby: jump");
            foreach (var a in l) a.Optional = a.Group == OptionalGroup;
            Defaults(l);
            return l;
        }

        // From the game's KEY BINDINGS and BUTTON MAPPING screens. Controls those screens don't
        // list stay null; rows named after a pad button show that button.
        static void Defaults(List<ActionDef> l)
        {
            var by = l.ToDictionary(a => a.Name);
            Action<string, string, string, string> d = (n, keys, pad, note) => { var a = by[n]; a.Keys = keys; a.Pad = pad; a.DefaultsNote = note; };
            d("Gun", "LMB / J", "A / Cross", "In game: Fire machine gun.");
            d("Missile", "RMB / L", "B / Circle", "In game: Fire missile or weapon.");
            d("Weapon", "Wheel ↓ / K", "X / Square", "In game: Change weapon / Next weapon. Previous weapon is wheel ↑ / I; keys 1, 2 and 3 pick standard missiles or a special weapon directly.");
            d("SelectMissiles", "1", null, "In game: picks standard missiles directly. Needs AC8HOTAS 0.3.2 or newer.");
            d("SelectSpecialWeapon1", "2", null, "In game: picks special weapon 1 directly. Needs AC8HOTAS 0.3.2 or newer.");
            d("SelectSpecialWeapon2", "3", null, "In game: picks special weapon 2 directly. Needs AC8HOTAS 0.3.2 or newer.");
            d("Target", "T / MMB", "Y / Triangle", "In game: Change target. MMB is the mouse wheel click.");
            d("Radar", "M", "View / Share", "In game: Switch radar map.");
            d("Flare", "X", null, "In game: Deploy flares.");
            d("View", "V", null, "In game: Change view.");
            d("Gear", "R", null, "In game: Gear down / Gear up.");
            d("AccelDecel", "Space + L-Ctrl", "RT + LT / R2 + L2", "Accelerate and Decelerate pressed together.");
            d("Pitch", "W / S", null, "In game: W is Descend / Pitch down, S is Ascend / Pitch up.");
            d("Roll", "A / D", null, "In game: Turn left / Roll left, Turn right / Roll right.");
            d("Yaw", "Q / E", "LB / RB · L1 / R1", "In game: Yaw left / Yaw right.");
            d("Throttle", "Space / L-Ctrl", "RT / LT · R2 / L2", "In game: Accelerate / Decelerate.");
            d("HatUp", "7 / Num 8", null, "In game: Camera up.");
            d("HatDown", "8 / Num 2", null, "In game: Camera down.");
            d("HatLeft", "9 / Num 4", null, "In game: Camera left.");
            d("HatRight", "0 / Num 6", null, "In game: Camera right.");
            d("CameraPitch", "Mouse (F)", null, "In game: Camera control (mouse) is on F.");
            d("CameraYaw", "Mouse (F)", null, "In game: Camera control (mouse) is on F.");
            d("FaceButtonBottom", null, "A / Cross", null);
            d("FaceButtonRight", null, "B / Circle", null);
            d("FaceButtonLeft", null, "X / Square", null);
            d("FaceButtonTop", null, "Y / Triangle", null);
            d("DPadUp", "↑", "D-pad ↑", "The game lists ↑ as Order: Forward attack.");
            d("DPadDown", "↓", "D-pad ↓", "The game lists ↓ as Order: Cover.");
            d("DPadLeft", "←", "D-pad ←", "The game lists ← as Order: Disp. atk.");
            d("DPadRight", "→", "D-pad →", "The game lists → as Order: SP weapons on/off.");
            d("Pause", "Esc", "Menu / Options", null);
            d("SpecialRight", "Hold Esc", "Menu / Options", "Hold to skip cutscenes and prompts.");
            d("AutoPilot", "Z / C", null, "In game: Autopilot.");
            d("Platform", null, "Guide / PS", null);
            d("LeftShoulder", null, "LB / L1", null);
            d("RightShoulder", null, "RB / R1", null);
            d("LeftTrigger", null, "LT / L2", null);
            d("RightTrigger", null, "RT / R2", null);
            d("LeftThumbstick", null, "LS click / L3", null);
            d("RightThumbstick", null, "RS click / R3", null);
            d("SpecialLeft", null, "View / Share", null);
            d("TouchPad", null, "Touchpad", null);
        }

        static void B(List<ActionDef> l, string g, string n, string hint)
        {
            l.Add(new ActionDef { Name = n, Group = g, Hint = hint });
        }

        static void W(List<ActionDef> l, string g, string n, string hint)
        {
            l.Add(new ActionDef { Name = n, Group = g, Hint = hint, DirectSelect = true });
        }

        static void A(List<ActionDef> l, string g, string n, string hint, string prompt)
        {
            l.Add(new ActionDef { Name = n, Group = g, Hint = hint, Prompt = prompt, IsAxis = true });
        }

        // Every guided prompt asks for the direction that should read as positive, so a raw
        // axis that decreases during the prompt gets invert = true.
        public bool InvertFor(double delta) { return delta < 0; }
    }

    // ------------------------------------------------------------------------------------
    // bindings.lua model
    // ------------------------------------------------------------------------------------

    // One input. An action holds a List<Binding> in slot order; null means unbound (never an
    // empty list, which AC8HOTAS rejects).
    class Binding
    {
        public string Device, Input, Mode;
        public bool Invert;

        static readonly Regex PairRx = new Regex(@"([A-Za-z_]\w*)\s*=\s*(""((?:[^""\\]|\\.)*)""|'([^']*)'|true|false)");

        public static Binding Parse(string table)
        {
            string inner = table.Trim();
            if (!inner.StartsWith("{") || !inner.EndsWith("}")) return null;
            inner = inner.Substring(1, inner.Length - 2);
            var b = new Binding();
            foreach (Match m in PairRx.Matches(inner))
            {
                string key = m.Groups[1].Value, raw = m.Groups[2].Value;
                string s = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Success ? m.Groups[4].Value : null;
                if (key == "device" && s != null) b.Device = s;
                else if (key == "input" && s != null) b.Input = s;
                else if (key == "mode" && s != null) b.Mode = s;
                else if (key == "invert" && (raw == "true" || raw == "false")) b.Invert = raw == "true";
                else return null;
            }
            if (!Regex.IsMatch(PairRx.Replace(inner, ""), @"^[\s,]*$")) return null;
            if (b.Device == null || b.Input == null) return null;
            return b;
        }

        public string ToLua(bool isAxis)
        {
            var sb = new StringBuilder();
            sb.Append("{ device = \"").Append(Device).Append("\", input = \"").Append(Input).Append('"');
            if (isAxis) sb.Append(", invert = ").Append(Invert ? "true" : "false");
            if (isAxis && Mode != null) sb.Append(", mode = \"").Append(Mode).Append('"');
            sb.Append(" }");
            return sb.ToString();
        }

        public bool SameInput(Binding o)
        {
            return o != null && string.Equals(Device, o.Device, StringComparison.OrdinalIgnoreCase) && Input == o.Input;
        }

        public static bool Same(Binding a, Binding b)
        {
            if (a == null || b == null) return a == b;
            return a.SameInput(b) && a.Invert == b.Invert && a.Mode == b.Mode;
        }

        // Order matters: it is the order of the game's profile slots.
        public static bool SameList(List<Binding> a, List<Binding> b)
        {
            int na = a == null ? 0 : a.Count, nb = b == null ? 0 : b.Count;
            if (na != nb) return false;
            for (int i = 0; i < na; i++) if (!Same(a[i], b[i])) return false;
            return true;
        }
    }

    // Just enough Lua lexing to ignore braces and "--" inside quoted strings. Device GUIDs
    // contain braces, and so can the device names written as comments.
    static class Lua
    {
        // The line without its trailing comment.
        public static string Code(string line)
        {
            char q = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; }
                else if (c == '"' || c == '\'') q = c;
                else if (c == '-' && i + 1 < line.Length && line[i + 1] == '-') return line.Substring(0, i);
            }
            return line;
        }

        // Net count of { minus } outside quoted strings.
        public static int Braces(string code)
        {
            int n = 0; char q = '\0';
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; }
                else if (c == '"' || c == '\'') q = c;
                else if (c == '{') n++;
                else if (c == '}') n--;
            }
            return n;
        }

        // The top-level { ... } tables in code. other is true if anything but tables, commas
        // and whitespace sits at the top level. Returns null if the braces don't balance.
        public static List<string> Tables(string code, out bool other)
        {
            var list = new List<string>();
            int depth = 0, start = 0; char q = '\0';
            other = false;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                if (q != '\0') { if (c == '\\') i++; else if (c == q) q = '\0'; continue; }
                if (c == '"' || c == '\'') { q = c; if (depth == 0) other = true; }
                else if (c == '{') { if (depth++ == 0) start = i; }
                else if (c == '}')
                {
                    if (--depth < 0) return null;
                    if (depth == 0) list.Add(code.Substring(start, i - start + 1));
                }
                else if (depth == 0 && c != ',' && !char.IsWhiteSpace(c)) other = true;
            }
            return depth == 0 && q == '\0' ? list : null;
        }
    }

    class BindingsDoc
    {
        public const string Marker = "-- BEGIN DETECTED DEVICES";
        public readonly Dictionary<string, List<Binding>> Bindings = new Dictionary<string, List<Binding>>();
        public readonly Dictionary<string, string> Locked = new Dictionary<string, string>(); // action -> reason

        static readonly Regex AssignRx = new Regex(@"^\s*([A-Za-z_]\w*)\s*=");
        static readonly Regex OpenRx = new Regex(@"^\s*bindings\s*=\s*\{\s*(--.*)?$");
        static readonly Regex CloseRx = new Regex(@"^\s*\}\s*,?\s*(--.*)?$");

        static List<string> Lines(string text) { return new List<string>(text.Replace("\r\n", "\n").Split('\n')); }

        static int EditableEnd(List<string> lines)
        {
            for (int i = 0; i < lines.Count; i++) if (lines[i].TrimStart().StartsWith(Marker)) return i;
            return lines.Count;
        }

        // One "Action = value," entry, which may span lines (a list of inputs).
        class Entry { public string Name, Value, Problem; public int First, Last; }

        static List<Entry> Entries(List<string> lines, int end)
        {
            var list = new List<Entry>();
            for (int i = 0; i < end; i++)
            {
                var a = AssignRx.Match(lines[i]);
                if (!a.Success || !ActionDef.ByName.ContainsKey(a.Groups[1].Value)) continue;
                string code = Lua.Code(lines[i]).Substring(a.Length);
                var value = new StringBuilder(code);
                int depth = Lua.Braces(code), j = i;
                while (depth > 0 && j + 1 < end)
                {
                    code = Lua.Code(lines[++j]);
                    value.Append('\n').Append(code);
                    depth += Lua.Braces(code);
                }
                string v = value.ToString().Trim();
                if (v.EndsWith(",")) v = v.Substring(0, v.Length - 1).TrimEnd();
                list.Add(new Entry
                {
                    Name = a.Groups[1].Value, First = i, Last = j, Value = v,
                    Problem = depth > 0 ? "a { that is never closed" : depth < 0 ? "an extra }" : null
                });
                i = j;
            }
            return list;
        }

        // A single input table, or a list of them; null if it's anything else.
        static List<Binding> ParseValue(string value)
        {
            bool other;
            var whole = Lua.Tables(value, out other);
            if (whole == null || other || whole.Count != 1 || whole[0].Length != value.Length) return null;
            var items = Lua.Tables(value.Substring(1, value.Length - 2), out other);
            if (items == null) return null;
            if (items.Count == 0)
            {
                var single = Binding.Parse(value);
                return single == null ? null : new List<Binding> { single };
            }
            if (other) return null;
            var list = new List<Binding>();
            foreach (var t in items)
            {
                var b = Binding.Parse(t);
                if (b == null) return null;
                list.Add(b);
            }
            return list;
        }

        public static BindingsDoc Parse(string text)
        {
            var doc = new BindingsDoc();
            var lines = Lines(text);
            var seen = new HashSet<string>();
            foreach (var e in Entries(lines, EditableEnd(lines)))
            {
                if (!seen.Add(e.Name)) { doc.Locked[e.Name] = "defined twice in bindings.lua"; continue; }
                if (e.Problem != null) { doc.Locked[e.Name] = "has " + e.Problem; continue; }
                if (e.Value == "nil") { doc.Bindings[e.Name] = null; continue; }
                var list = ParseValue(e.Value);
                if (list == null) doc.Locked[e.Name] = "in a format this tool doesn't recognise";
                else doc.Bindings[e.Name] = list;
            }
            foreach (var n in doc.Locked.Keys) doc.Bindings.Remove(n);
            return doc;
        }

        // One input stays on one line, as AC8HOTAS 0.2.0 expects; two or more use the mod's
        // multi-line list format. Clearing writes nil, never an empty list.
        static List<string> Render(ActionDef def, List<Binding> list, string indent, Func<Binding, string> comment)
        {
            if (list == null || list.Count == 0) return new List<string> { indent + def.Name + " = nil," };
            if (list.Count == 1) return new List<string> { indent + def.Name + " = " + list[0].ToLua(def.IsAxis) + ", -- " + comment(list[0]) };
            var r = new List<string> { indent + def.Name + " = {" };
            foreach (var b in list) r.Add(indent + "    " + b.ToLua(def.IsAxis) + ", -- " + comment(b));
            r.Add(indent + "},");
            return r;
        }

        // Rewrites only the lines of the given actions; everything else is kept byte-for-byte.
        public static string Apply(string text, IDictionary<string, List<Binding>> changes, Func<Binding, string> comment)
        {
            string nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var lines = Lines(text);
            foreach (var kv in changes)
            {
                var def = ActionDef.ByName[kv.Key];
                int end = EditableEnd(lines);
                var e = Entries(lines, end).FirstOrDefault(x => x.Name == kv.Key);
                if (e != null)
                {
                    var block = Render(def, kv.Value, Regex.Match(lines[e.First], @"^\s*").Value, comment);
                    lines.RemoveRange(e.First, e.Last - e.First + 1);
                    lines.InsertRange(e.First, block);
                    continue;
                }

                int open = -1, close = -1;
                for (int i = 0; i < end; i++) if (OpenRx.IsMatch(lines[i])) { open = i; break; }
                if (open >= 0)
                {
                    int depth = 0;
                    for (int i = open + 1; i < end; i++)
                    {
                        string t = lines[i];
                        if (CloseRx.IsMatch(t) && depth == 0) { close = i; break; }
                        depth += Lua.Braces(Lua.Code(t));
                    }
                }
                if (close < 0) throw new InvalidOperationException("Couldn't find the bindings = { ... } table in bindings.lua.");
                lines.InsertRange(close, Render(def, kv.Value, "        ", comment));
            }
            return string.Join(nl, lines);
        }
    }

    // What the installed AC8HOTAS can read, checked by feature rather than version number.
    // 0.2.0 rejects the whole bindings.lua if it meets a list of inputs or a weapon-select binding.
    class ModFeatures
    {
        public bool Lists, WeaponSelect;

        public static ModFeatures Detect(string dir)
        {
            var f = new ModFeatures();
            if (dir == null) return f;
            try { f.Lists = File.ReadAllText(Path.Combine(dir, "Scripts", "mapper.lua")).Contains("Binding list must"); } catch { }
            try { f.WeaponSelect = File.Exists(Path.Combine(dir, "Scripts", "weapon_select.lua")); } catch { }
            return f;
        }
    }

    // ------------------------------------------------------------------------------------
    // Device catalog written by AC8HOTAS (Scripts\devices.lua)
    // ------------------------------------------------------------------------------------

    class CatalogDevice
    {
        public string Id, Product, Name;
        public int Axes, Buttons, Povs;
        public readonly Dictionary<string, bool> Inputs = new Dictionary<string, bool>(); // token -> supported

        static readonly Regex DevRx = new Regex(@"\{\s*id=""([^""]*)"",\s*product=""([^""]*)"",\s*name=""((?:[^""\\]|\\.)*)"",\s*axes=(\d+),\s*buttons=(\d+),\s*povs=(\d+)");
        static readonly Regex InRx = new Regex(@"\{token=""([^""]*)"",name=""(?:[^""\\]|\\.)*"",supported=(true|false)\}");

        public static List<CatalogDevice> Parse(string text)
        {
            var list = new List<CatalogDevice>();
            CatalogDevice cur = null;
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var d = DevRx.Match(line);
                if (d.Success)
                {
                    cur = new CatalogDevice
                    {
                        Id = d.Groups[1].Value, Product = d.Groups[2].Value.ToUpperInvariant().Replace("0X", "0x"),
                        Name = Regex.Unescape(d.Groups[3].Value).Trim(),
                        Axes = int.Parse(d.Groups[4].Value), Buttons = int.Parse(d.Groups[5].Value), Povs = int.Parse(d.Groups[6].Value)
                    };
                    list.Add(cur);
                    continue;
                }
                if (cur == null) continue;
                foreach (Match m in InRx.Matches(line)) cur.Inputs[m.Groups[1].Value] = m.Groups[2].Value == "true";
            }
            return list;
        }

        public bool Accepts(string token, bool axis, out string why)
        {
            why = null;
            bool supported;
            if (axis)
            {
                if (!Inputs.TryGetValue(token, out supported)) { why = Name + " doesn't report a " + token + " axis to the game."; return false; }
                if (!supported) { why = "AC8HOTAS can't use the " + token + " axis on " + Name + ". Try a different axis."; return false; }
                return true;
            }
            if (token.StartsWith("POV1_"))
            {
                if (!Inputs.ContainsKey("POV1")) { why = Name + " has no hat the game can see."; return false; }
                return true;
            }
            int n;
            if (token.StartsWith("Button") && int.TryParse(token.Substring(6), out n))
            {
                if (n > 128) { why = "AC8HOTAS supports buttons 1-128 only (that was button " + n + ")."; return false; }
                if (!Inputs.ContainsKey(token)) { why = "The game doesn't see button " + n + " on " + Name + "."; return false; }
                return true;
            }
            why = "Unrecognised input " + token + ".";
            return false;
        }
    }

    // ------------------------------------------------------------------------------------
    // Raw HID input (read-only)
    // ------------------------------------------------------------------------------------

    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }
        public const int HIDP_STATUS_SUCCESS = 0x00110000;
        public const int CAPS_SIZE = 72; // sizeof(HIDP_BUTTON_CAPS) == sizeof(HIDP_VALUE_CAPS)

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_GetProductString(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS c);
        [DllImport("hid.dll")] public static extern int HidP_GetButtonCaps(int type, byte[] caps, ref ushort len, IntPtr p);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int type, byte[] caps, ref ushort len, IntPtr p);
        [DllImport("hid.dll")] public static extern int HidP_GetUsages(int type, ushort page, ushort link, ushort[] usages, ref int len, IntPtr p, byte[] report, int reportLen);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr p, byte[] report, int reportLen);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] public static extern bool HideCaret(IntPtr hwnd);
    }

    class InputEvent
    {
        public HidDevice Device;
        public string Token; // Button12 or POV1_Up
    }

    class HidAxis
    {
        public string Token;
        public ushort Usage;
        public byte ReportId;
        public int Min, Max, BitSize;
    }

    class HidDevice
    {
        public string Name, Product, Path;
        public volatile bool Alive = true;
        public readonly List<HidAxis> Axes = new List<HidAxis>();
        public int ButtonCount;
        public bool HasHat { get { return hat != null; } }

        HidAxis hat;
        string hatDir = "";
        readonly Dictionary<ushort, int> buttonIndex = new Dictionary<ushort, int>();     // usage -> Button number
        readonly Dictionary<ushort, byte> buttonReport = new Dictionary<ushort, byte>();  // usage -> report ID
        readonly HashSet<byte> buttonReports = new HashSet<byte>();
        double[] values;
        readonly object sync = new object();
        SafeFileHandle handle;
        IntPtr preparsed;
        int reportLength;
        Thread thread;
        ConcurrentQueue<InputEvent> sink;

        public double[] Snapshot() { lock (sync) return (double[])values.Clone(); }

        public static List<HidDevice> Enumerate(ConcurrentQueue<InputEvent> sink)
        {
            var found = new List<HidDevice>();
            Guid hid; Native.HidD_GetHidGuid(out hid);
            IntPtr set = Native.SetupDiGetClassDevs(ref hid, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            var d = new Native.SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
            try
            {
                for (int i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hid, i, ref d); i++)
                {
                    int req; Native.SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    string path = null;
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                        if (Native.SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                            path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                    if (path == null) continue;
                    var dev = TryOpen(path, sink);
                    if (dev != null) found.Add(dev);
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return found;
        }

        static HidDevice TryOpen(string path, ConcurrentQueue<InputEvent> sink)
        {
            var h = Native.CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero); // GENERIC_READ, share R/W, OPEN_EXISTING
            if (h.IsInvalid) return null;
            IntPtr pp = IntPtr.Zero;
            try
            {
                var a = new Native.HIDD_ATTRIBUTES(); a.Size = Marshal.SizeOf(a);
                if (!Native.HidD_GetAttributes(h, ref a) || !Native.HidD_GetPreparsedData(h, out pp)) { h.Dispose(); return null; }
                Native.HIDP_CAPS caps; Native.HidP_GetCaps(pp, out caps);
                // Generic desktop joystick (4) or gamepad (5) collections only.
                if (caps.UsagePage != 1 || (caps.Usage != 4 && caps.Usage != 5)) { Native.HidD_FreePreparsedData(pp); h.Dispose(); return null; }

                var nb = new byte[256]; Native.HidD_GetProductString(h, nb, nb.Length);
                string name = Encoding.Unicode.GetString(nb).TrimEnd('\0').Trim();
                var dev = new HidDevice
                {
                    Path = path, handle = h, preparsed = pp, sink = sink, reportLength = caps.InputReportByteLength,
                    Name = name.Length > 0 ? name : string.Format("Controller {0:X4}:{1:X4}", a.VendorID, a.ProductID),
                    Product = string.Format("0x{0:X4}{1:X4}", a.ProductID, a.VendorID)
                };
                dev.ReadCaps(caps);
                return dev;
            }
            catch
            {
                if (pp != IntPtr.Zero) Native.HidD_FreePreparsedData(pp);
                h.Dispose();
                return null;
            }
        }

        void ReadCaps(Native.HIDP_CAPS caps)
        {
            // Buttons: DirectInput numbers them in usage order, starting at 1.
            var usages = new SortedSet<ushort>();
            ushort n = caps.NumberInputButtonCaps;
            if (n > 0)
            {
                var bc = new byte[Native.CAPS_SIZE * n];
                if (Native.HidP_GetButtonCaps(0, bc, ref n, preparsed) == Native.HIDP_STATUS_SUCCESS)
                    for (int k = 0; k < n; k++)
                    {
                        int o = k * Native.CAPS_SIZE;
                        if (BitConverter.ToUInt16(bc, o) != 9) continue;
                        byte rid = bc[o + 2];
                        bool range = bc[o + 12] != 0;
                        ushort lo = BitConverter.ToUInt16(bc, o + 56), hi = range ? BitConverter.ToUInt16(bc, o + 58) : lo;
                        for (int u = lo; u <= hi; u++) { usages.Add((ushort)u); buttonReport[(ushort)u] = rid; }
                        buttonReports.Add(rid);
                    }
            }
            int idx = 0;
            foreach (var u in usages) buttonIndex[u] = ++idx;
            ButtonCount = idx;

            // Axes and the first hat. Slider/Dial usages become Slider1, Slider2 in order.
            var vals = new List<HidAxis>();
            n = caps.NumberInputValueCaps;
            if (n > 0)
            {
                var vc = new byte[Native.CAPS_SIZE * n];
                if (Native.HidP_GetValueCaps(0, vc, ref n, preparsed) == Native.HIDP_STATUS_SUCCESS)
                    for (int k = 0; k < n; k++)
                    {
                        int o = k * Native.CAPS_SIZE;
                        if (BitConverter.ToUInt16(vc, o) != 1) continue;
                        bool range = vc[o + 12] != 0;
                        int bits = BitConverter.ToUInt16(vc, o + 18);
                        int lmin = BitConverter.ToInt32(vc, o + 40), lmax = BitConverter.ToInt32(vc, o + 44);
                        ushort lo = BitConverter.ToUInt16(vc, o + 56), hi = range ? BitConverter.ToUInt16(vc, o + 58) : lo;
                        for (int u = lo; u <= hi; u++)
                            vals.Add(new HidAxis { Usage = (ushort)u, ReportId = vc[o + 2], Min = lmin, Max = lmax, BitSize = bits });
                    }
            }
            int sliders = 0;
            foreach (var v in vals.OrderBy(x => x.Usage))
            {
                string t = null;
                switch (v.Usage)
                {
                    case 0x30: t = "X"; break;
                    case 0x31: t = "Y"; break;
                    case 0x32: t = "Z"; break;
                    case 0x33: t = "Rx"; break;
                    case 0x34: t = "Ry"; break;
                    case 0x35: t = "Rz"; break;
                    case 0x36:
                    case 0x37: if (sliders < 2) t = "Slider" + (++sliders); break;
                    case 0x39: if (hat == null) hat = v; break;
                }
                if (t != null && v.Max > v.Min && !Axes.Any(x => x.Token == t)) { v.Token = t; Axes.Add(v); }
            }
            values = Enumerable.Repeat(double.NaN, Axes.Count).ToArray();
        }

        public void Start()
        {
            thread = new Thread(ReadLoop) { IsBackground = true, Name = "HID " + Name };
            thread.Start();
        }

        public void Stop()
        {
            Alive = false;
            try { Native.CancelIoEx(handle, IntPtr.Zero); } catch { }
            handle.Dispose();
            if (thread != null && thread.Join(300) && preparsed != IntPtr.Zero)
            {
                Native.HidD_FreePreparsedData(preparsed);
                preparsed = IntPtr.Zero;
            }
        }

        int Value(HidAxis x, byte[] report, out bool ok)
        {
            uint raw;
            ok = Native.HidP_GetUsageValue(0, 1, 0, x.Usage, out raw, preparsed, report, reportLength) == Native.HIDP_STATUS_SUCCESS;
            long v = raw;
            if (x.Min < 0 && x.BitSize > 0 && x.BitSize < 32 && v >= (1L << (x.BitSize - 1))) v -= 1L << x.BitSize;
            return (int)v;
        }

        void ReadLoop()
        {
            var report = new byte[reportLength];
            var usages = new ushort[1024];
            var pressed = new HashSet<ushort>();
            string[] dirs = { "Up", "Up+Right", "Right", "Down+Right", "Down", "Down+Left", "Left", "Up+Left" };
            try
            {
                while (Alive)
                {
                    int got;
                    if (!Native.ReadFile(handle, report, reportLength, out got, IntPtr.Zero) || got <= 0) break;
                    byte rid = report[0];
                    bool ok;

                    if (buttonReports.Contains(rid))
                    {
                        int len = usages.Length;
                        if (Native.HidP_GetUsages(0, 9, 0, usages, ref len, preparsed, report, reportLength) == Native.HIDP_STATUS_SUCCESS)
                        {
                            var now = new HashSet<ushort>();
                            for (int i = 0; i < len; i++) now.Add(usages[i]);
                            foreach (var u in now)
                            {
                                int b;
                                if (!pressed.Contains(u) && buttonIndex.TryGetValue(u, out b))
                                    sink.Enqueue(new InputEvent { Device = this, Token = "Button" + b });
                            }
                            pressed.RemoveWhere(u => { byte r; return buttonReport.TryGetValue(u, out r) && r == rid; });
                            pressed.UnionWith(now);
                        }
                    }

                    if (hat != null && hat.ReportId == rid)
                    {
                        int v = Value(hat, report, out ok);
                        if (ok)
                        {
                            int span = hat.Max - hat.Min, p = v - hat.Min;
                            string dir = "";
                            if (p >= 0 && p <= span && (span == 7 || span == 3)) dir = dirs[span == 3 ? p * 2 : p];
                            if (dir != hatDir)
                            {
                                hatDir = dir;
                                if (dir == "Up" || dir == "Down" || dir == "Left" || dir == "Right")
                                    sink.Enqueue(new InputEvent { Device = this, Token = "POV1_" + dir });
                            }
                        }
                    }

                    lock (sync)
                        for (int i = 0; i < Axes.Count; i++)
                        {
                            var x = Axes[i];
                            if (x.ReportId != rid) continue;
                            int v = Value(x, report, out ok);
                            if (ok) values[i] = Math.Max(0.0, Math.Min(1.0, (v - x.Min) / (double)(x.Max - x.Min)));
                        }
                }
            }
            catch { }
            Alive = false;
        }
    }

    class HidManager
    {
        public readonly List<HidDevice> Devices = new List<HidDevice>();
        public readonly ConcurrentQueue<InputEvent> Events = new ConcurrentQueue<InputEvent>();

        public void Rescan()
        {
            Stop();
            Devices.AddRange(HidDevice.Enumerate(Events));
            foreach (var d in Devices) d.Start();
        }

        public void Stop()
        {
            foreach (var d in Devices) d.Stop();
            Devices.Clear();
            InputEvent e;
            while (Events.TryDequeue(out e)) { }
        }
    }

    // ------------------------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------------------------

    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(24, 26, 31);
        public static readonly Color Panel = Color.FromArgb(32, 35, 42);
        public static readonly Color Row = Color.FromArgb(27, 29, 35);
        public static readonly Color RowHover = Color.FromArgb(38, 42, 51);
        public static readonly Color Line = Color.FromArgb(44, 48, 58);
        public static readonly Color Edge = Color.FromArgb(84, 91, 106);     // visible outlines / resize handles
        public static readonly Color Text = Color.FromArgb(230, 232, 236);
        public static readonly Color Dim = Color.FromArgb(140, 148, 160);
        public static readonly Color Accent = Color.FromArgb(242, 169, 59);   // HUD amber
        public static readonly Color AccentDark = Color.FromArgb(70, 52, 22);
        public static readonly Color Good = Color.FromArgb(92, 200, 140);
        public static readonly Color GoodDark = Color.FromArgb(26, 58, 42);
        public static readonly Color Warn = Color.FromArgb(240, 200, 100);
        public static readonly Color Bad = Color.FromArgb(240, 110, 110);
        public static readonly Color BadDark = Color.FromArgb(66, 30, 32);
        public static readonly Color Blue = Color.FromArgb(96, 165, 250);
    }

    enum BtnStyle { Filled, OutlineAccent, Outline, OutlineDanger }

    class FlatBtn : Button
    {
        static readonly Font Regular = new Font("Segoe UI", 9.5f);
        static readonly Font Bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);

        public FlatBtn(string text, BtnStyle style)
        {
            Text = text;
            FlatStyle = FlatStyle.Flat;
            AutoSize = true;
            Padding = new Padding(10, 3, 10, 3);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 1;
            Apply(style);
        }

        public void Apply(BtnStyle style)
        {
            switch (style)
            {
                case BtnStyle.Filled:
                    Set(Theme.Accent, Color.FromArgb(25, 20, 10), Theme.Accent, Color.FromArgb(255, 190, 90), Color.FromArgb(220, 150, 40), Bold);
                    break;
                case BtnStyle.OutlineAccent:
                    Set(Theme.Panel, Theme.Accent, Theme.Accent, Theme.AccentDark, Theme.Line, Bold);
                    break;
                case BtnStyle.OutlineDanger:
                    Set(Theme.Panel, Theme.Bad, Theme.Bad, Theme.BadDark, Theme.Line, Regular);
                    break;
                default:
                    Set(Theme.Panel, Theme.Text, Theme.Edge, Theme.RowHover, Theme.Line, Regular);
                    break;
            }
        }

        void Set(Color back, Color fore, Color border, Color hover, Color down, Font font)
        {
            BackColor = back;
            ForeColor = fore;
            FlatAppearance.BorderColor = border;
            FlatAppearance.MouseOverBackColor = hover;
            FlatAppearance.MouseDownBackColor = down;
            Font = font;
        }

        protected override bool ShowFocusCues { get { return false; } }
    }

    class MainForm : Form
    {
        const int ColAction = 0, ColHint = 1, ColKeys = 2, ColPad = 3, ColBinding = 4, ColDirection = 5, ColShared = 6, ColAdd = 7, ColClear = 8;
        const double AxisThreshold = 0.35;
        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        string modDir, bindingsPath, devicesPath;
        List<CatalogDevice> catalog = new List<CatalogDevice>();
        readonly Dictionary<string, CatalogDevice> catalogById = new Dictionary<string, CatalogDevice>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, CatalogDevice> catalogByProduct = new Dictionary<string, CatalogDevice>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> duplicateProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BindingsDoc disk = new BindingsDoc();
        readonly Dictionary<string, List<Binding>> current = new Dictionary<string, List<Binding>>(); // null = unbound
        ModFeatures features = new ModFeatures();
        DateTime bindingsStamp, devicesStamp;

        readonly HidManager hid = new HidManager();
        ActionDef listening;
        int listenSlot;
        readonly Dictionary<HidDevice, double[]> baseline = new Dictionary<HidDevice, double[]>();
        readonly HashSet<string> ignoredAxes = new HashSet<string>();

        DataGridView grid;
        readonly Dictionary<string, DataGridViewRow> rows = new Dictionary<string, DataGridViewRow>();
        readonly Dictionary<string, List<DataGridViewRow>> extraRows = new Dictionary<string, List<DataGridViewRow>>();
        readonly List<DataGridViewRow> optionalRows = new List<DataGridViewRow>();
        DataGridViewRow optionalHeader;
        bool optionalOpen;
        DataGridViewRow flashRow;
        DateTime flashUntil;

        Panel bannerPanel;
        Label bannerText, statusLabel, pathLabel;
        FlowLayoutPanel deviceStrip;
        FlatBtn saveButton, revertButton;
        System.Windows.Forms.Timer pollTimer, fileTimer;
        readonly ToolTip tips = new ToolTip();
        Font bold, small, groupFont;
        float scale = 1f;

        int S(int px) { return (int)Math.Round(px * scale); }
        Padding P(int l, int t, int r, int b) { return new Padding(S(l), S(t), S(r), S(b)); }

        // Keep the window inside the usable desktop area (above the taskbar) on small or scaled screens.
        public static void FitToScreen(Form f, bool center)
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(f.Width, wa.Width - 16), h = Math.Min(f.Height, wa.Height - 16);
            f.MinimumSize = new Size(Math.Min(f.MinimumSize.Width, w), Math.Min(f.MinimumSize.Height, h));
            f.Size = new Size(w, h);
            if (!center) return;
            // Centre explicitly within the working area; Windows' own placement can overlap the taskbar.
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2);
        }

        public MainForm()
        {
            Text = "AC8 HOTAS Binder";
            Font = new Font("Segoe UI", 9.5f);
            bold = new Font(Font, FontStyle.Bold);
            small = new Font("Segoe UI", 8.5f);
            groupFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            // Fonts scale with DPI on their own; pixel sizes go through S().
            AutoScaleMode = AutoScaleMode.None;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            ClientSize = new Size(S(1200), S(800));
            MinimumSize = new Size(S(860), S(560));
            FitToScreen(this, true);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();

            pollTimer = new System.Windows.Forms.Timer { Interval = 15 };
            pollTimer.Tick += (s, e) => Poll();
            fileTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            fileTimer.Tick += (s, e) => CheckFiles();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { } // dark title bar
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                // Dark scrollbars on Windows 10 1809+ / 11; ignored elsewhere.
                foreach (Control c in grid.Controls)
                    if (c is ScrollBar) Native.SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            }
            catch { }
            string dir = FindModFolder();
            if (dir == null) dir = AskForFolder();
            if (dir == null) { Close(); return; }
            OpenFolder(dir);
            pollTimer.Start();
            fileTimer.Start();
        }

        // ---------------------------------------------------------------- layout

        void BuildUi()
        {
            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = S(92), BackColor = Theme.Panel };
            var title = new Label { Text = "AC8 HOTAS Binder", AutoSize = true, Font = new Font("Segoe UI Semibold", 16f), ForeColor = Theme.Text, Location = new Point(S(16), S(10)) };
            var sub = new Label
            {
                Text = "With the game closed: click an action, press the control on your stick, throttle or pedals, then Save. Launch the game to test.",
                AutoSize = true, ForeColor = Theme.Dim, Location = new Point(S(19), S(46))
            };
            pathLabel = new Label { AutoSize = true, ForeColor = Theme.Dim, Font = small, Location = new Point(S(19), S(68)) };
            var version = new Label { Text = "v" + Program.Version, AutoSize = true, ForeColor = Theme.Dim, Font = small, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            var help = Link("How it works", (s, e) => ShowHelp());
            var change = Link("Change folder…", (s, e) => ChangeFolder());
            help.Anchor = change.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.AddRange(new Control[] { title, sub, pathLabel, version, help, change });
            header.Resize += (s, e) =>
            {
                version.Location = new Point(header.Width - version.Width - S(18), S(16));
                help.Location = new Point(header.Width - help.Width - S(18), S(46));
                change.Location = new Point(header.Width - change.Width - S(18), S(68));
            };

            // Device strip
            var devicesBar = new Panel { Dock = DockStyle.Top, Height = S(44), BackColor = Theme.Bg, Padding = P(16, 8, 16, 6) };
            var rescan = new FlatBtn("Rescan devices", BtnStyle.Outline) { Dock = DockStyle.Right };
            rescan.Click += (s, e) => Rescan(true);
            tips.SetToolTip(rescan, "Re-detect controllers and reload the game's device list (Scripts\\devices.lua).");
            deviceStrip = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, BackColor = Theme.Bg };
            devicesBar.Controls.Add(deviceStrip);
            devicesBar.Controls.Add(rescan);

            // Grid
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, BackgroundColor = Theme.Bg, BorderStyle = BorderStyle.None, GridColor = Theme.Line,
                RowHeadersVisible = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                AllowUserToResizeColumns = false, // replaced by the always-visible live handles below
                AllowUserToOrderColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                ReadOnly = true, EditMode = DataGridViewEditMode.EditProgrammatically, EnableHeadersVisualStyles = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = S(30), ShowCellToolTips = true, StandardTab = true
            };
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(grid, true, null);
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Bg, ForeColor = Theme.Dim, SelectionBackColor = Theme.Bg, SelectionForeColor = Theme.Dim,
                Font = groupFont, Padding = P(8, 0, 8, 0)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Theme.Row, ForeColor = Theme.Text, SelectionBackColor = Theme.RowHover, SelectionForeColor = Theme.Text,
                Padding = P(8, 0, 8, 0), Font = Font
            };
            grid.RowTemplate.Height = S(30);
            AddColumn("ACTION", 180, false);
            AddColumn("WHAT IT DOES", 180, false);
            AddColumn("KB / MOUSE", 110, false);
            AddColumn("CONTROLLER", 125, false);
            AddColumn("BINDING", 0, true);
            AddColumn("DIRECTION", 92, false);
            AddColumn("ALSO USED BY", 110, false);
            AddColumn("", 64, false);
            AddColumn("", 64, false);
            grid.Columns[ColKeys].ToolTipText = "The game's default keyboard & mouse controls, for reference.";
            grid.Columns[ColPad].ToolTipText = "The game's default controller buttons (Expert controls), for reference.";
            // Handled after the event returns: a click can add or remove rows (extra inputs),
            // which DataGridView doesn't allow from inside its own cell events.
            grid.CellClick += (s, e) => BeginInvoke((Action)(() => GridCellClick(s, e)));
            grid.CellMouseEnter += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var r = grid.Rows[e.RowIndex];
                grid.Cursor = (r.Tag is RowRef || r == optionalHeader) ? Cursors.Hand : Cursors.Default;
            };
            grid.CellMouseLeave += (s, e) => { if (dragCol < 0) grid.Cursor = Cursors.Default; };
            grid.CellPainting += PaintHeader;
            grid.RowPostPaint += PaintGroupRow;
            grid.MouseDown += GridMouseDown;
            grid.MouseMove += GridMouseMove;
            grid.MouseUp += (s, e) => EndColumnDrag();
            grid.MouseCaptureChanged += (s, e) => { if (!grid.Capture) EndColumnDrag(); };
            grid.MouseLeave += (s, e) => { if (dragCol < 0) SetHoverEdge(-1); };

            // Banner (listening / messages)
            bannerPanel = new Panel { Dock = DockStyle.Bottom, Height = S(58), Padding = P(20, 0, 20, 0), BackColor = Theme.Panel };
            bannerText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 11f), AutoEllipsis = true };
            bannerPanel.Controls.Add(bannerText);

            // Footer
            var footer = new Panel { Dock = DockStyle.Bottom, Height = S(54), BackColor = Theme.Bg, Padding = P(16, 10, 16, 10) };
            saveButton = new FlatBtn("Save bindings", BtnStyle.OutlineAccent) { Dock = DockStyle.Right };
            saveButton.Click += (s, e) => Save();
            revertButton = new FlatBtn("Discard changes", BtnStyle.Outline) { Dock = DockStyle.Right };
            revertButton.Click += (s, e) => Revert();
            statusLabel = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim };
            footer.Controls.Add(statusLabel);
            footer.Controls.Add(revertButton);
            footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = S(8), BackColor = Theme.Bg });
            footer.Controls.Add(saveButton); // docked first, so it ends up rightmost
            tips.SetToolTip(saveButton, "Write bindings.lua (Ctrl+S). A backup is saved as bindings.lua.binder.bak.");
            tips.SetToolTip(revertButton, "Throw away unsaved changes and go back to what's in bindings.lua.");

            Controls.Add(grid);
            Controls.Add(devicesBar);
            Controls.Add(header);
            Controls.Add(bannerPanel);
            Controls.Add(footer);

            Banner("Loading…", BannerKind.Info);
        }

        LinkLabel Link(string text, LinkLabelLinkClickedEventHandler click)
        {
            var l = new LinkLabel
            {
                Text = text, AutoSize = true, LinkColor = Theme.Blue, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Blue,
                LinkBehavior = LinkBehavior.HoverUnderline, Font = small
            };
            l.LinkClicked += click;
            return l;
        }

        void AddColumn(string header, int width, bool fill)
        {
            var c = new DataGridViewTextBoxColumn { HeaderText = header, SortMode = DataGridViewColumnSortMode.NotSortable };
            if (fill) { c.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; c.MinimumWidth = S(260); }
            else c.Width = S(width);
            grid.Columns.Add(c);
        }

        // ---------------------------------------------------------------- column resize handles
        // DataGridView's own resizing hides its handles and only applies on mouse-up, so the
        // header draws a permanent grip at each column edge and dragging resizes live.

        int dragCol = -1, dragStartX, dragStartWidth, dragNeighbourWidth, hoverEdge = -1;

        int EdgeAt(Point p)
        {
            if (p.Y < 0 || p.Y >= grid.ColumnHeadersHeight) return -1;
            for (int i = 0; i < grid.Columns.Count - 1; i++)
            {
                var r = grid.GetColumnDisplayRectangle(i, false);
                if (r.Width > 0 && Math.Abs(p.X - r.Right) <= S(5)) return i;
            }
            return -1;
        }

        void SetHoverEdge(int i)
        {
            if (hoverEdge == i) return;
            hoverEdge = i;
            grid.Invalidate(new Rectangle(0, 0, grid.Width, grid.ColumnHeadersHeight));
        }

        void GridMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = EdgeAt(e.Location);
            if (i < 0) return;
            dragCol = i;
            dragStartX = e.X;
            dragStartWidth = grid.Columns[i].Width;
            dragNeighbourWidth = grid.Columns[i + 1].Width;
            grid.Capture = true;
        }

        void GridMouseMove(object sender, MouseEventArgs e)
        {
            if (dragCol >= 0)
            {
                int delta = e.X - dragStartX, min = S(48);
                var col = grid.Columns[dragCol];
                if (col.AutoSizeMode == DataGridViewAutoSizeColumnMode.Fill)
                    // The fill column has no fixed width; move its edge by resizing the neighbour.
                    grid.Columns[dragCol + 1].Width = Math.Max(min, dragNeighbourWidth - delta);
                else
                    col.Width = Math.Max(min, dragStartWidth + delta);
                return;
            }
            int edge = EdgeAt(e.Location);
            SetHoverEdge(edge);
            if (e.Y < grid.ColumnHeadersHeight) grid.Cursor = edge >= 0 ? Cursors.VSplit : Cursors.Default;
        }

        void EndColumnDrag()
        {
            if (dragCol < 0) return;
            dragCol = -1;
            grid.Capture = false;
            SetHoverEdge(EdgeAt(grid.PointToClient(Cursor.Position)));
        }

        void PaintHeader(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && !(grid.Rows[e.RowIndex].Tag is RowRef))
            {
                // Group rows: background only; PaintGroupRow draws the label across the row.
                e.PaintBackground(e.CellBounds, false);
                e.Handled = true;
                return;
            }
            if (e.RowIndex != -1 || e.ColumnIndex < 0) return;
            e.PaintBackground(e.CellBounds, false);
            e.PaintContent(e.CellBounds);
            using (var line = new Pen(Theme.Line))
                e.Graphics.DrawLine(line, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            if (e.ColumnIndex < grid.Columns.Count - 1)
            {
                bool hot = e.ColumnIndex == hoverEdge || e.ColumnIndex == dragCol;
                int w = Math.Max(2, S(hot ? 3 : 2));
                var grip = new Rectangle(e.CellBounds.Right - w, e.CellBounds.Top + S(7), w, e.CellBounds.Height - S(14));
                using (var b = new SolidBrush(hot ? Theme.Accent : Theme.Edge)) e.Graphics.FillRectangle(b, grip);
            }
            e.Handled = true;
        }

        // Group labels span the row instead of being clipped to the first columns.
        void PaintGroupRow(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            var r = grid.Rows[e.RowIndex];
            if (r.Tag is RowRef) return;
            var flags = TextFormatFlags.Bottom | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            var bounds = new Rectangle(e.RowBounds.Left + S(8), e.RowBounds.Top, e.RowBounds.Width - S(16), e.RowBounds.Height - S(4));
            string label = r.Cells[ColAction].Value as string ?? "", extra = r.Cells[ColHint].Value as string;
            TextRenderer.DrawText(e.Graphics, label, groupFont, bounds, Theme.Accent, flags);
            if (string.IsNullOrEmpty(extra)) return;
            int x = Math.Max(grid.GetColumnDisplayRectangle(ColHint, false).Left + S(8),
                bounds.Left + TextRenderer.MeasureText(e.Graphics, label, groupFont, bounds.Size, flags).Width + S(24));
            TextRenderer.DrawText(e.Graphics, extra, groupFont, new Rectangle(x, bounds.Top, Math.Max(0, bounds.Right - x), bounds.Height), Theme.Accent, flags);
        }

        void BuildRows()
        {
            grid.Rows.Clear();
            rows.Clear();
            extraRows.Clear();
            optionalRows.Clear();
            string group = null;
            foreach (var def in ActionDef.All)
            {
                if (def.Group != group)
                {
                    group = def.Group;
                    int hi = grid.Rows.Add(group, "", "", "", "", "", "", "", "");
                    var hr = grid.Rows[hi];
                    hr.Height = S(34);
                    hr.DefaultCellStyle = new DataGridViewCellStyle
                    {
                        BackColor = Theme.Bg, SelectionBackColor = Theme.Bg, ForeColor = Theme.Accent, SelectionForeColor = Theme.Accent,
                        Font = groupFont, Alignment = DataGridViewContentAlignment.BottomLeft, Padding = P(8, 0, 8, 4)
                    };
                    if (group == ActionDef.OptionalGroup) optionalHeader = hr;
                }
                int i = grid.Rows.Add(def.Name, def.Hint, def.Keys ?? "–", def.Pad ?? "–", "", "", "", "", "");
                var r = grid.Rows[i];
                r.Tag = new RowRef { Def = def, Slot = 0 };
                rows[def.Name] = r;
                extraRows[def.Name] = new List<DataGridViewRow>();
                foreach (int c in new[] { ColHint, ColKeys, ColPad }) r.Cells[c].Style.ForeColor = r.Cells[c].Style.SelectionForeColor = Theme.Dim;
                r.Cells[ColKeys].ToolTipText = DefaultTip("Keyboard & mouse", def.Keys, def.DefaultsNote);
                r.Cells[ColPad].ToolTipText = DefaultTip("Controller", def.Pad, def.DefaultsNote);
                r.Cells[ColAction].Style.Font = bold;
                if (def.Optional) { optionalRows.Add(r); r.Visible = optionalOpen; }
            }
            UpdateOptionalHeader();
        }

        static string DefaultTip(string what, string value, string note)
        {
            if (value == null) return what + ": not listed in the game's controls menu.";
            return what + " default: " + value + (note != null ? "\n" + note : "");
        }

        void UpdateOptionalHeader()
        {
            if (optionalHeader == null) return;
            int bound = optionalRows.Count(r => InputCount(((RowRef)r.Tag).Def.Name) > 0);
            optionalHeader.Cells[ColAction].Value = (optionalOpen ? "▾  " : "▸  ") + ActionDef.OptionalGroup;
            optionalHeader.Cells[ColHint].Value = optionalOpen ? "click to hide" : string.Format("{0} actions, {1} bound — click to show", optionalRows.Count, bound);
        }

        void ToggleOptional()
        {
            if (listening != null && listening.Optional) StopListening();
            optionalOpen = !optionalOpen;
            grid.CurrentCell = null;
            foreach (var r in optionalRows) r.Visible = optionalOpen;
            RefreshAll(); // the rows of extra inputs follow their action's row
            if (optionalOpen && optionalRows.Count > 0) grid.FirstDisplayedScrollingRowIndex = Math.Max(0, optionalHeader.Index - 2);
        }

        // ---------------------------------------------------------------- folder & files

        static string SettingsFile
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AC8-HOTAS-Binder", "folder.txt"); }
        }

        static bool IsModFolder(string d)
        {
            return !string.IsNullOrEmpty(d) && File.Exists(System.IO.Path.Combine(d, "bindings.lua"));
        }

        static string FindModFolder()
        {
            string exe = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            var candidates = new List<string> { exe, System.IO.Path.GetDirectoryName(exe) };
            try { if (File.Exists(SettingsFile)) candidates.Add(File.ReadAllText(SettingsFile).Trim()); } catch { }
            return candidates.FirstOrDefault(IsModFolder);
        }

        string AskForFolder()
        {
            MessageBox.Show(this,
                "Couldn't find bindings.lua next to this program.\n\nPick your AC8HOTAS mod folder, usually:\n" +
                @"...\ACE COMBAT 8\Game\Binaries\Win64\UE4SS\Mods\AC8HOTAS",
                "AC8 HOTAS Binder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            using (var dlg = new FolderBrowserDialog { Description = "Select the AC8HOTAS mod folder (the one containing bindings.lua)", ShowNewFolderButton = false })
            {
                while (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (IsModFolder(dlg.SelectedPath)) return dlg.SelectedPath;
                    MessageBox.Show(this, "That folder has no bindings.lua. Pick the AC8HOTAS folder itself.", "AC8 HOTAS Binder",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return null;
        }

        void ChangeFolder()
        {
            if (!ConfirmDiscard()) return;
            string dir = AskForFolder();
            if (dir != null) OpenFolder(dir);
        }

        void OpenFolder(string dir)
        {
            StopListening();
            modDir = dir;
            bindingsPath = System.IO.Path.Combine(dir, "bindings.lua");
            devicesPath = System.IO.Path.Combine(dir, "Scripts", "devices.lua");
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile));
                File.WriteAllText(SettingsFile, dir);
            }
            catch { }
            pathLabel.Text = bindingsPath;
            DetectFeatures();
            current.Clear();
            LoadCatalog();
            LoadBindings(true);
            BuildRows();
            Rescan(false);
            RefreshAll();
            if (catalog.Count == 0)
                Banner("No game device list yet. Launch the game once with AC8HOTAS installed and your controllers plugged in, quit, then click Rescan devices.", BannerKind.Warn);
            else
                Banner("Click an action to bind it.", BannerKind.Info);
        }

        void DetectFeatures()
        {
            features = ModFeatures.Detect(modDir);
            grid.Columns[ColAdd].Visible = features.Lists;
        }

        void LoadCatalog()
        {
            catalog = new List<CatalogDevice>();
            try
            {
                if (File.Exists(devicesPath))
                {
                    catalog = CatalogDevice.Parse(File.ReadAllText(devicesPath));
                    devicesStamp = File.GetLastWriteTimeUtc(devicesPath);
                }
            }
            catch (IOException) { }
            catalogById.Clear(); catalogByProduct.Clear(); duplicateProducts.Clear();
            foreach (var d in catalog)
            {
                catalogById[d.Id] = d;
                if (catalogByProduct.ContainsKey(d.Product)) duplicateProducts.Add(d.Product);
                else catalogByProduct[d.Product] = d;
            }
        }

        // reset=false keeps unsaved edits; actions the user hasn't touched follow the file.
        void LoadBindings(bool reset)
        {
            var text = File.ReadAllText(bindingsPath, Encoding.UTF8);
            var fresh = BindingsDoc.Parse(text);
            bindingsStamp = File.GetLastWriteTimeUtc(bindingsPath);
            foreach (var def in ActionDef.All)
            {
                string n = def.Name;
                List<Binding> was, now;
                disk.Bindings.TryGetValue(n, out was);
                fresh.Bindings.TryGetValue(n, out now);
                bool edited = !reset && current.ContainsKey(n) && !Binding.SameList(current[n], was) && !disk.Locked.ContainsKey(n);
                if (fresh.Locked.ContainsKey(n)) current.Remove(n);
                else if (!edited) current[n] = now;
            }
            disk = fresh;
        }

        void CheckFiles()
        {
            if (bindingsPath == null) return;
            try
            {
                bool changed = false;
                if (File.Exists(devicesPath) && File.GetLastWriteTimeUtc(devicesPath) != devicesStamp)
                {
                    LoadCatalog();
                    Rescan(false);
                    changed = true;
                }
                if (File.GetLastWriteTimeUtc(bindingsPath) != bindingsStamp)
                {
                    LoadBindings(false);
                    changed = true;
                }
                if (changed)
                {
                    RefreshAll();
                    if (listening == null)
                        Banner("bindings.lua was updated on disk (probably the game) and has been reloaded." +
                               (DirtyNames().Count > 0 ? " Your unsaved changes were kept." : ""), BannerKind.Info);
                }
            }
            catch (IOException) { } // the game may be mid-write; try again next tick
        }

        // ---------------------------------------------------------------- devices

        CatalogDevice Match(HidDevice d)
        {
            CatalogDevice c;
            return catalogByProduct.TryGetValue(d.Product, out c) ? c : null;
        }

        void Rescan(bool reloadCatalog)
        {
            if (reloadCatalog) { DetectFeatures(); LoadCatalog(); RefreshAll(); }
            StopListening();
            hid.Rescan();
            deviceStrip.SuspendLayout();
            deviceStrip.Controls.Clear();
            foreach (var d in hid.Devices)
            {
                var c = Match(d);
                bool dup = c != null && duplicateProducts.Contains(c.Product);
                Color col = c == null ? Theme.Warn : dup ? Theme.Bad : Theme.Good;
                string extra = c == null ? "  (not in game list yet)" : dup ? "  (duplicate model)" : "";
                var chip = Chip("●  " + Short(d.Name) + extra, col);
                tips.SetToolTip(chip, c == null
                    ? d.Name + "\nThe game hasn't listed this controller yet. Launch the game once with it plugged in, quit, then click Rescan devices."
                    : dup ? d.Name + "\nTwo connected controllers share this product ID. AC8HOTAS can't tell them apart."
                    : string.Format("{0}\n{1}  {2}\n{3} axes, {4} buttons{5}", c.Name, c.Id, c.Product, d.Axes.Count, d.ButtonCount, d.HasHat ? ", 1 hat" : ""));
                deviceStrip.Controls.Add(chip);
            }
            foreach (var c in catalog.Where(c => !hid.Devices.Any(d => string.Equals(d.Product, c.Product, StringComparison.OrdinalIgnoreCase))))
            {
                var chip = Chip("○  " + Short(c.Name) + "  (not connected)", Theme.Dim);
                tips.SetToolTip(chip, c.Name + "\nListed by the game but not plugged in now.");
                deviceStrip.Controls.Add(chip);
            }
            if (deviceStrip.Controls.Count == 0) deviceStrip.Controls.Add(Chip("No controllers found", Theme.Warn));
            deviceStrip.ResumeLayout();
        }

        Label Chip(string text, Color color)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color, Margin = P(0, 6, 22, 0), Font = Font };
        }

        static string Short(string name)
        {
            name = Regex.Replace(name.Trim(), @"\s+", " ");
            return name.Length > 34 ? name.Substring(0, 33) + "…" : name;
        }

        string DeviceLabel(string id)
        {
            CatalogDevice c;
            return catalogById.TryGetValue(id, out c) ? Short(c.Name) : null;
        }

        static string InputLabel(string token)
        {
            if (token.StartsWith("Button")) return "Button " + token.Substring(6);
            if (token.StartsWith("POV1_")) return "Hat " + token.Substring(5);
            if (token.StartsWith("Slider")) return "Slider " + token.Substring(6);
            return token + " axis";
        }

        // ---------------------------------------------------------------- rows

        // Each action has its own row for its first input (slot 0); inputs 2, 3, ... get rows
        // underneath it, as does an input being added.
        class RowRef { public ActionDef Def; public int Slot; }

        List<Binding> Inputs(string name)
        {
            List<Binding> l;
            return current.TryGetValue(name, out l) ? l : null;
        }

        int InputCount(string name)
        {
            var l = Inputs(name);
            return l == null ? 0 : l.Count;
        }

        bool Unsupported(ActionDef def) { return def.DirectSelect && !features.WeaponSelect; }

        DataGridViewRow RowFor(ActionDef def, int slot)
        {
            return slot == 0 ? rows[def.Name] : extraRows[def.Name][slot - 1];
        }

        List<string> DirtyNames()
        {
            var l = new List<string>();
            foreach (var def in ActionDef.All)
            {
                if (disk.Locked.ContainsKey(def.Name) || !current.ContainsKey(def.Name)) continue;
                List<Binding> d; disk.Bindings.TryGetValue(def.Name, out d);
                if (!Binding.SameList(current[def.Name], d)) l.Add(def.Name);
            }
            return l;
        }

        void RefreshAll()
        {
            if (rows.Count == 0) return;
            var dirty = new HashSet<string>(DirtyNames());
            foreach (var def in ActionDef.All) RefreshRow(def, dirty.Contains(def.Name));
            UpdateOptionalHeader();
            int n = dirty.Count;
            // Buttons stay visible and clickable; the unsaved state is shown by fill, counts and colour.
            statusLabel.Text = n == 0 ? "✓  All changes saved to bindings.lua"
                : "●  " + n + (n == 1 ? " unsaved change" : " unsaved changes") + ". Click Save bindings to write them.";
            statusLabel.ForeColor = n == 0 ? Theme.Good : Theme.Accent;
            statusLabel.Font = n == 0 ? Font : bold;
            saveButton.Text = n == 0 ? "Save bindings" : "Save bindings (" + n + ")";
            saveButton.Apply(n == 0 ? BtnStyle.OutlineAccent : BtnStyle.Filled);
            revertButton.Apply(n == 0 ? BtnStyle.Outline : BtnStyle.OutlineDanger);
            Text = "AC8 HOTAS Binder" + (n > 0 ? " •" : "");
        }

        // Adds or removes the rows under an action so there is one per extra input.
        void SyncExtraRows(ActionDef def)
        {
            var main = rows[def.Name];
            var extras = extraRows[def.Name];
            int n = disk.Locked.ContainsKey(def.Name) ? 0 : InputCount(def.Name);
            int want = Math.Max(0, n - 1) + (listening == def && listenSlot >= Math.Max(n, 1) ? 1 : 0);
            while (extras.Count > want)
            {
                var x = extras[extras.Count - 1];
                extras.RemoveAt(extras.Count - 1);
                grid.Rows.Remove(x);
            }
            while (extras.Count < want)
            {
                int at = main.Index + extras.Count + 1;
                var values = new object[grid.Columns.Count];
                for (int i = 0; i < values.Length; i++) values[i] = "";
                values[ColAction] = "    ↳  also";
                grid.Rows.Insert(at, values);
                var r = grid.Rows[at];
                r.Tag = new RowRef { Def = def, Slot = extras.Count + 1 };
                r.Cells[ColAction].Style.ForeColor = r.Cells[ColAction].Style.SelectionForeColor = Theme.Dim;
                extras.Add(r);
            }
            foreach (var x in extras) x.Visible = main.Visible;
        }

        void RefreshRow(ActionDef def, bool dirty)
        {
            SyncExtraRows(def);
            var main = rows[def.Name];
            main.Cells[ColAction].Value = def.Name + (dirty ? "  •" : "");
            main.Cells[ColAction].Style.ForeColor = main.Cells[ColAction].Style.SelectionForeColor = dirty ? Theme.Accent : Theme.Text;
            var list = Inputs(def.Name);
            RefreshSlot(def, main, 0, list);
            var extras = extraRows[def.Name];
            for (int i = 0; i < extras.Count; i++) RefreshSlot(def, extras[i], i + 1, list);
        }

        void RefreshSlot(ActionDef def, DataGridViewRow r, int slot, List<Binding> list)
        {
            bool isListening = listening == def && listenSlot == slot;
            Color bg = isListening ? Theme.AccentDark : (r == flashRow && DateTime.Now < flashUntil) ? Theme.GoodDark : Theme.Row;
            r.DefaultCellStyle.BackColor = bg;
            r.DefaultCellStyle.SelectionBackColor = isListening ? Theme.AccentDark : (bg == Theme.Row ? Theme.RowHover : bg);

            var bc = r.Cells[ColBinding];
            var dc = r.Cells[ColDirection];
            var sc = r.Cells[ColShared];
            var ac = r.Cells[ColAdd];
            var cc = r.Cells[ColClear];
            dc.Value = sc.Value = ac.Value = cc.Value = "";
            bc.ToolTipText = dc.ToolTipText = ac.ToolTipText = cc.ToolTipText = "";

            string locked;
            if (disk.Locked.TryGetValue(def.Name, out locked))
            {
                SetCell(bc, "Edited by hand — " + locked, Theme.Warn);
                bc.ToolTipText = "This tool leaves it alone. Edit it in bindings.lua directly, or set it back to nil to edit it here.";
                return;
            }
            if (isListening)
            {
                string what = def.IsAxis ? def.Prompt : "press a button or hat direction";
                SetCell(bc, (slot > 0 && slot >= InputCount(def.Name) ? "Adding another input…  " : "Listening…  ") + what, Theme.Accent);
                SetCell(cc, "Cancel", Theme.Dim);
                return;
            }
            Binding b = list != null && slot < list.Count ? list[slot] : null;
            if (b == null)
            {
                if (Unsupported(def))
                {
                    SetCell(bc, "Needs AC8HOTAS 0.3.2 or newer", Theme.Dim);
                    bc.ToolTipText = "Direct weapon selection arrived in a later AC8HOTAS. Your installed version would reject bindings.lua with this bound.";
                }
                else SetCell(bc, "—  click to bind", Theme.Dim);
                return;
            }
            string dev = DeviceLabel(b.Device);
            if (dev == null)
            {
                SetCell(bc, "Unknown device  ·  " + InputLabel(b.Input), Theme.Bad);
                bc.ToolTipText = "Device " + b.Device + " isn't in the game's device list. Plug it in and launch the game once, or rebind this action.";
            }
            else SetCell(bc, dev + "  ·  " + InputLabel(b.Input), Theme.Text);
            string problem = Unsupported(def)
                ? "Your AC8HOTAS has no direct weapon selection, so it rejects bindings.lua while this is bound. Clear it, or update AC8HOTAS to 0.3.2 or newer."
                : slot > 0 && !features.Lists
                ? "Your AC8HOTAS takes one input per action, so it rejects bindings.lua while this action has several. Clear the extra ones, or update AC8HOTAS to 0.3.2 or newer."
                : null;
            if (problem != null)
            {
                bc.Style.ForeColor = bc.Style.SelectionForeColor = Theme.Bad;
                bc.ToolTipText = problem;
            }

            if (def.IsAxis)
            {
                SetCell(dc, b.Invert ? "Inverted" : "Normal", b.Invert ? Theme.Accent : Theme.Dim);
                dc.ToolTipText = "Click to flip direction";
            }
            var shared = current.Where(kv => kv.Key != def.Name && kv.Value != null && kv.Value.Any(x => x.SameInput(b))).Select(kv => kv.Key).ToList();
            if (shared.Count > 0) SetCell(sc, string.Join(", ", shared), Theme.Warn);
            if (slot == 0 && features.Lists && !Unsupported(def))
            {
                SetCell(ac, "+ Add", Theme.Dim);
                ac.ToolTipText = "Bind another input to " + def.Name + " as well";
            }
            SetCell(cc, "Clear", Theme.Dim);
            cc.ToolTipText = list.Count > 1 ? "Remove this input (Delete)" : "Unbind (Delete)";
        }

        static void SetCell(DataGridViewCell c, string text, Color color)
        {
            c.Value = text;
            c.Style.ForeColor = c.Style.SelectionForeColor = color;
        }

        void GridCellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var r = grid.Rows[e.RowIndex];
            if (r == optionalHeader) { ToggleOptional(); return; }
            var rr = r.Tag as RowRef;
            if (rr == null) return;
            var def = rr.Def;

            if (listening == def) { StopListening(); Banner("Cancelled.", BannerKind.Info); return; }
            var list = Inputs(def.Name);
            Binding b = list != null && rr.Slot < list.Count ? list[rr.Slot] : null;
            if (e.ColumnIndex == ColClear && b != null && listening == null) { Clear(def, rr.Slot); return; }
            if (e.ColumnIndex == ColAdd && rr.Slot == 0 && b != null && features.Lists && !Unsupported(def))
            {
                StartListening(def, list.Count);
                return;
            }
            if (e.ColumnIndex == ColDirection && def.IsAxis && b != null && listening == null)
            {
                var l = new List<Binding>(list);
                l[rr.Slot] = new Binding { Device = b.Device, Input = b.Input, Invert = !b.Invert, Mode = b.Mode };
                current[def.Name] = l;
                RefreshAll();
                Banner(def.Name + (l.Count > 1 ? " input " + (rr.Slot + 1) : "") + " direction " + (b.Invert ? "set to normal." : "inverted."), BannerKind.Info);
                return;
            }
            StartListening(def, rr.Slot);
        }

        // Removes one input; the ones below it move up a slot.
        void Clear(ActionDef def, int slot)
        {
            if (disk.Locked.ContainsKey(def.Name)) return;
            var list = Inputs(def.Name);
            if (list == null || slot >= list.Count) return;
            var l = new List<Binding>(list);
            l.RemoveAt(slot);
            current[def.Name] = l.Count == 0 ? null : l;
            RefreshAll();
            Banner(l.Count == 0 ? def.Name + " unbound." : "Removed input " + (slot + 1) + " from " + def.Name + ".", BannerKind.Info);
        }

        // ---------------------------------------------------------------- listening

        // slot: the input to replace, or the input count to add one more.
        void StartListening(ActionDef def, int slot)
        {
            string locked;
            if (disk.Locked.TryGetValue(def.Name, out locked))
            {
                Banner(def.Name + " is edited by hand (" + locked + "); edit it in bindings.lua directly.", BannerKind.Warn);
                return;
            }
            if (Unsupported(def))
            {
                Banner(def.Name + " needs AC8HOTAS 0.3.2 or newer. Your installed version would reject bindings.lua with it bound.", BannerKind.Warn);
                return;
            }
            if (catalog.Count == 0)
            {
                Banner("The game hasn't listed any controllers yet. Launch the game once with AC8HOTAS and your controllers plugged in, quit, then click Rescan devices.", BannerKind.Warn);
                return;
            }
            int n = InputCount(def.Name);
            slot = Math.Min(slot, n);
            bool adding = n > 0 && slot == n;
            if (adding && !features.Lists) return;
            listening = def;
            listenSlot = slot;
            baseline.Clear();
            ignoredAxes.Clear();
            foreach (var d in hid.Devices) baseline[d] = d.Snapshot();
            // The action's other inputs can't be picked again, so a head tracker already bound to
            // this action can't win while you move a second axis. Other axes it drives still can.
            foreach (var d in hid.Devices)
            {
                var c = Match(d);
                if (c == null) continue;
                for (int i = 0; i < d.Axes.Count; i++)
                    if (IsOtherInput(c.Id, d.Axes[i].Token)) ignoredAxes.Add(d.Path + "|" + i);
            }
            InputEvent stale;
            while (hid.Events.TryDequeue(out stale)) { }
            RefreshAll();
            Banner((adding ? "ADD TO " : "") + def.Name.ToUpperInvariant() + "  —  " + (def.IsAxis ? def.Prompt : "press a button or hat direction") + "        Esc to cancel", BannerKind.Listen);
        }

        // True if the action being listened for already has this input in another slot.
        bool IsOtherInput(string device, string token)
        {
            var list = listening == null ? null : Inputs(listening.Name);
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (i != listenSlot && string.Equals(list[i].Device, device, StringComparison.OrdinalIgnoreCase) && list[i].Input == token) return true;
            return false;
        }

        void StopListening()
        {
            if (listening == null) return;
            listening = null;
            RefreshAll();
        }

        void Poll()
        {
            if (flashRow != null && DateTime.Now >= flashUntil) { flashRow = null; RefreshAll(); }

            InputEvent ev;
            while (hid.Events.TryDequeue(out ev))
            {
                if (listening == null || listening.IsAxis) continue;
                var c = Match(ev.Device);
                if (c == null) { NotInCatalog(ev.Device); continue; }
                if (duplicateProducts.Contains(c.Product)) { DuplicateModel(c); continue; }
                string why;
                if (!c.Accepts(ev.Token, false, out why)) { Banner(why, BannerKind.Warn); continue; }
                if (IsOtherInput(c.Id, ev.Token)) { Banner(InputLabel(ev.Token) + " is already one of " + listening.Name + "'s inputs. Press a different one, or Esc to cancel.", BannerKind.Warn); continue; }
                Assign(listening, listenSlot, new Binding { Device = c.Id, Input = ev.Token });
                return;
            }

            if (listening == null || !listening.IsAxis) return;
            HidDevice bestDev = null; int bestAxis = -1; double bestDelta = 0;
            foreach (var d in hid.Devices)
            {
                if (!d.Alive) continue;
                var now = d.Snapshot();
                double[] b0;
                if (!baseline.TryGetValue(d, out b0)) { baseline[d] = now; continue; }
                for (int i = 0; i < now.Length; i++)
                {
                    if (double.IsNaN(now[i])) continue;
                    if (double.IsNaN(b0[i])) { b0[i] = now[i]; continue; }
                    if (ignoredAxes.Contains(d.Path + "|" + i)) continue;
                    double delta = now[i] - b0[i];
                    if (Math.Abs(delta) > Math.Abs(bestDelta)) { bestDelta = delta; bestDev = d; bestAxis = i; }
                }
            }
            if (bestDev == null || Math.Abs(bestDelta) < AxisThreshold) return;

            var cat = Match(bestDev);
            string token = bestDev.Axes[bestAxis].Token, reason;
            if (cat == null) { NotInCatalog(bestDev); ignoredAxes.Add(bestDev.Path + "|" + bestAxis); return; }
            if (duplicateProducts.Contains(cat.Product)) { DuplicateModel(cat); ignoredAxes.Add(bestDev.Path + "|" + bestAxis); return; }
            if (!cat.Accepts(token, true, out reason)) { Banner(reason, BannerKind.Warn); ignoredAxes.Add(bestDev.Path + "|" + bestAxis); return; }
            var list = Inputs(listening.Name);
            Binding old = list != null && listenSlot < list.Count ? list[listenSlot] : null;
            Assign(listening, listenSlot, new Binding
            {
                Device = cat.Id, Input = token, Invert = listening.InvertFor(bestDelta),
                Mode = old != null && Regex.IsMatch(old.Input, "^(X|Y|Z|Rx|Ry|Rz|Slider[12])$") ? old.Mode : null
            });
        }

        void NotInCatalog(HidDevice d)
        {
            Banner(Short(d.Name) + " isn't in the game's device list yet. Launch the game once with it plugged in, quit, then click Rescan devices.", BannerKind.Warn);
        }

        void DuplicateModel(CatalogDevice c)
        {
            Banner("Two connected controllers are the same model (" + Short(c.Name) + "). AC8HOTAS can't tell them apart, so they can't be bound.", BannerKind.Warn);
        }

        void Assign(ActionDef def, int slot, Binding b)
        {
            var list = Inputs(def.Name) == null ? new List<Binding>() : new List<Binding>(Inputs(def.Name));
            if (slot >= list.Count) { slot = list.Count; list.Add(b); }
            else list[slot] = b;
            current[def.Name] = list;
            listening = null;
            SyncExtraRows(def);
            flashRow = RowFor(def, slot);
            flashUntil = DateTime.Now.AddMilliseconds(900);
            RefreshAll();
            string msg = def.Name + (list.Count > 1 ? " input " + (slot + 1) : "") + "  →  " + (DeviceLabel(b.Device) ?? b.Device) + "  ·  " + InputLabel(b.Input);
            if (def.IsAxis) msg += b.Invert ? "  (inverted)" : "";
            var shared = current.Where(kv => kv.Key != def.Name && kv.Value != null && kv.Value.Any(x => x.SameInput(b))).Select(kv => kv.Key).ToList();
            if (shared.Count > 0) msg += "    also used by " + string.Join(", ", shared);
            // Inputs on one device share that device's game profile, which has a limited number
            // of slots per action. The binder can't see the limit; AC8HOTAS rejects the file if it's exceeded.
            bool sameDevice = list.Where((x, i) => i != slot && string.Equals(x.Device, b.Device, StringComparison.OrdinalIgnoreCase)).Any();
            if (sameDevice) msg += "    Same device twice: if AC8HOTAS then rejects your bindings, the game has too few slots for this.";
            Banner(msg, sameDevice ? BannerKind.Warn : BannerKind.Good);
        }

        // ---------------------------------------------------------------- save / revert

        bool Save()
        {
            StopListening();
            var dirty = DirtyNames();
            if (dirty.Count == 0)
            {
                Banner("Nothing to save. bindings.lua already matches what you see.", BannerKind.Info);
                return true;
            }
            try
            {
                string text = File.ReadAllText(bindingsPath, Encoding.UTF8);
                var changes = dirty.ToDictionary(n => n, n => current[n]);
                string updated = BindingsDoc.Apply(text, changes, b => DeviceLabel(b.Device) ?? "unknown device");
                var check = BindingsDoc.Parse(updated);
                foreach (var n in dirty)
                {
                    List<Binding> got; check.Bindings.TryGetValue(n, out got);
                    if (check.Locked.ContainsKey(n) || !Binding.SameList(got, current[n]))
                        throw new InvalidOperationException("Verification failed for " + n + "; nothing was written.");
                }
                File.Copy(bindingsPath, bindingsPath + ".binder.bak", true);
                File.WriteAllText(bindingsPath, updated, Utf8NoBom);
                disk = check;
                bindingsStamp = File.GetLastWriteTimeUtc(bindingsPath);
                RefreshAll();
                Banner("Saved " + dirty.Count + (dirty.Count == 1 ? " change" : " changes") + ". Launch the game to test them (if it's already running, F5 in game may reload them; otherwise restart it).", BannerKind.Good);
                return true;
            }
            catch (Exception ex)
            {
                Banner("Couldn't save: " + ex.Message, BannerKind.Bad);
                MessageBox.Show(this, "Couldn't save bindings.lua:\n\n" + ex.Message, "AC8 HOTAS Binder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        void Revert()
        {
            StopListening();
            int count = DirtyNames().Count;
            if (count == 0) { Banner("No unsaved changes to discard.", BannerKind.Info); return; }
            if (MessageBox.Show(this, "Discard " + count + (count == 1 ? " unsaved change?" : " unsaved changes?"), "AC8 HOTAS Binder",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.OK) return;
            foreach (var n in DirtyNames())
            {
                List<Binding> d; disk.Bindings.TryGetValue(n, out d);
                current[n] = d;
            }
            RefreshAll();
            Banner("Changes discarded.", BannerKind.Info);
        }

        bool ConfirmDiscard()
        {
            if (DirtyNames().Count == 0) return true;
            var r = MessageBox.Show(this, "Save your binding changes first?", "AC8 HOTAS Binder", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return false;
            return r == DialogResult.No || Save();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (bindingsPath != null && !ConfirmDiscard()) { e.Cancel = true; return; }
            pollTimer.Stop();
            fileTimer.Stop();
            hid.Stop();
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape && listening != null) { StopListening(); Banner("Cancelled.", BannerKind.Info); return true; }
            if (keyData == (Keys.Control | Keys.S)) { Save(); return true; }
            var rr = grid.CurrentRow != null ? grid.CurrentRow.Tag as RowRef : null;
            if (keyData == Keys.Enter && grid.Focused)
            {
                if (grid.CurrentRow == optionalHeader) ToggleOptional();
                else if (rr != null) { if (listening == rr.Def) StopListening(); else StartListening(rr.Def, rr.Slot); }
                return true;
            }
            if (keyData == Keys.Delete && grid.Focused && rr != null && listening == null) { Clear(rr.Def, rr.Slot); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------------------------------------------------------------- banner & help

        enum BannerKind { Info, Listen, Good, Warn, Bad }

        void Banner(string text, BannerKind kind)
        {
            bannerText.Text = text;
            switch (kind)
            {
                case BannerKind.Listen: bannerPanel.BackColor = Theme.Accent; bannerText.ForeColor = Color.FromArgb(25, 20, 10); break;
                case BannerKind.Good: bannerPanel.BackColor = Theme.GoodDark; bannerText.ForeColor = Theme.Good; break;
                case BannerKind.Warn: bannerPanel.BackColor = Theme.AccentDark; bannerText.ForeColor = Theme.Warn; break;
                case BannerKind.Bad: bannerPanel.BackColor = Theme.BadDark; bannerText.ForeColor = Theme.Bad; break;
                default: bannerPanel.BackColor = Theme.Panel; bannerText.ForeColor = Theme.Text; break;
            }
        }

        void ShowHelp()
        {
            using (var f = new HelpForm(scale)) f.ShowDialog(this);
        }
    }

    // Help window: a read-only rich text box with real hanging indents, so wrapped lines
    // stay aligned under their step text instead of relying on spaces in a MessageBox.
    class HelpForm : Form
    {
        readonly RichTextBox box;
        readonly float scale;
        readonly Font body = new Font("Segoe UI", 10f);
        readonly Font heading = new Font("Segoe UI Semibold", 11.5f);
        readonly Font strong = new Font("Segoe UI", 10f, FontStyle.Bold);
        readonly Font note = new Font("Segoe UI", 9f);

        int S(int px) { return (int)Math.Round(px * scale); }

        public HelpForm(float scale)
        {
            this.scale = scale;
            Text = "How AC8 HOTAS Binder works";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(660), S(640));
            MinimumSize = new Size(S(480), S(360));
            MainForm.FitToScreen(this, false);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            box = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text,
                ScrollBars = RichTextBoxScrollBars.Vertical, DetectUrls = true, TabStop = false, Cursor = Cursors.Arrow
            };
            box.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(e.LinkText); } catch { } };
            box.GotFocus += (s, e) => Native.HideCaret(box.Handle);
            var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), S(18), S(16), S(8)), BackColor = Theme.Bg };
            pad.Controls.Add(box);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = S(54), Padding = new Padding(S(16), S(10), S(16), S(10)), BackColor = Theme.Panel };
            var ok = new FlatBtn("Got it", BtnStyle.Filled) { Dock = DockStyle.Right, DialogResult = DialogResult.OK };
            footer.Controls.Add(ok);
            AcceptButton = CancelButton = ok;

            Controls.Add(pad);
            Controls.Add(footer);
            Fill();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int on = 1; Native.DwmSetWindowAttribute(Handle, 20, ref on, 4); } catch { }
            try { Native.SetWindowTheme(box.Handle, "DarkMode_Explorer", null); } catch { } // dark scrollbar
        }

        void Fill()
        {
            Heading("Quick start");
            Step("1.", "Launch Ace Combat 8 once with AC8HOTAS installed and your controllers plugged in, then quit. AC8HOTAS writes the list of controllers this tool needs.");
            Step("2.", "With the game closed, click an action (or select it and press Enter). The bar at the bottom turns amber and the binder listens:");
            Bullet("Buttons: ", "press the button or hat direction you want.");
            Bullet("Axes: ", "follow the prompt, e.g. push the stick FORWARD. The way you move it sets Normal or Inverted.");
            Bullet("Esc ", "cancels. Clear (or Delete) unbinds. Click an axis's Direction to flip it.");
            Bullet("+ Add ", "binds another input to the same action, e.g. a head tracker and a throttle mini-stick both on the camera. Each input gets its own row with its own Direction and Clear. Pause head tracking while you bind a stick, so a head movement isn't picked up instead.");
            Step("3.", "Click Save bindings (Ctrl+S).");
            Step("4.", "Launch the game and test your controls. If something feels off, quit, adjust it here, save and launch again.");

            Heading("Changing bindings while the game is running");
            Para("AC8HOTAS reloads bindings.lua when F5 is pressed in game, so you can try saving and pressing F5 instead of restarting. " +
                 "If a change doesn't seem to take effect, restart the game. Starting the game always loads the saved file.");

            Heading("Good to know");
            Bullet(null, "The KB / Mouse and Controller columns show the game's own default controls (Expert mode), so you can see what each action does before giving it a HOTAS control. Hover a cell for the in-game name.");
            Bullet(null, "One control can drive several actions, e.g. the same hat for Look and the menu D-pad. The \"Also used by\" column shows sharing.");
            Bullet(null, "Several inputs per action (+ Add) and the SelectMissiles / SelectSpecialWeapon rows need AC8HOTAS 0.3.2 or newer. With an older AC8HOTAS they're hidden or greyed out, because it would reject the whole bindings.lua.");
            Bullet(null, "Inputs on different devices are the safe way to add more. Several inputs on the same device can need more slots than the game has for that action; AC8HOTAS then rejects the file, and its HOTAS-*.log says which action.");
            Bullet(null, "Buttons already held down when you start listening are ignored, so switches resting \"on\" won't hijack a binding.");
            Bullet(null, "Drag the grips between column headers to resize columns.");
            Bullet(null, "Every save backs up the previous file to bindings.lua.binder.bak.");
            Bullet(null, "This tool only reads your controllers. It never changes their settings or sends input to the game.");

            Heading("Credits");
            Para("Built for AC8 HOTAS Support (AC8HOTAS) by Drumsmasher17, which does all the real work of getting HOTAS input into the game, " +
                 "alongside AC8AnalogYaw (also by Drumsmasher17). Both run on UE4SS by the UE4SS-RE team. Thank you!");
            Link("https://www.nexusmods.com/acecombat8wingsoftheve/mods/8");
            Link("https://github.com/Drumsmasher17/AC8AnalogueYaw");
            Link("https://github.com/UE4SS-RE/RE-UE4SS");
            Para("");
            Run("AC8 HOTAS Binder " + Program.Version + " by PhantomLasagne (Ph4ntomLasagne on Nexus) · MIT License · unofficial companion tool", note, Theme.Dim);
            box.SelectionStart = 0;
        }

        readonly Font spacerSmall = new Font("Segoe UI", 3f), spacerLarge = new Font("Segoe UI", 7f);

        void Format(int indent, int hanging, bool bullet)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionBullet = bullet;
            box.SelectionIndent = S(indent);
            box.SelectionHangingIndent = S(hanging);
            box.SelectionTabs = new[] { S(indent + hanging) };
            if (bullet) box.BulletIndent = S(14);
        }

        // spaceBefore: 0 none, 1 small gap (between steps), 2 large gap (before headings)
        void Paragraph(int indent, int hanging, bool bullet, int spaceBefore)
        {
            if (spaceBefore > 0)
            {
                Format(0, 0, false);
                Run("\n", spaceBefore > 1 ? spacerLarge : spacerSmall, Theme.Text);
            }
            Format(indent, hanging, bullet);
        }

        void Run(string text, Font font, Color color)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionLength = 0;
            box.SelectionFont = font;
            box.SelectionColor = color;
            box.SelectedText = text;
        }

        void Heading(string text)
        {
            Paragraph(0, 0, false, box.TextLength > 0 ? 2 : 0);
            Run(text + "\n", heading, Theme.Accent);
        }

        void Step(string num, string text)
        {
            Paragraph(0, 26, false, 1);
            Run(num + "\t", strong, Theme.Accent);
            Run(text + "\n", body, Theme.Text);
        }

        void Bullet(string lead, string text)
        {
            Paragraph(26, 0, true, 0);
            if (lead != null) Run(lead, strong, Theme.Text);
            Run(text + "\n", body, Theme.Text);
        }

        void Para(string text)
        {
            Paragraph(0, 0, false, 0);
            Run(text + "\n", body, Theme.Text);
        }

        void Link(string url)
        {
            Paragraph(14, 0, false, 0);
            Run(url + "\n", body, Theme.Blue);
        }
    }

    // ------------------------------------------------------------------------------------
    // Headless checks:  AC8-HOTAS-Binder.exe --selftest "<mod folder>" "<report file>"
    // ------------------------------------------------------------------------------------

    static class SelfTest
    {
        public static int Run(string dir, string outFile)
        {
            var log = new StringBuilder();
            int failures = 0;
            Action<bool, string> check = (ok, what) => { log.AppendLine((ok ? "PASS  " : "FAIL  ") + what); if (!ok) failures++; };
            try
            {
                string text = File.ReadAllText(Path.Combine(dir, "bindings.lua"), Encoding.UTF8);
                var catalog = CatalogDevice.Parse(File.ReadAllText(Path.Combine(dir, "Scripts", "devices.lua")));
                log.AppendLine("Catalog:");
                foreach (var c in catalog)
                    log.AppendLine(string.Format("  {0} {1} '{2}' axes={3} buttons={4} povs={5} inputs={6}", c.Id, c.Product, c.Name, c.Axes, c.Buttons, c.Povs, c.Inputs.Count));

                var doc = BindingsDoc.Parse(text);
                Func<ActionDef, List<Binding>, string> show = (a, l) => l == null ? "nil" : string.Join("  +  ", l.Select(x => x.ToLua(a.IsAxis)));
                log.AppendLine("Bindings:");
                foreach (var a in ActionDef.All)
                {
                    List<Binding> b; string why;
                    if (doc.Locked.TryGetValue(a.Name, out why)) log.AppendLine("  " + a.Name + " LOCKED " + why);
                    else if (doc.Bindings.TryGetValue(a.Name, out b)) log.AppendLine("  " + a.Name + " = " + show(a, b));
                    else log.AppendLine("  " + a.Name + " (missing)");
                }
                var feat = ModFeatures.Detect(dir);
                log.AppendLine("AC8HOTAS here: several inputs per action " + (feat.Lists ? "yes" : "no") + ", direct weapon select " + (feat.WeaponSelect ? "yes" : "no"));
                check(ActionDef.All.Count == 52, "52 actions defined");
                check(ActionDef.All.Count(x => x.Keys != null) == 29 && ActionDef.All.Count(x => x.Pad != null) == 27, "game defaults loaded (29 keyboard, 27 controller)");

                check(BindingsDoc.Apply(text, new Dictionary<string, List<Binding>>(), b => "x") == text, "no-op apply preserves file exactly");

                Func<Binding[], List<Binding>> L = xs => new List<Binding>(xs);
                Func<BindingsDoc, string, List<Binding>> get = (d, n) => { List<Binding> x; d.Bindings.TryGetValue(n, out x); return x; };
                Func<string, string[]> split = t => t.Replace("\r\n", "\n").Split('\n');
                var gun = new Binding { Device = "{TEST}", Input = "Button99" };
                var pitch = new Binding { Device = "{TEST}", Input = "Ry", Invert = true, Mode = "zero_to_one" };
                var changed = BindingsDoc.Apply(text, new Dictionary<string, List<Binding>> { { "Gun", L(new[] { gun }) }, { "Pitch", L(new[] { pitch }) }, { "Flare", null } }, b => "Test device");
                var re = BindingsDoc.Parse(changed);
                check(Binding.SameList(get(re, "Gun"), L(new[] { gun })), "button binding round-trips");
                check(Binding.SameList(get(re, "Pitch"), L(new[] { pitch })), "axis binding with invert+mode round-trips");
                check(re.Bindings.ContainsKey("Flare") && re.Bindings["Flare"] == null, "clearing writes nil");
                int diff = 0;
                var a1 = split(text); var a2 = split(changed);
                check(a1.Length == a2.Length, "line count unchanged");
                for (int i = 0; i < Math.Min(a1.Length, a2.Length); i++) if (a1[i] != a2[i]) diff++;
                check(diff <= 3, "only the edited lines changed (" + diff + ")");
                int tail = text.IndexOf(BindingsDoc.Marker);
                check(tail < 0 || changed.EndsWith(text.Substring(tail)), "detected-devices section untouched");

                var noGun = Regex.Replace(text, @"^[ \t]*Gun[ \t]*=.*\r?\n", "", RegexOptions.Multiline);
                var inserted = BindingsDoc.Parse(BindingsDoc.Apply(noGun, new Dictionary<string, List<Binding>> { { "Gun", L(new[] { gun }) } }, b => "Test"));
                check(Binding.SameList(get(inserted, "Gun"), L(new[] { gun })), "missing action gets inserted into bindings table");

                // Several inputs per action (AC8HOTAS 0.3.2+), on a sample in the mod's documented
                // format plus the other shapes a hand-edited file can take.
                string sample = string.Join("\n", new[]
                {
                    "return {",
                    "    version = 1,",
                    "    bindings = {",
                    "        Target = {",
                    "            { device = \"{AAAA-1}\", input = \"Button1\" }, -- Stick {left}",
                    "            { device = \"{BBBB-2}\", input = \"Button2\" },",
                    "        }, -- two buttons",
                    "        Gun = { { device = \"{AAAA-1}\", input = \"Button3\" } },",
                    "        CameraYaw = { { device = \"{AAAA-1}\", input = \"Rx\", invert = false }, { device = \"{BBBB-2}\", input = \"Rx\", invert = true, mode = \"zero_to_one\" } },",
                    "        Missile = nil, -- a -- comment { with a brace",
                    "        View = {",
                    "            device = \"{AAAA-1}\",",
                    "            input = \"Button5\",",
                    "        },",
                    "        Flare = {},",
                    "        Radar = { device = \"{AAAA-1}\" },",
                    "        Gear = { { device = \"{AAAA-1}\", input = \"Button6\" }, nil },",
                    "        Pause = nil, Platform = nil,",
                    "    },",
                    "}",
                    "",
                    BindingsDoc.Marker + " (generated; edit bindings above)",
                    "-- { device = \"{AAAA-1}\", input = \"X\", invert = false },",
                    "-- END DETECTED DEVICES",
                    ""
                });
                var s = BindingsDoc.Parse(sample);
                var t1 = get(s, "Target");
                check(t1 != null && t1.Count == 2 && t1[0].Device == "{AAAA-1}" && t1[0].Input == "Button1" && t1[1].Device == "{BBBB-2}" && t1[1].Input == "Button2",
                    "multi-line list in the mod's format parses");
                var g1 = get(s, "Gun");
                check(g1 != null && g1.Count == 1 && g1[0].Input == "Button3", "one-element list parses");
                var c1 = get(s, "CameraYaw");
                check(c1 != null && c1.Count == 2 && !c1[0].Invert && c1[1].Invert && c1[1].Mode == "zero_to_one", "one-line list of axes parses");
                check(s.Bindings.ContainsKey("Missile") && s.Bindings["Missile"] == null, "nil with a brace in its comment parses");
                var v1 = get(s, "View");
                check(v1 != null && v1.Count == 1 && v1[0].Input == "Button5", "single input written over several lines parses");
                check(s.Locked.ContainsKey("Flare") && s.Locked.ContainsKey("Radar") && s.Locked.ContainsKey("Gear") && s.Locked.ContainsKey("Pause"),
                    "empty list, incomplete input, nil in a list and two actions on one line stay locked");

                var x1 = new Binding { Device = "{CCCC-3}", Input = "Button7" };
                var x2 = new Binding { Device = "{DDDD-4}", Input = "POV1_Up" };
                var grown = BindingsDoc.Apply(sample, new Dictionary<string, List<Binding>> { { "Missile", L(new[] { x1, x2 }) } }, b => "Dev {x}");
                var sg = BindingsDoc.Parse(grown);
                check(Binding.SameList(get(sg, "Missile"), L(new[] { x1, x2 })), "writing a list round-trips");
                var b1 = split(sample); var b2 = split(grown);
                int at = Array.FindIndex(b1, l => l.TrimStart().StartsWith("Missile"));
                bool untouched = b2.Length == b1.Length + 3;
                for (int i = 0; i < at && untouched; i++) untouched = b1[i] == b2[i];
                for (int i = at + 1; i < b1.Length && untouched; i++) untouched = b1[i] == b2[i + 3];
                check(untouched, "a list replaces only its own lines");
                check(Binding.SameList(get(sg, "Target"), t1) && Binding.SameList(get(sg, "CameraYaw"), c1) && sg.Locked.Count == s.Locked.Count,
                    "other entries unchanged after writing a list");

                var axes = L(new[] { new Binding { Device = "{AAAA-1}", Input = "Rx" }, new Binding { Device = "{BBBB-2}", Input = "X", Invert = true, Mode = "minus_one_to_one" } });
                check(Binding.SameList(get(BindingsDoc.Parse(BindingsDoc.Apply(sample, new Dictionary<string, List<Binding>> { { "CameraPitch", axes } }, b => "x")), "CameraPitch"), axes),
                    "list of axes inserted for a missing action round-trips");

                var shrunk = BindingsDoc.Apply(sample, new Dictionary<string, List<Binding>> { { "Target", L(new[] { t1[1] }) } }, b => "x");
                var ss = BindingsDoc.Parse(shrunk);
                check(Binding.SameList(get(ss, "Target"), L(new[] { t1[1] })) && split(shrunk).Length == b1.Length - 3, "list shrinks back to one line");
                var cleared = BindingsDoc.Apply(sample, new Dictionary<string, List<Binding>> { { "Target", null } }, b => "x");
                check(BindingsDoc.Parse(cleared).Bindings.ContainsKey("Target") && BindingsDoc.Parse(cleared).Bindings["Target"] == null && Regex.IsMatch(cleared, @"^[ \t]*Target = nil,$", RegexOptions.Multiline),
                    "clearing a list writes nil, not an empty list");
                check(grown.EndsWith(sample.Substring(sample.IndexOf(BindingsDoc.Marker))), "detected-devices section untouched by lists");

                string tmp = Path.Combine(Path.GetTempPath(), "ac8-binder-selftest-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(Path.Combine(tmp, "Scripts"));
                    var none = ModFeatures.Detect(tmp);
                    File.WriteAllText(Path.Combine(tmp, "Scripts", "mapper.lua"), "assert(#bindings>0,'Binding list must not be empty: '..action)");
                    File.WriteAllText(Path.Combine(tmp, "Scripts", "weapon_select.lua"), "");
                    var both = ModFeatures.Detect(tmp);
                    check(!none.Lists && !none.WeaponSelect && both.Lists && both.WeaponSelect, "AC8HOTAS feature detection");
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }

                log.AppendLine("HID devices:");
                var q = new ConcurrentQueue<InputEvent>();
                foreach (var d in HidDevice.Enumerate(q))
                {
                    var c = catalog.FirstOrDefault(x => string.Equals(x.Product, d.Product, StringComparison.OrdinalIgnoreCase));
                    log.AppendLine(string.Format("  '{0}' {1} -> {2}  axes=[{3}] buttons={4} hat={5}", d.Name, d.Product, c == null ? "NOT IN CATALOG" : c.Id,
                        string.Join(",", d.Axes.Select(x => x.Token)), d.ButtonCount, d.HasHat));
                    d.Stop();
                }
            }
            catch (Exception ex) { log.AppendLine("EXCEPTION " + ex); failures++; }
            log.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            if (outFile != null) File.WriteAllText(outFile, log.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
