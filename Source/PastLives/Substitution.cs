using System;
using RimWorld;
using Verse;

namespace PastLives
{
    public class PendingSubstitution
    {
        public ArchivePawn Archived;
        public PastRole Role;
        public SpawnScope Scope;
        public bool GrownUp;
        public bool KeepBackstories;
    }

    // Dev tools: while active, substitutions ignore chances, cooldown and settings, up to Remaining pawns,
    // optionally always with one given person.
    public static class DevForce
    {
        public static ArchivePawn Pawn;
        public static int Remaining;

        public static bool Active => Remaining > 0;

        public static void Run(ArchivePawn pawn, int count, Action action)
        {
            Pawn = pawn;
            Remaining = count;
            try { action(); }
            finally { Pawn = null; Remaining = 0; }
        }
    }

    // Decides, for one PawnGenerator.GeneratePawn call, whether a past colonist takes this pawn's place.
    public static class Substitution
    {
        public static bool WorldGenerating => Current.ProgramState != ProgramState.Playing;

        public static PendingSubstitution TryBegin(ref PawnGenerationRequest request)
        {
            var settings = PastLivesMod.Settings;
            bool forced = DevForce.Active;
            if ((!settings.enabled && !forced) || Current.Game == null) return null;
            var comp = GameComponent_PastLives.Instance;
            if (comp == null) return null;
            var kind = request.KindDef;
            if (kind?.race?.race == null || !kind.RaceProps.Humanlike) return null;
            if (request.Context == PawnGenerationContext.PlayerStarter || request.ForceDead) return null;
            if (!request.AllowedDevelopmentalStages.Adult()) return null;

            var scope = SpawnContext.Top;
            PastRole role;
            if (kind == PawnKindDefOf.Slave && !SpawnContext.DeadReturnPending)
            {
                // Slaves are generated one by one inside trader stock, so they roll individually.
                if (!forced && !Rand.Chance(settings.eventChance)) return null;
                role = PastRole.Slave;
                scope = null;
            }
            else
            {
                if (scope == null || (!forced && (scope.Consumed || !scope.Rolled))) return null;
                role = scope.Role != PastRole.None ? scope.Role : Classify(scope.Faction ?? request.Faction, kind);
                if (role == PastRole.None) return null;
            }

            if (!forced && !settings.RoleEnabled(role)) return null;
            // While the world is generated only leaders matter; other pawns made then (stock slaves etc.)
            // are mostly never seen and would just use up the archive.
            if (WorldGenerating && role != PastRole.Leader) return null;
            bool exempt = role == PastRole.Leader || role == PastRole.DeadReturn;
            if (!forced && !exempt && !comp.CooldownOver) return null;
            if (role == PastRole.Leader && WorldGenerating && comp.worldGenLeaders >= settings.maxWorldGenLeaders) return null;

            var picked = forced && DevForce.Pawn != null
                ? (ArchiveSelector.RaceCompatible(DevForce.Pawn.RaceDef, kind.race) ? DevForce.Pawn : null)
                : ArchiveSelector.Pick(role, request, comp);
            if (picked == null) return null;

            if (forced) DevForce.Remaining--;
            if (scope != null) scope.Consumed = true;
            comp.MarkUsed(picked.Key, countsForCooldown: !exempt && !forced);
            if (role == PastRole.Leader && WorldGenerating) comp.worldGenLeaders++;

            bool keepBackstories = request.IsCreepJoiner;
            PawnRecipe.AdjustRequest(ref request, picked, out bool grownUp);
            return new PendingSubstitution
            {
                Archived = picked,
                Role = role,
                Scope = scope,
                GrownUp = grownUp,
                KeepBackstories = keepBackstories,
            };
        }

        public static void Finish(Pawn pawn, PendingSubstitution s)
        {
            PawnRecipe.Apply(pawn, s.Archived, s.KeepBackstories, s.GrownUp);
            var variant = s.Scope?.Variant ?? DeadVariant.None;
            if (s.Role == PastRole.DeadReturn && variant == DeadVariant.Wanderer && PastLivesDefOf.PastLives_Returned != null)
                pawn.health.AddHediff(PastLivesDefOf.PastLives_Returned);
            if (s.Scope != null) s.Scope.Result = pawn;

            // The story is picked first: one that left marks decides the scars and trait.
            var arc = PastStories.PickArc(s.Role, variant);
            bool marks = Hardship.Apply(pawn, s.Role, arc);
            string story = null;
            if (arc != null)
            {
                int yearsGrown = s.GrownUp ? Math.Max(1, pawn.ageTracker.AgeBiologicalYears - (int)(s.Archived.AgeBioTicks / 3600000L)) : 0;
                try { story = PastStories.Tell(arc, pawn, s.Archived, PastStories.PickFaction(arc), marks, yearsGrown); }
                catch (Exception ex) { Log.Warning("[PastLives] story " + arc.defName + " failed: " + ex.Message); }
            }
            GameComponent_PastLives.Instance?.Register(pawn, s.Archived, s.Role, variant, story);
            Log.Message("[PastLives] " + s.Archived.DisplayName + " (colony \"" + s.Archived.Colony + "\") returns as "
                + s.Role + (variant != DeadVariant.None ? "/" + variant : "") + (pawn.Faction != null ? " of " + pawn.Faction.Name : ""));
        }

        private static PastRole Classify(Faction faction, PawnKindDef kind)
        {
            // Entities (shambler swarms etc.) are not a place for the living.
            if (faction == null || faction.IsPlayer || !faction.def.humanlikeFaction || kind.mutant != null) return PastRole.None;
            var player = Faction.OfPlayerSilentFail;
            if (player != null && faction.HostileTo(player)) return PastRole.Raider;
            if (kind.trader) return PastRole.Trader;
            return PastRole.Visitor;
        }
    }
}
