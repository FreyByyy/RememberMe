using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PastLives
{
    // Extension for the dead-return incident defs: which form the returning colonist takes.
    public class DeadReturnExtension : DefModExtension
    {
        public DeadVariant variant = DeadVariant.Wanderer;
    }

    public static class DeadReturn
    {
        public static bool CanFire(ThingDef race)
        {
            var comp = GameComponent_PastLives.Instance;
            return PastLivesMod.Settings.enabled && PastLivesMod.Settings.roleDead && comp != null
                && ArchiveSelector.AnyCandidate(PastRole.DeadReturn, race ?? ThingDefOf.Human, comp);
        }

        public static DeadVariant VariantOf(IncidentDef def) => def.GetModExtension<DeadReturnExtension>()?.variant ?? DeadVariant.Wanderer;
    }

    // A colonist who died in an old game walks in and asks to join (wanderer quest), or comes as a
    // creepjoiner with Anomaly. The vanilla quest runs as usual; the scope makes its pawn the dead colonist.
    public class IncidentWorker_DeadReturnsQuest : IncidentWorker_GiveQuest
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return base.CanFireNowSub(parms) && DeadReturn.CanFire(ThingDefOf.Human);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            var scope = new SpawnScope { Role = PastRole.DeadReturn, Rolled = true, Variant = DeadReturn.VariantOf(def) };
            SpawnContext.Push(scope);
            try { return base.TryExecuteWorker(parms); }
            finally { SpawnContext.Pop(scope); }
        }
    }

    // Anomaly: a dead colonist shambles in among other shamblers.
    public class IncidentWorker_DeadReturnsShambler : IncidentWorker_ShamblerSwarm
    {
        private Pawn returned;

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return ModsConfig.AnomalyActive && base.CanFireNowSub(parms) && DeadReturn.CanFire(ThingDefOf.Human);
        }

        protected override List<Pawn> GenerateEntities(IncidentParms parms, float points)
        {
            returned = null;
            var list = base.GenerateEntities(parms, points * 0.5f) ?? new List<Pawn>();

            var scope = new SpawnScope { Role = PastRole.DeadReturn, Rolled = true, Variant = DeadVariant.Shambler };
            SpawnContext.Push(scope);
            Pawn pawn;
            try
            {
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null,
                    PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            }
            finally { SpawnContext.Pop(scope); }

            if (scope.Result == pawn && pawn != null)
            {
                MutantUtility.SetFreshPawnAsMutant(pawn, MutantDefOf.Shambler);
                SetupShamblerHediffs(new List<Pawn> { pawn }, ShamblerLifespanTicksRange);
                list.Add(pawn);
                returned = pawn;
            }
            return list;
        }

        protected override void SendLetter(IncidentParms parms, List<Pawn> entities)
        {
            var comp = GameComponent_PastLives.Instance;
            var entry = returned == null ? null : comp?.EntryFor(returned);
            if (entry == null)
            {
                base.SendLetter(parms, entities);
                return;
            }
            entry.notified = true;
            var text = "PastLives_Letter_Dead_Shambler".Translate(returned.Named("PAWN"), entry.colony.Named("COLONY"), "".Named("FACTION"));
            if (entities.Count > 1) text += "\n\n" + "LetterShamblerSwarmArrived".Translate(entities.Count - 1);
            SendStandardLetter("PastLives_LetterLabel_Dead".Translate(returned.Named("PAWN")), text, def.letterDef, parms, new LookTargets(returned));
        }
    }
}
