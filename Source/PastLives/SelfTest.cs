using System;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PastLives
{
    // Automated smoke test, only active with the -pastlives-selftest launch argument (use with -quicktest).
    // Forces every kind of appearance once, checks the restored pawns against the archive, round-trips a
    // save, writes everything to the log and quits the game.
    public static class SelfTest
    {
        public static readonly bool Active = GenCommandLine.CommandLineArgPassed("pastlives-selftest");

        private static float startAt = -1f;
        private static bool done;

        public static void Update()
        {
            if (!Active || done || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null) return;
            if (startAt < 0f) { startAt = Time.realtimeSinceStartup + 5f; return; }
            if (Time.realtimeSinceStartup < startAt) return;
            done = true;
            try { Run(); }
            catch (Exception ex) { Log.Error("[PastLives][selftest] crashed: " + ex); }
            Log.Message("[PastLives][selftest] DONE");
            Root.Shutdown();
        }

        // In memory only: the settings file is never written during a self-test.
        public static void ApplyOverrides()
        {
            var s = PastLivesMod.Settings;
            s.eventChance = 1f;
            s.leaderChance = 1f;
            s.cooldownDays = 0f;
        }

        private static void Run()
        {
            var comp = GameComponent_PastLives.Instance;
            var map = Find.CurrentMap;
            Out("archive=" + PastLivesArchive.Count + " entriesAfterWorldGen=" + comp.Entries.Count);

            foreach (var name in new[] { "RaidEnemy", "VisitorGroup", "TraderCaravanArrival", "WandererJoin",
                         "PastLives_DeadReturns", "PastLives_DeadReturns_CreepJoiner", "PastLives_DeadReturns_Shambler" })
            {
                var def = DefDatabase<IncidentDef>.GetNamedSilentFail(name);
                if (def == null) { Out(name + ": def missing"); continue; }
                int before = comp.Entries.Count;
                bool ok;
                try
                {
                    var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
                    parms.forced = true;
                    ok = def.Worker.TryExecute(parms);
                }
                catch (Exception ex) { Out(name + ": EXCEPTION " + ex); continue; }
                Out(name + ": executed=" + ok + " newPastColonists=" + (comp.Entries.Count - before));
            }

            var faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer && f.def.humanlikeFaction && f.leader != null && !f.Hidden);
            if (faction != null)
            {
                int before = comp.Entries.Count;
                faction.TryGenerateNewLeader();
                Out("leader change in " + faction.Name + ": newPastColonists=" + (comp.Entries.Count - before));
            }

            comp.CheckSpawned();

            foreach (var e in comp.Entries)
            {
                var pawn = PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.FirstOrDefault(p => p.ThingID == e.thingId);
                var a = PastLivesArchive.All.FirstOrDefault(p => p.Key == e.key);
                if (pawn == null || a == null) { Out("entry " + e.name + " (" + e.role + "): pawn or archive record not found"); continue; }
                int traitsWanted = a.Traits.Count(t => DefDatabase<TraitDef>.GetNamedSilentFail(t.Def) != null);
                int traitsHave = a.Traits.Count(t => pawn.story?.traits?.allTraits.Any(x => x.def.defName == t.Def && x.Degree == t.Degree) == true);
                int skillsOk = a.Skills.Count(sk => pawn.skills?.skills.Any(r => r.def.defName == sk.Def && r.levelInt >= sk.Level) == true);
                Out($"entry {e.role}/{e.variant}: '{pawn.Name?.ToStringFull}' vs archive '{a.DisplayName}' colony={a.Colony} " +
                    $"faction={pawn.Faction?.Name} spawned={pawn.Spawned} dead={pawn.Dead} mutant={pawn.IsMutant} age={pawn.ageTracker.AgeBiologicalYears} " +
                    $"gender={pawn.gender}/{a.Gender} traits={traitsHave}/{traitsWanted} skills>={skillsOk}/{a.Skills.Count} " +
                    $"child={pawn.story?.Childhood?.defName}/{a.Childhood} adult={pawn.story?.Adulthood?.defName}/{a.Adulthood} " +
                    $"xeno={pawn.genes?.XenotypeLabel}/{a.Xenotype} hair={pawn.story?.hairDef?.defName}/{a.HairDef} notified={e.notified} " +
                    $"inspect={pawn.Spawned && pawn.GetInspectString().Contains(a.Colony)}");
            }
            Out("letters=" + string.Join(" | ", Find.LetterStack.LettersListForReading.Select(l => l.Label.ToString())));

            // Save round trip of the game component.
            const string saveName = "PastLives_SelfTest";
            GameDataSaveLoader.SaveGame(saveName);
            var path = GenFilePaths.FilePathForSavedGame(saveName);
            var text = File.Exists(path) ? File.ReadAllText(path) : "";
            Out("save: exists=" + File.Exists(path) + " hasComponent=" + text.Contains("PastLives.GameComponent_PastLives")
                + " entriesSaved=" + (text.Split(new[] { "<thingId>" }, StringSplitOptions.None).Length - 1));
            if (File.Exists(path)) File.Delete(path);
        }

        private static void Out(string msg) => Log.Message("[PastLives][selftest] " + msg);
    }
}
