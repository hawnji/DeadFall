using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Rebindable keyboard and mouse controls (Settings › Keybinds). Each action has one key or mouse button, stored
    /// in <see cref="GameSettings.KeyBindings"/> as "Id=path" entries using Input System paths ("&lt;Keyboard&gt;/w",
    /// "&lt;Mouse&gt;/leftButton"). Gamepad bindings, Esc (pause) and the 1-4 weapon keys stay fixed.
    /// </summary>
    public static class KeyBindings
    {
        public struct Entry
        {
            public string Id, Label, Default;
            public Entry(string id, string label, string def) { Id = id; Label = label; Default = def; }
        }

        /// <summary>True while Settings waits for a new key, so Esc cancels the rebind instead of unpausing.</summary>
        public static bool Capturing;

        public static readonly Entry[] All =
        {
            new Entry("MoveUp", "Move forward", "<Keyboard>/w"),
            new Entry("MoveDown", "Move back", "<Keyboard>/s"),
            new Entry("MoveLeft", "Move left", "<Keyboard>/a"),
            new Entry("MoveRight", "Move right", "<Keyboard>/d"),
            new Entry("Sprint", "Sprint", "<Keyboard>/leftShift"),
            new Entry("Jump", "Jump", "<Keyboard>/space"),
            new Entry("Crouch", "Crouch", "<Keyboard>/leftCtrl"),
            new Entry("Fire", "Fire", "<Mouse>/leftButton"),
            new Entry("Aim", "Aim down sights", "<Mouse>/rightButton"),
            new Entry("Reload", "Reload", "<Keyboard>/r"),
            new Entry("Melee", "Melee", "<Keyboard>/v"),
            new Entry("Interact", "Interact / use", "<Keyboard>/f"),
            new Entry("Ability", "Class ability", "<Keyboard>/q"),
            new Entry("Grenade", "Throw grenade", "<Keyboard>/e"),
        };

        /// <summary>The path bound to <paramref name="id"/> in these settings (the default when unset).</summary>
        public static string Path(GameSettings s, string id)
        {
            string found = Find(s, id);
            if (found != null) return found;
            foreach (Entry e in All) if (e.Id == id) return e.Default;
            return null;
        }

        /// <summary>True when the action still uses its default key.</summary>
        public static bool IsDefault(GameSettings s, string id)
        {
            string found = Find(s, id);
            if (found == null) return true;
            foreach (Entry e in All) if (e.Id == id) return found == e.Default;
            return true;
        }

        private static string Find(GameSettings s, string id)
        {
            if (s == null || s.KeyBindings == null) return null;
            string prefix = id + "=";
            foreach (string kv in s.KeyBindings)
                if (kv != null && kv.StartsWith(prefix, StringComparison.Ordinal) && kv.Length > prefix.Length) return kv.Substring(prefix.Length);
            return null;
        }

        /// <summary>
        /// Binds <paramref name="id"/> to <paramref name="path"/>. An action that already used that key takes this
        /// action's old key instead (a swap), so no two actions ever share a key.
        /// </summary>
        public static void Set(GameSettings s, string id, string path)
        {
            if (s == null || string.IsNullOrEmpty(path)) return;
            string old = Path(s, id);
            var map = new Dictionary<string, string>();
            foreach (Entry e in All) map[e.Id] = Path(s, e.Id);
            foreach (Entry e in All)
                if (e.Id != id && map[e.Id] == path) map[e.Id] = old;
            map[id] = path;
            var list = new List<string>();
            foreach (Entry e in All) if (map[e.Id] != e.Default) list.Add(e.Id + "=" + map[e.Id]);
            s.KeyBindings = list.ToArray();
        }

        /// <summary>Short name for a binding path: "W", "Left Shift", "Mouse 4".</summary>
        public static string Display(string path)
        {
            if (string.IsNullOrEmpty(path)) return "-";
            if (path.StartsWith("<Mouse>/", StringComparison.Ordinal))
            {
                switch (path.Substring(8))
                {
                    case "leftButton": return "Left Mouse";
                    case "rightButton": return "Right Mouse";
                    case "middleButton": return "Middle Mouse";
                    case "backButton": return "Mouse 4";
                    case "forwardButton": return "Mouse 5";
                }
            }
            int slash = path.LastIndexOf('/');
            string key = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (key)
            {
                case "leftCtrl": return "Left Ctrl";
                case "rightCtrl": return "Right Ctrl";
                case "backquote": return "`";
                case "minus": return "-";
                case "equals": return "=";
                case "leftBracket": return "[";
                case "rightBracket": return "]";
                case "semicolon": return ";";
                case "quote": return "'";
                case "comma": return ",";
                case "period": return ".";
                case "slash": return "/";
                case "backslash": return "\\";
            }
            if (key.Length == 1) return key.ToUpperInvariant();
            // camelCase to Title Case: "leftShift" -> "Left Shift", "numpad5" -> "Numpad5".
            var sb = new System.Text.StringBuilder(key.Length + 4);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i == 0) sb.Append(char.ToUpperInvariant(c));
                else if (char.IsUpper(c)) { sb.Append(' '); sb.Append(c); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The Input System path for an IMGUI key press, or null for keys that can't be bound (Esc, 1-4).</summary>
        public static string FromKeyCode(KeyCode k)
        {
            if (k >= KeyCode.A && k <= KeyCode.Z) return "<Keyboard>/" + (char)('a' + (k - KeyCode.A));
            if (k >= KeyCode.Alpha5 && k <= KeyCode.Alpha9) return "<Keyboard>/" + (char)('0' + (k - KeyCode.Alpha0));
            if (k == KeyCode.Alpha0) return "<Keyboard>/0";
            if (k >= KeyCode.F1 && k <= KeyCode.F12) return "<Keyboard>/f" + (1 + (k - KeyCode.F1));
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "<Keyboard>/numpad" + (k - KeyCode.Keypad0);
            switch (k)
            {
                case KeyCode.Space: return "<Keyboard>/space";
                case KeyCode.Tab: return "<Keyboard>/tab";
                case KeyCode.Return: return "<Keyboard>/enter";
                case KeyCode.Backspace: return "<Keyboard>/backspace";
                case KeyCode.CapsLock: return "<Keyboard>/capsLock";
                case KeyCode.LeftShift: return "<Keyboard>/leftShift";
                case KeyCode.RightShift: return "<Keyboard>/rightShift";
                case KeyCode.LeftControl: return "<Keyboard>/leftCtrl";
                case KeyCode.RightControl: return "<Keyboard>/rightCtrl";
                case KeyCode.LeftAlt: return "<Keyboard>/leftAlt";
                case KeyCode.RightAlt: return "<Keyboard>/rightAlt";
                case KeyCode.UpArrow: return "<Keyboard>/upArrow";
                case KeyCode.DownArrow: return "<Keyboard>/downArrow";
                case KeyCode.LeftArrow: return "<Keyboard>/leftArrow";
                case KeyCode.RightArrow: return "<Keyboard>/rightArrow";
                case KeyCode.BackQuote: return "<Keyboard>/backquote";
                case KeyCode.Minus: return "<Keyboard>/minus";
                case KeyCode.Equals: return "<Keyboard>/equals";
                case KeyCode.LeftBracket: return "<Keyboard>/leftBracket";
                case KeyCode.RightBracket: return "<Keyboard>/rightBracket";
                case KeyCode.Semicolon: return "<Keyboard>/semicolon";
                case KeyCode.Quote: return "<Keyboard>/quote";
                case KeyCode.Comma: return "<Keyboard>/comma";
                case KeyCode.Period: return "<Keyboard>/period";
                case KeyCode.Slash: return "<Keyboard>/slash";
                case KeyCode.Backslash: return "<Keyboard>/backslash";
                case KeyCode.Insert: return "<Keyboard>/insert";
                case KeyCode.Delete: return "<Keyboard>/delete";
                case KeyCode.Home: return "<Keyboard>/home";
                case KeyCode.End: return "<Keyboard>/end";
                case KeyCode.PageUp: return "<Keyboard>/pageUp";
                case KeyCode.PageDown: return "<Keyboard>/pageDown";
                default: return null;
            }
        }

        /// <summary>The Input System path for an IMGUI mouse button (0 left, 1 right, 2 middle, 3 back, 4 forward).</summary>
        public static string FromMouseButton(int button)
        {
            switch (button)
            {
                case 0: return "<Mouse>/leftButton";
                case 1: return "<Mouse>/rightButton";
                case 2: return "<Mouse>/middleButton";
                case 3: return "<Mouse>/backButton";
                case 4: return "<Mouse>/forwardButton";
                default: return null;
            }
        }
    }
}
