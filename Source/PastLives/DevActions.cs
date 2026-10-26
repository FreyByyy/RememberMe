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

        private static readonly (string label, PastRole role, string incident)[] LivingRoles =
        {
            ("wanderer who joins", PastRole.Joiner, "WandererJoin"),
            ("visitor", PastRole.Visitor, "VisitorGroup"),
            ("trader", PastRole.Trader, "TraderCaravanArrival"),
            ("raider", PastRole.Raider, "RaidEnemy"),
        };

        private static readonly (string label, string incident)[] DeadRoles =
        {
            ("dead: wanderer", "PastLives_DeadReturns"),
            ("dead: creepjoiner", "PastLives_DeadReturns_CreepJoiner"),
            ("dead: shambler", "PastLives_DeadReturns_Shambler"),
        };

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
                    yield return new DebugActionNode(r.label, DebugActionType.Action, () => FireIncident(r.incident, p, 1));
            if (living)
                yield return new DebugActionNode("faction leader...", DebugActionType.Action, () => PickFactionForLeader(p));
            if (dead)
                foreach (var r in DeadRoles)
                    yield return new DebugActionNode(r.label, DebugActionType.Action, () => FireIncident(r.incident, p, 1));
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
                    ? new[] { DeadVariant.Wanderer, DeadVariant.CreepJoiner, DeadVariant.Shambler }
                    : new[] { DeadVariant.None };
                foreach (var v in variants)
                    list.Add(new DebugActionNode(role + (v != DeadVariant.None ? "/" + v : ""), DebugActionType.Action, () => LogStories(role, v, 5)));
            }
            return list;
        }

        // Told about the selected pawn (or a random colonist) as if they were a random archived person.
        public static List<string> LogStories(PastRole role, DeadVariant variant, int count)
        {
            var pawn = Find.Selector.SingleSelectedThing as Pawn ?? Find.CurrentMap?.mapPawns.FreeColonists.RandomElementWithFallback();
            var result = new List<string>();
            if (pawn == null) { Log.Warning("[PastLives] no pawn to tell stories about"); return result; }
            for (int i = 0; i < count; i++)
            {
                var arc = DefDatabase<StoryArcDef>.AllDefs.Where(a => a.Fits(role, variant)).RandomElementByWeightWithFallback(a => a.commonality);
                if (arc == null) { Log.Warning("[PastLives] no stories for " + role + "/" + variant); break; }
                var a = PastLivesArchive.All.RandomElementWithFallback() ?? new ArchivePawn { Colony = "Test colony" };
                var text = PastStories.Tell(arc, pawn, a, PastStories.PickFaction(arc), PastLivesMod.Settings.hardships && arc.scars.Count > 0, Rand.Bool ? Rand.RangeInclusive(1, 25) : 0);
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
            return new[] { PastRole.Joiner, PastRole.Raider, PastRole.Slave, PastRole.DeadReturn }
                .Select(role => new DebugActionNode(role.ToString(), DebugActionType.ToolMapForPawns, pawnAction: p =>
                {
                    bool ok = Hardship.TryGainTrait(p, Hardship.TraitsFor(role));
                    Messages.Message(p.LabelShort + (ok ? ": trait added" : ": no trait fits"), p, MessageTypeDefOf.TaskCompletion, historical: false);
                })).ToList();
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
