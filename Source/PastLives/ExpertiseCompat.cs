using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace PastLives
{
    // Vanilla Skills Expanded expertise, by reflection so the mod is never a dependency. With VSE loaded the
    // archived expertise comes back as it was. Without it, each skill gets a fifth of its best expertise level
    // on top (rounded down, at most 20): an aiming expert of level 10 with Shooting 15 comes back with 17.
    public static class ExpertiseCompat
    {
        public const float FallbackFactor = 0.2f;

        // Expertise of VSE and of the Vanilla Expanded mods and add-ons that bring their own, for games without VSE.
        private static readonly Dictionary<string, string> KnownSkills = new Dictionary<string, string>
        {
            // Vanilla Skills Expanded
            { "Precision", "Shooting" }, { "CloseQuarters", "Shooting" }, { "Gunner", "Shooting" }, { "VSE_Sniping", "Shooting" },
            { "Sharp", "Melee" }, { "Blunt", "Melee" }, { "VSE_Striking", "Melee" },
            { "Tamer", "Animals" }, { "Rancher", "Animals" }, { "Hunter", "Animals" }, { "Trainer", "Animals" },
            { "QualityExpert", "Artistic" }, { "QuantityExpert", "Artistic" },
            { "Flooring", "Construction" }, { "Repairman", "Construction" }, { "Architect", "Construction" }, { "Foreman", "Construction" }, { "VSE_Smoothing", "Construction" },
            { "Butcher", "Cooking" }, { "DrugChef", "Cooking" }, { "TopChef", "Cooking" }, { "IndustrialChef", "Cooking" },
            { "MechanoidExpert", "Crafting" }, { "Tailor", "Crafting" }, { "Weaponsmith", "Crafting" }, { "IndustrialProcessExpert", "Crafting" },
            { "Surgeon", "Medicine" }, { "BattlefieldMedic", "Medicine" }, { "InfectiousDiseaseExpert", "Medicine" }, { "VSE_Operating", "Medicine" },
            { "Driller", "Mining" }, { "OreExpert", "Mining" }, { "Tunneller", "Mining" }, { "Geologist", "Mining" },
            { "Pharmacologist", "Intellectual" }, { "Researcher", "Intellectual" },
            { "Forager", "Plants" }, { "HarvesterCareful", "Plants" }, { "GreenThumb", "Plants" },
            { "Warden", "Social" }, { "Negotiator", "Social" },
            // Vanilla Cooking Expanded, Vanilla Fishing Expanded
            { "VCE_GourmetChef", "Cooking" }, { "VCE_FlavourMaster", "Cooking" },
            { "VCEF_Swiftcasting", "Animals" }, { "VCEF_Catchmastery", "Animals" }, { "VCEF_Aquabounty", "Animals" },
            // Alpha Skills
            { "AS_Blasting", "Shooting" }, { "AS_Mortaring", "Shooting" },
            { "AS_Pummeling", "Melee" }, { "AS_Cleaving", "Melee" }, { "AS_Evading", "Melee" }, { "AS_Enduring", "Melee" },
            { "AS_VacuumResilience", "Melee" }, { "AS_RangedDodging", "Melee" },
            { "AS_CraftingQuality", "Crafting" }, { "AS_CraftingYield", "Crafting" }, { "AS_Smelting", "Crafting" },
            { "AS_Panning", "Mining" }, { "AS_Salvaging", "Mining" },
            { "AS_Trafficking", "Social" }, { "AS_Influencing", "Social" }, { "AS_Mindfulness", "Social" },
            { "AS_Psycasting", "Intellectual" }, { "AS_Quelling", "Intellectual" }, { "AS_Piloting", "Intellectual" },
        };

        private static bool resolved;
        private static Type defType;
        private static FieldInfo defSkill, recordDef;
        private static PropertyInfo recordLevel, trackerAll;
        private static MethodInfo trackerOf, trackerClear, trackerAdd;

        public static bool Active
        {
            get
            {
                if (!resolved)
                {
                    resolved = true;
                    try { Resolve(); }
                    catch (Exception ex) { defType = null; Log.Warning("[PastLives] Vanilla Skills Expanded expertise not understood: " + ex.Message); }
                }
                return defType != null;
            }
        }

        private static void Resolve()
        {
            var type = GenTypes.GetTypeInAnyAssembly("VSE.Expertise.ExpertiseDef");
            var trackers = GenTypes.GetTypeInAnyAssembly("VSE.ExpertiseTrackers");
            var tracker = GenTypes.GetTypeInAnyAssembly("VSE.ExpertiseTracker");
            var record = GenTypes.GetTypeInAnyAssembly("VSE.ExpertiseRecord");
            if (type == null || trackers == null || tracker == null || record == null) return;
            defSkill = type.GetField("skill");
            recordDef = record.GetField("def");
            recordLevel = record.GetProperty("Level");
            trackerAll = tracker.GetProperty("AllExpertise");
            trackerOf = trackers.GetMethod("Expertise", new[] { typeof(Pawn) });
            trackerClear = tracker.GetMethod("ClearExpertise");
            trackerAdd = tracker.GetMethod("AddExpertise");
            if (defSkill == null || recordDef == null || recordLevel == null || trackerAll == null || trackerOf == null || trackerClear == null || trackerAdd == null) return;
            defType = type;
        }

        public static List<ArchivePawn.ExpertiseEntry> Read(Pawn pawn)
        {
            var result = new List<ArchivePawn.ExpertiseEntry>();
            if (!Active || pawn?.skills == null) return result;
            try
            {
                if (trackerAll.GetValue(trackerOf.Invoke(null, new object[] { pawn })) is IEnumerable list)
                    foreach (var r in list)
                        if (recordDef.GetValue(r) is Def def)
                            result.Add(new ArchivePawn.ExpertiseEntry { Def = def.defName, Level = (int)recordLevel.GetValue(r) });
            }
            catch (Exception ex) { Log.Warning("[PastLives] could not read expertise of " + pawn + ": " + ex.Message); }
            return result;
        }

        // With VSE: the archived expertise replaces whatever the pawn was generated with. Without it nothing
        // happens here; see SkillBonus.
        public static void Apply(Pawn pawn, ArchivePawn a)
        {
            if (!Active || a.Expertise.Count == 0 || pawn?.skills == null) return;
            var tracker = trackerOf.Invoke(null, new object[] { pawn });
            if (tracker == null) return;
            trackerClear.Invoke(tracker, null);
            foreach (var x in a.Expertise)
            {
                var def = GenDefDatabase.GetDefSilentFail(defType, x.Def, false);
                if (def == null) continue;
                trackerAdd.Invoke(tracker, new object[] { def });
                if (trackerAll.GetValue(tracker) is IList list && list.Count > 0) recordLevel.SetValue(list[list.Count - 1], x.Level);
            }
        }

        // Without VSE: levels a skill gains from the best expertise that belonged to it.
        public static int SkillBonus(ArchivePawn a, string skill)
        {
            if (Active) return 0;
            int best = 0;
            foreach (var x in a.Expertise)
                if (SkillOf(x.Def) == skill && x.Level > best) best = x.Level;
            return Mathf.FloorToInt(best * FallbackFactor);
        }

        public static string SkillOf(string expertise)
        {
            if (string.IsNullOrEmpty(expertise)) return null;
            if (Active && GenDefDatabase.GetDefSilentFail(defType, expertise, false) is Def def && defSkill.GetValue(def) is SkillDef skill)
                return skill.defName;
            return KnownSkills.TryGetValue(expertise, out var s) ? s : null;
        }
    }
}
