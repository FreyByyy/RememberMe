using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace PastLives
{
    // Passions in the archive are names. Vanilla has three; Vanilla Skills Expanded (and add-ons such as Alpha
    // Skills) adds its own as VSE.Passions.PassionDef and keeps them in SkillRecord.passion as the def's index,
    // numbered at startup in load order. Saves store the def name, so do we: a name survives a different mod
    // list, an index does not. Without VSE everything here falls back to the vanilla enum.
    public static class PassionCompat
    {
        private const string ActiveSuffix = "_Active";   // Alpha Skills' temporary states, e.g. "drunken (active)"

        private static bool resolved;
        private static Type defType;
        private static FieldInfo isBadField;

        private static Type DefType
        {
            get
            {
                if (!resolved)
                {
                    resolved = true;
                    defType = GenTypes.GetTypeInAnyAssembly("VSE.Passions.PassionDef");
                    if (defType == null) return null;
                    isBadField = defType.GetField("isBad");
                    // VSE numbers its passions in this static constructor; make sure it has run.
                    var manager = GenTypes.GetTypeInAnyAssembly("VSE.Passions.PassionManager");
                    if (manager != null) RuntimeHelpers.RunClassConstructor(manager.TypeHandle);
                }
                return defType;
            }
        }

        public static bool VseActive => DefType != null;

        public static string Name(Passion passion)
        {
            var def = DefOf(passion);
            return def != null ? BaseName(def.defName) : passion.ToString();
        }

        // Names from saves, editors and our own files; plain numbers only for the three vanilla values, since any
        // other number was an index in somebody else's mod list.
        public static Passion Parse(string name)
        {
            if (string.IsNullOrEmpty(name)) return Passion.None;
            if (int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return n >= 0 && n <= (int)Passion.Major ? (Passion)n : Passion.None;
            if (DefType != null)
            {
                var def = GenDefDatabase.GetDefSilentFail(DefType, name, false)
                    ?? (name.EndsWith(ActiveSuffix, StringComparison.Ordinal) ? GenDefDatabase.GetDefSilentFail(DefType, name.Substring(0, name.Length - ActiveSuffix.Length), false) : null);
                if (def != null) return (Passion)def.index;
            }
            return Enum.TryParse(name, out Passion p) && Enum.IsDefined(typeof(Passion), p) ? p : Passion.None;
        }

        // A passion worth a head start when a child comes back grown up: not none, and not one of VSE's bad ones (apathy).
        public static bool IsGood(Passion passion)
        {
            if (passion == Passion.None) return false;
            var def = DefOf(passion);
            return def == null || isBadField == null || !(bool)isBadField.GetValue(def);
        }

        private static Def DefOf(Passion passion)
        {
            if (DefType == null) return null;
            foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(DefType))
                if (def.index == (ushort)passion) return def;
            return null;
        }

        // A temporary state comes back as the passion it is a state of.
        private static string BaseName(string defName)
        {
            if (!defName.EndsWith(ActiveSuffix, StringComparison.Ordinal)) return defName;
            var baseName = defName.Substring(0, defName.Length - ActiveSuffix.Length);
            return GenDefDatabase.GetDefSilentFail(DefType, baseName, false) != null ? baseName : defName;
        }
    }
}
