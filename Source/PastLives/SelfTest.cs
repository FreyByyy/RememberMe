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
            s.importPawnEditor = true;
            s.importCharacterEditor = true;
        }

        private static void Run()
        {
            var comp = GameComponent_PastLives.Instance;
            var map = Find.CurrentMap;
            Out("saves=" + PastLivesArchive.SavedColonists.Count() + " myList=" + PastLivesArchive.CountMyList
                + " pawnEditor=" + PastLivesArchive.CountSource(ArchivePawn.SourcePawnEditor)
                + " characterEditor=" + PastLivesArchive.CountSource(ArchivePawn.SourceCharacterEditor)
                + " eligible=" + PastLivesArchive.All.Count() + " entriesAfterWorldGen=" + comp.Entries.Count
                + " listLeaders=" + comp.Entries.Count(e => e.role == PastRole.Leader && PastLivesArchive.All.Any(p => p.Key == e.key && p.IsListPawn)));

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

            TestMyList(comp, map);

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
                    $"src={a.Source} inspect={(pawn.Spawned ? pawn.GetInspectString().Split('\n').LastOrDefault() : "-")}");
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

        // Remember / Forget on a real colonist, the gizmo, "only my list" and the "saved" letter texts.
        // Leaves the player's list folder exactly as it found it.
        private static void TestMyList(GameComponent_PastLives comp, Map map)
        {
            var colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist == null) { Out("mylist: no colonist"); return; }
            var gizmo = colonist.GetGizmos().OfType<Command_Action>().FirstOrDefault(g => g.icon == MyListTextures.Remember);
            Out("mylist: gizmo=" + (gizmo != null) + " label=" + gizmo?.defaultLabel);
            bool wasThere = MyList.Contains(colonist);
            MyList.Remember(colonist);
            var entry = PastLivesArchive.MyListEntry(MyList.KeyFor(colonist));
            Out("mylist: remembered=" + (entry != null) + " file=" + (entry != null && File.Exists(entry.SourceFile))
                + " name=" + entry?.DisplayName + " colony=" + entry?.Colony + " traits=" + entry?.Traits.Count + " skills=" + entry?.Skills.Count
                + " forgetGizmo=" + colonist.GetGizmos().OfType<Command_Action>().Any(g => g.icon == MyListTextures.Forget));
            string file = entry?.SourceFile;
            if (!wasThere) MyList.Forget(colonist);
            Out("mylist: forgotten=" + (PastLivesArchive.MyListEntry(MyList.KeyFor(colonist)) == null) + " fileGone=" + (file == null || !File.Exists(file)));

            var s = PastLivesMod.Settings;
            s.onlyMyList = true;
            Out("onlyMyList: eligible=" + PastLivesArchive.All.Count() + " allFromList=" + PastLivesArchive.All.All(p => p.IsListPawn));
            var def = IncidentDefOf.VisitorGroup;
            int before = comp.Entries.Count;
            var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
            parms.forced = true;
            def.Worker.TryExecute(parms);
            var added = comp.Entries.Skip(before).ToList();
            Out("onlyMyList visitor: new=" + added.Count + " fromList=" + added.All(e => PastLivesArchive.All.Any(p => p.Key == e.key))
                + " sources=" + string.Join(",", added.Select(e => PastLivesArchive.All.FirstOrDefault(p => p.Key == e.key)?.Source)));
            s.onlyMyList = false;
        }

        private static void Out(string msg) => Log.Message("[PastLives][selftest] " + msg);
    }
}
