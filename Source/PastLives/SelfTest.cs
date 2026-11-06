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
            s.stories = true;
            s.hardships = true;
            s.showRememberButton = true;
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
            TestStoriesAndHardships(comp, map);
            TestPassions();
            TestNewRoles(comp, map);
            TestRotation(comp, map);
            TestDevMenu();
            TestExpertise(map);

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
            Out("mylist: gizmo=" + (gizmo != null) + " label=" + gizmo?.defaultLabel + " buttonSetting=" + PastLivesMod.Settings.showRememberButton
                + " colonist=" + colonist.IsColonist + " gizmos=" + colonist.GetGizmos().Count());
            bool wasThere = MyList.Contains(colonist);
            MyList.Remember(colonist);
            var entry = PastLivesArchive.MyListEntry(MyList.KeyFor(colonist));
            Out("mylist: remembered=" + (entry != null) + " file=" + (entry != null && File.Exists(entry.SourceFile))
                + " name=" + entry?.DisplayName + " colony=" + entry?.Colony + " traits=" + entry?.Traits.Count + " skills=" + entry?.Skills.Count
                + " forgetGizmo=" + colonist.GetGizmos().OfType<Command_Action>().Any(g => g.icon == MyListTextures.Forget)
                + " excludedInThisWorld=" + (entry != null && comp.AlreadyHere(entry)));
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

        // Every story kind resolves in the current language; scars never cost a part; the dev raid forces pawns.
        private static void TestStoriesAndHardships(GameComponent_PastLives comp, Map map)
        {
            foreach (var arc in DefDatabase<StoryArcDef>.AllDefs)
                foreach (var role in arc.roles)
                {
                    var variant = arc.variants.Count > 0 ? arc.variants[0] : (role == PastRole.DeadReturn ? DeadVariant.Wanderer : DeadVariant.None);
                    var stories = DevActions.LogStories(role, variant, 4, arc);
                    Out("stories " + arc.defName + " as " + role + "/" + variant + ": " + stories.Count
                        + " ok=" + stories.All(t => t.Length > 20 && !t.Contains("ERR") && !t.Contains("[") && !t.Contains("]")));
                }
            Out("stories in entries=" + comp.Entries.Count(e => !string.IsNullOrEmpty(e.story)) + "/" + comp.Entries.Count(e => e.role != PastRole.Leader));

            var test = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            var kinds = new[] { "Cut", "Burn", "Gunshot" }.Select(d => DefDatabase<HediffDef>.GetNamedSilentFail(d)).Where(d => d != null).ToList();
            int partsBefore = test.health.hediffSet.GetNotMissingParts().Count();
            int scars = Hardship.AddScars(test, kinds, 3);
            int permanent = test.health.hediffSet.hediffs.Count(h => h is Hediff_Injury i && i.IsPermanent());
            int traitsBefore = test.story.traits.allTraits.Count;
            bool trait = Hardship.TryGainTrait(test, Hardship.TraitsFor(PastRole.Raider));
            Out("hardship: scars=" + scars + " permanent=" + permanent + " partsKept=" + (test.health.hediffSet.GetNotMissingParts().Count() == partsBefore)
                + " trait=" + trait + " traits=" + traitsBefore + "->" + test.story.traits.allTraits.Count + " dead=" + test.Dead
                + " onUnhittableParts=" + test.health.hediffSet.hediffs.Count(h => h is Hediff_Injury && h.Part != null && h.Part.coverageAbs <= 0f));

            var raid = DefDatabase<IncidentDef>.GetNamedSilentFail("RaidEnemy");
            if (raid != null)
            {
                int before = comp.Entries.Count;
                DevForce.Run(null, 3, () =>
                {
                    var parms = StorytellerUtility.DefaultParmsNow(raid.category, map);
                    parms.forced = true;
                    raid.Worker.TryExecute(parms);
                });
                Out("dev raid x3: newPastColonists=" + (comp.Entries.Count - before) + " forceCleared=" + !DevForce.Active);
            }
        }

        // Every passion survives name -> index -> name; with Vanilla Skills Expanded this covers its passions too.
        private static void TestPassions()
        {
            int ok = 0, total = 0;
            var bad = new System.Collections.Generic.List<string>();
            foreach (Passion p in PassionCompat.VseActive
                ? Enumerable.Range(0, 256).Select(i => (Passion)i).Where(p => PassionCompat.Name(p) != p.ToString())
                : new[] { Passion.None, Passion.Minor, Passion.Major })
            {
                total++;
                var name = PassionCompat.Name(p);
                var back = PassionCompat.Parse(name);
                if (back == p || name.EndsWith("_Active") || PassionCompat.Name(back) == name) ok++;
                else bad.Add(name);
            }
            Out("passions: vse=" + PassionCompat.VseActive + " roundTrip=" + ok + "/" + total + (bad.Count > 0 ? " bad=" + string.Join(",", bad) : "")
                + " unknownName=" + PassionCompat.Parse("NoSuchPassion") + " foreignIndex=" + PassionCompat.Parse("17"));
        }

        // 1.2: every quest of the role table, the wild man, a cryptosleep casket and an enemy settlement's people.
        private static void TestNewRoles(GameComponent_PastLives comp, Map map)
        {
            // The ghoul pod quest first turns a starting pawn the player left behind into its ghoul; with those
            // gone it generates one, which is where a dead colonist can take the place.
            var leftBehind = Find.WorldPawns.GetPawnsBySituation(RimWorld.Planet.WorldPawnSituation.StartingPawnLeftBehind).ToList();
            foreach (var p in leftBehind) Find.WorldPawns.RemovePawn(p);
            Out("left-behind starting pawns removed: " + leftBehind.Count);
            foreach (var def in DefDatabase<QuestRoleDef>.AllDefs)
                foreach (var q in def.quests)
                {
                    var qd = DefDatabase<QuestScriptDef>.GetNamedSilentFail(q);
                    if (qd == null) { Out("quest " + q + ": not loaded"); continue; }
                    int before = comp.Entries.Count;
                    Quest quest;
                    try { quest = DevActions.GenerateQuest(qd, map); }
                    catch (Exception ex) { Out("quest " + q + ": EXCEPTION " + ex); continue; }
                    var added = comp.Entries.Skip(before).ToList();
                    Out("quest " + q + ": generated=" + (quest != null) + " new=" + added.Count + " expected=" + def.role
                        + (def.variant != DeadVariant.None ? "/" + def.variant : "") + " got=" + string.Join(",", added.Select(e => e.role + (e.variant != DeadVariant.None ? "/" + e.variant : "")))
                        + " story=" + added.All(e => !string.IsNullOrEmpty(e.story) || PawnOf(e)?.DevelopmentalStage.Baby() == true)
                        + " ghoul=" + added.Count(e => PawnOf(e)?.IsMutant == true));
                }

            int wildBefore = comp.Entries.Count;
            var wild = DefDatabase<IncidentDef>.GetNamedSilentFail("WildManWandersIn");
            bool wildOk = false;
            if (wild != null)
            {
                var parms = StorytellerUtility.DefaultParmsNow(wild.category, map);
                parms.forced = true;
                wildOk = wild.Worker.TryExecute(parms);
            }
            Out("wild man: executed=" + wildOk + " got=" + string.Join(",", comp.Entries.Skip(wildBefore).Select(e => e.role + " faction=" + (PawnOf(e)?.Faction?.Name ?? "none"))));

            int casketBefore = comp.Entries.Count;
            var casket = DevActions.SpawnCasket(map) as Building_Casket;
            var sleepers = comp.Entries.Skip(casketBefore).ToList();
            bool notifiedClosed = sleepers.Any(e => e.notified);
            string inside = casket == null ? "-" : string.Join(",", casket.GetDirectlyHeldThings().Select(t => t.LabelShort));
            casket?.Open();
            comp.CheckSpawned();
            Out("casket: spawned=" + (casket != null) + " inside=" + inside + " got=" + string.Join(",", sleepers.Select(e => e.role.ToString()))
                + " pawnSpawned=" + string.Join(",", sleepers.Select(e => PawnOf(e)?.Spawned.ToString() ?? "?"))
                + " letterWhileClosed=" + notifiedClosed + " letterAfterOpen=" + sleepers.All(e => e.notified));

            var settlement = Find.WorldObjects.Settlements.FirstOrDefault(x => x.Faction != null && !x.Faction.IsPlayer && x.Faction.def.humanlikeFaction
                && x.Faction.HostileTo(Faction.OfPlayer) && !x.HasMap);
            if (settlement == null) { Out("settlement: none hostile"); return; }
            int sBefore = comp.Entries.Count;
            Map sMap = null;
            try { sMap = GetOrGenerateMapUtility.GetOrGenerateMap(settlement.Tile, null); }
            catch (Exception ex) { Out("settlement: EXCEPTION " + ex); }
            var defenders = comp.Entries.Skip(sBefore).ToList();
            comp.CheckSpawned();
            Out("settlement " + settlement.Label + " (" + settlement.Faction.def.defName + "): map=" + (sMap != null) + " got=" + string.Join(",", defenders.Select(e => e.role.ToString()))
                + " home=" + PastStories.HomeOf(settlement.Faction) + " letterWithoutColonists=" + defenders.Any(e => e.notified)
                + " stories=" + string.Join(" || ", defenders.Select(e => e.story)));
            if (sMap != null) Current.Game.DeinitAndRemoveMap(sMap, false);
        }

        public static void TestDevMenu()
        {
            var m = typeof(DevActions).GetMethod("CallUpAppearance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var nodes = m?.Invoke(null, null) as System.Collections.Generic.List<LudeonTK.DebugActionNode>;
            Out("dev menu: appearances=" + (nodes?.Count ?? -1) + " labels=" + (nodes == null ? "" : string.Join(" | ", nodes.Select(n => n.label))));
        }

        // A story just told is not told again right away and comes back after a few others.
        private static void TestRotation(GameComponent_PastLives comp, Map map)
        {
            var pawn = map.mapPawns.FreeColonists.FirstOrDefault();
            var picks = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 30; i++) picks.Add(PastStories.PickArc(PastRole.Raider, DeadVariant.None, pawn)?.defName);
            int repeats = 0;
            for (int i = 1; i < picks.Count; i++) if (picks[i] == picks[i - 1]) repeats++;
            var told = picks.Where(x => x != null).Distinct().Count();
            var last = DefDatabase<StoryArcDef>.GetNamedSilentFail(picks.LastOrDefault() ?? "");
            Out("rotation: picks=" + picks.Count + " distinct=" + told + " immediateRepeats=" + repeats
                + " lastWeight=" + (last != null ? comp.ArcWeight(last) : -1f));
        }

        // Vanilla Skills Expanded: expertise and passions, with or without VSE loaded.
        private static void TestExpertise(Map map)
        {
            var colonist = map.mapPawns.FreeColonists.FirstOrDefault();
            if (colonist == null) return;
            var a = MyList.Snapshot(colonist);
            a.Key = "selftest#expertise";
            a.Skills.Clear();
            a.Skills.Add(new ArchivePawn.SkillEntry { Def = "Shooting", Level = 15, Passion = "VSE_Critical" });
            a.Skills.Add(new ArchivePawn.SkillEntry { Def = "Melee", Level = 8, Passion = "VSE_Apathy" });
            a.Skills.Add(new ArchivePawn.SkillEntry { Def = "Cooking", Level = 6, Passion = "AS_DrunkenPassion_Active" });
            a.Skills.Add(new ArchivePawn.SkillEntry { Def = "Plants", Level = 6, Passion = "SomeModsBadPassion", Bad = true });
            a.Skills.Add(new ArchivePawn.SkillEntry { Def = "Mining", Level = 20, Passion = "VSE_Natural" });
            a.Expertise.Clear();
            a.Expertise.Add(new ArchivePawn.ExpertiseEntry { Def = "CloseQuarters", Level = 10 });
            a.Expertise.Add(new ArchivePawn.ExpertiseEntry { Def = "Precision", Level = 3 });
            a.Expertise.Add(new ArchivePawn.ExpertiseEntry { Def = "Driller", Level = 12 });

            var back = ArchivePawn.FromXml(a.ToXml());
            Out("expertise xml: expertise=" + back.Expertise.Count + "/" + a.Expertise.Count + " badFlag=" + back.Skills.Any(x => x.Bad));

            var pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            PawnRecipe.Apply(pawn, back, keepBackstories: false, grownUp: false);
            string Sk(string d)
            {
                var r = pawn.skills.skills.FirstOrDefault(x => x.def.defName == d);
                return r == null ? "-" : r.levelInt + "/" + PassionCompat.Name(r.passion);
            }
            var read = ExpertiseCompat.Read(pawn);
            Out("expertise: vse=" + ExpertiseCompat.Active + " shooting=" + Sk("Shooting") + " melee=" + Sk("Melee") + " cooking=" + Sk("Cooking")
                + " plants=" + Sk("Plants") + " mining=" + Sk("Mining")
                + " restored=" + string.Join(",", read.Select(x => x.Def + ":" + x.Level))
                + " skillOf(CloseQuarters)=" + ExpertiseCompat.SkillOf("CloseQuarters"));
        }

        private static Pawn PawnOf(PastLifeEntry e) => PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.FirstOrDefault(p => p.ThingID == e.thingId);

        private static void Out(string msg) => Log.Message("[PastLives][selftest] " + msg);
    }
}
