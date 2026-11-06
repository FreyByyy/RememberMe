using System;
using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace PastLives
{
    // Debug menu, category "Remember Me?". Everything here bypasses chances, cooldown and settings.
    public static class DevActions
    {
        private const string Category = "Remember Me?";

        // How each appearance is called up: an incident, a quest (prefix "quest:"), our own casket ("casket") or the
        // people of an enemy settlement ("settlement"). Every quest of the QuestRoleDef table is listed, so a quest
        // added there shows up here too; quests of missing DLCs are left out.
        private static readonly (string label, string how)[] FixedLivingRoles =
        {
            ("wanderer who joins", "WandererJoin"),
            ("visitor", "VisitorGroup"),
            ("trader", "TraderCaravanArrival"),
            ("raider", "RaidEnemy"),
            ("wild man or woman", "WildManWandersIn"),
            ("sleeper in a cryptosleep casket", "casket"),
            ("inhabitant of an enemy settlement (generates its map)", "settlement"),
        };

        private static readonly (string label, string how)[] FixedDeadRoles =
        {
            ("dead: wanderer", "PastLives_DeadReturns"),
            ("dead: creepjoiner", "PastLives_DeadReturns_CreepJoiner"),
            ("dead: shambler", "PastLives_DeadReturns_Shambler"),
        };

        private static IEnumerable<(string label, string how)> QuestRoles(bool dead)
        {
            foreach (var def in DefDatabase<QuestRoleDef>.AllDefs)
            {
                if ((def.role == PastRole.DeadReturn) != dead) continue;
                foreach (var q in def.quests)
                    if (DefDatabase<QuestScriptDef>.GetNamedSilentFail(q) != null)
                        yield return ((dead ? "dead: " + def.variant.ToString().ToLowerInvariant() : def.role.ToString().ToLowerInvariant()) + " - quest " + q, "quest:" + q);
            }
        }

        private static IEnumerable<(string label, string how)> LivingRoles => FixedLivingRoles.Concat(QuestRoles(dead: false));
        private static IEnumerable<(string label, string how)> DeadRoles => FixedDeadRoles.Concat(QuestRoles(dead: true));

        // Every kind of appearance in one list, each with a random person from the archive.
        [DebugAction(Category, "Call up an appearance (any kind)...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> CallUpAppearance()
        {
            var list = RoleNodes(null, living: true, dead: true).ToList();
            return list;
        }

        [DebugAction(Category, "Spawn past colonist...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> SpawnPastColonist()
        {
            var list = new List<DebugActionNode>();
            var random = new DebugActionNode("(random person)");
            foreach (var node in RoleNodes(null, living: true, dead: true)) random.AddChild(node);
            list.Add(random);
            foreach (var colony in PastLivesArchive.All.GroupBy(p => p.HasColony ? p.Colony : "(no colony)").OrderBy(g => g.Key))
            {
                var colonyNode = new DebugActionNode(colony.Key + " (" + colony.Count() + ")");
                foreach (var p in colony.OrderBy(p => p.DisplayName))
                {
                    var pawnNode = new DebugActionNode(p.DisplayName + (p.Dead ? " †" : "") + " [" + p.Source + "]");
                    foreach (var node in RoleNodes(p, living: !p.Dead, dead: p.Dead)) pawnNode.AddChild(node);
                    colonyNode.AddChild(pawnNode);
                }
                list.Add(colonyNode);
            }
            return list;
        }

        private static IEnumerable<DebugActionNode> RoleNodes(ArchivePawn p, bool living, bool dead)
        {
            if (living)
                foreach (var r in LivingRoles)
                    yield return new DebugActionNode(r.label, DebugActionType.Action, () => Fire(r.how, p, 1));
            if (living)
                yield return new DebugActionNode("faction leader...", DebugActionType.Action, () => PickFactionForLeader(p));
            if (dead)
                foreach (var r in DeadRoles)
                    yield return new DebugActionNode(r.label, DebugActionType.Action, () => Fire(r.how, p, 1));
        }

        [DebugAction(Category, "Raid with past colonists...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> RaidWithPastColonists() => CountNodes("RaidEnemy");

        [DebugAction(Category, "Visitors with past colonists...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> VisitorsWithPastColonists() => CountNodes("VisitorGroup");

        [DebugAction(Category, "Trade caravan with past colonists...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> CaravanWithPastColonists() => CountNodes("TraderCaravanArrival");

        private static List<DebugActionNode> CountNodes(string incident) =>
            new[] { 1, 3, 5, 99 }.Select(n => new DebugActionNode(n == 99 ? "as many as possible" : n.ToString(), DebugActionType.Action,
                () => FireIncident(incident, null, n))).ToList();

        [DebugAction(Category, "New faction leader from archive...", allowedGameStates = AllowedGameStates.Playing)]
        private static void NewFactionLeader() => PickFactionForLeader(null);

        private static void PickFactionForLeader(ArchivePawn p)
        {
            var options = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction)
                .Select(f => new DebugMenuOption(f.Name, DebugMenuOptionMode.Action, () => Report(() => DevForce.Run(p, 1, () => f.TryGenerateNewLeader()))))
                .ToList();
            Find.WindowStack.Add(new Dialog_DebugOptionListLister(options));
        }

        [DebugAction(Category, "Preview stories (log)...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> PreviewStories()
        {
            var list = new List<DebugActionNode>();
            foreach (PastRole role in Enum.GetValues(typeof(PastRole)))
            {
                if (role == PastRole.None || role == PastRole.Leader) continue;
                var variants = role == PastRole.DeadReturn
                    ? new[] { DeadVariant.Wanderer, DeadVariant.CreepJoiner, DeadVariant.Shambler, DeadVariant.Ghoul }
                    : new[] { DeadVariant.None };
                foreach (var v in variants)
                    list.Add(new DebugActionNode(role + (v != DeadVariant.None ? "/" + v : ""), DebugActionType.Action, () => LogStories(role, v, 5)));
            }
            return list;
        }

        // Told about the selected pawn (or a random colonist) as if they were a random archived person. The pick
        // follows the pawn's faction and the story rotation, without changing the rotation; "only" forces one story.
        public static List<string> LogStories(PastRole role, DeadVariant variant, int count, StoryArcDef only = null)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn ?? Find.CurrentMap?.mapPawns.FreeColonists.RandomElementWithFallback();
            var result = new List<string>();
            if (pawn == null) { Log.Warning("[PastLives] no pawn to tell stories about"); return result; }
            for (int i = 0; i < count; i++)
            {
                var arc = only ?? PastStories.PickArc(role, variant, pawn, grownUp: Rand.Bool, remember: false);
                if (arc == null) { Log.Warning("[PastLives] no stories for " + role + "/" + variant); break; }
                var a = PastLivesArchive.All.RandomElementWithFallback() ?? new ArchivePawn { Colony = "Test colony" };
                int years = arc.grownUpOnly || Rand.Bool ? Rand.RangeInclusive(1, 25) : 0;
                var text = PastStories.Tell(arc, pawn, a, PastStories.PickFaction(arc), PastLivesMod.Settings.hardships && arc.scars.Count > 0, years, role);
                result.Add(text);
                Log.Message("[PastLives] story " + arc.defName + " (" + role + "/" + variant + "):\n" + text);
            }
            return result;
        }

        [DebugAction(Category, "Reset used past colonists", allowedGameStates = AllowedGameStates.Playing)]
        private static void ResetUsed()
        {
            GameComponent_PastLives.Instance?.ResetUsed();
            Messages.Message("Everyone from the archive may appear again; cooldown reset.", MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction(Category, "Re-read saves and log stats", allowedGameStates = AllowedGameStates.Playing)]
        private static void RescanAndLog()
        {
            var task = PastLivesArchive.Rescan();
            if (task == null) { Log.Message("[PastLives] a scan is already running"); return; }
            task.ContinueWith(_ => PastLivesArchive.LogStats());
        }

        [DebugAction(Category, "Add scars", actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void AddScars(Pawn p)
        {
            var kinds = new[] { "Cut", "Scratch", "Bite", "Gunshot", "Burn", "Crush", "Stab" }
                .Select(d => DefDatabase<HediffDef>.GetNamedSilentFail(d)).Where(d => d != null).ToList();
            int n = Hardship.AddScars(p, kinds, PastLivesMod.Settings.scarCount.RandomInRange);
            Messages.Message(p.LabelShort + ": " + n + " scars", p, MessageTypeDefOf.TaskCompletion, historical: false);
        }

        [DebugAction(Category, "Add thematic trait...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static List<DebugActionNode> AddTrait()
        {
            return new[] { PastRole.Joiner, PastRole.Raider, PastRole.Slave, PastRole.Wild, PastRole.DeadReturn }
                .Select(role => new DebugActionNode(role.ToString(), DebugActionType.ToolMapForPawns, pawnAction: p =>
                {
                    bool ok = Hardship.TryGainTrait(p, Hardship.TraitsFor(role));
                    Messages.Message(p.LabelShort + (ok ? ": trait added" : ": no trait fits"), p, MessageTypeDefOf.TaskCompletion, historical: false);
                })).ToList();
        }

        private static void Fire(string how, ArchivePawn p, int count)
        {
            if (how == "casket") Report(() => DevForce.Run(p, count, () => SpawnCasket(Find.CurrentMap)));
            else if (how == "settlement") Report(() => DevForce.Run(p, count, () => GenerateEnemySettlement()));
            else if (how.StartsWith("quest:")) FireQuest(how.Substring("quest:".Length), p, count);
            else FireIncident(how, p, count);
        }

        public static void FireQuest(string defName, ArchivePawn p, int count)
        {
            var def = DefDatabase<QuestScriptDef>.GetNamedSilentFail(defName);
            var map = Find.CurrentMap;
            if (def == null || map == null) { Messages.Message("No quest " + defName + " (DLC missing?)", MessageTypeDefOf.RejectInput, historical: false); return; }
            Report(() => DevForce.Run(p, count, () =>
            {
                var quest = GenerateQuest(def, map);
                if (quest == null) Messages.Message(defName + " cannot run here now", MessageTypeDefOf.RejectInput, historical: false);
            }));
        }

        public static Quest GenerateQuest(QuestScriptDef def, Map map)
        {
            var slate = new RimWorld.QuestGen.Slate();
            slate.Set("points", StorytellerUtility.DefaultThreatPointsNow(map));
            slate.Set("map", map);
            if (!def.CanRun(slate, map)) return null;
            var quest = QuestUtility.GenerateQuestAndMakeAvailable(def, slate);
            QuestUtility.SendLetterQuestAvailable(quest);
            return quest;
        }

        // The nearest hostile settlement without a map gets one, as if a caravan had arrived; the camera goes there.
        public static Map GenerateEnemySettlement()
        {
            var home = Find.CurrentMap;
            var settlement = Find.WorldObjects.Settlements
                .Where(x => x.Faction != null && !x.Faction.IsPlayer && x.Faction.def.humanlikeFaction && x.Faction.HostileTo(Faction.OfPlayer) && !x.HasMap)
                .OrderBy(x => home == null ? 0f : Find.WorldGrid.ApproxDistanceInTiles(home.Tile, x.Tile))
                .FirstOrDefault();
            if (settlement == null) { Messages.Message("No hostile settlement without a map", MessageTypeDefOf.RejectInput, historical: false); return null; }
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(settlement.Tile, null);
            if (map != null) CameraJumper.TryJump(map.Center, map);
            return map;
        }

        // An ancient cryptosleep casket near the map centre, filled the way map generation fills them.
        public static Thing SpawnCasket(Map map)
        {
            var casketDef = DefDatabase<ThingDef>.GetNamedSilentFail("AncientCryptosleepCasket");
            var contents = ThingSetMakerDefOf.MapGen_AncientPodContents;
            if (casketDef == null || contents == null || map == null) return null;
            var things = contents.root.Generate(new ThingSetMakerParams());
            if (!RCellFinder.TryFindRandomCellNearWith(map.Center, c => c.Standable(map) && c.GetFirstBuilding(map) == null, map, out var cell, 5, 40)) return null;
            var casket = (Building_Casket)ThingMaker.MakeThing(casketDef);
            foreach (var t in things) casket.TryAcceptThing(t, allowSpecialEffects: false);
            GenSpawn.Spawn(casket, cell, map, Rot4.North);
            return casket;
        }

        private static void FireIncident(string defName, ArchivePawn p, int count)
        {
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail(defName);
            var map = Find.CurrentMap;
            if (def == null || map == null) { Messages.Message("No incident " + defName, MessageTypeDefOf.RejectInput, historical: false); return; }
            Report(() => DevForce.Run(p, count, () =>
            {
                var parms = StorytellerUtility.DefaultParmsNow(def.category, map);
                parms.forced = true;
                if (!def.Worker.TryExecute(parms)) Messages.Message(defName + " did not fire", MessageTypeDefOf.RejectInput, historical: false);
            }));
        }

        // Says how many past colonists the action brought.
        private static void Report(Action action)
        {
            var comp = GameComponent_PastLives.Instance;
            int before = comp?.Entries.Count ?? 0;
            action();
            int added = (comp?.Entries.Count ?? 0) - before;
            Messages.Message("Past colonists: " + added + (added > 0 ? " (" + string.Join(", ", comp.Entries.Skip(before).Select(e => e.name)) + ")" : ""),
                added > 0 ? MessageTypeDefOf.TaskCompletion : MessageTypeDefOf.RejectInput, historical: false);
        }
    }
}
