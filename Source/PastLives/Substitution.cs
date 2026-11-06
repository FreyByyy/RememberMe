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

        // What TryBegin changed, so Cancel can undo it.
        public bool WasUsed;
        public int PrevAppearanceTick;
        public bool Forced;
        public bool CountedWorldGenLeader;
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
        // True while the world is made and the first map is set up, when nobody is looking yet.
        public static bool WorldGenerating => Current.ProgramState != ProgramState.Playing;

        // The game is under way. Maps generated later (a camp the caravan enters, a site) switch the program
        // state to MapInitializing for a moment; that still counts as play, the start of a new game does not.
        public static bool InPlay => Current.ProgramState == ProgramState.Playing
            || (Current.ProgramState == ProgramState.MapInitializing && Find.TickManager != null && Find.TickManager.TicksGame > 0);

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

            var scope = SpawnContext.Top;
            PastRole role;
            bool scopeHasRole = scope != null && scope.Role != PastRole.None && !scope.Consumed;
            if (kind == PawnKindDefOf.Slave && !scopeHasRole && !SpawnContext.DeadReturnPending)
            {
                // Slaves are generated one by one inside trader stock, so they roll individually. Slaves made with
                // a camp or settlement the player enters are its captives, not goods for sale.
                var mapGen = MapGenerator.mapBeingGenerated;
                role = mapGen != null && !mapGen.IsPlayerHome ? PastRole.Prisoner : PastRole.Slave;
                if (!forced && !Rand.Chance(settings.ChanceFor(role))) return null;
                scope = null;
            }
            else
            {
                if (scope == null || scope.Blocked || (!forced && scope.Consumed)) return null;
                role = scope.Role != PastRole.None ? scope.Role : Classify(scope, scope.Faction ?? request.Faction, kind);
                if (role == PastRole.None) return null;
                if (!forced && !scope.Roll(role)) return null;
            }

            if (!forced && !settings.RoleEnabled(role)) return null;
            // While the world is generated only leaders matter; other pawns made then (stock slaves etc.)
            // are mostly never seen and would just use up the archive. Sleepers in the first map's caskets are
            // the exception: they wait there until the colony opens them.
            if (!InPlay && role != PastRole.Leader
                && !(role == PastRole.Sleeper && Current.ProgramState == ProgramState.MapInitializing)) return null;
            bool exempt = role == PastRole.Leader || role == PastRole.DeadReturn;
            if (!forced && !exempt && !comp.CooldownOver) return null;
            if (role == PastRole.Leader && !InPlay && comp.worldGenLeaders >= settings.maxWorldGenLeaders) return null;

            // Requests that allow no adults (a baby in a crashed pod) take someone who still is that young.
            bool youngOnly = !request.AllowedDevelopmentalStages.Adult();
            var picked = forced && DevForce.Pawn != null
                ? (ArchiveSelector.RaceCompatible(DevForce.Pawn.RaceDef, kind.race) ? DevForce.Pawn : null)
                : ArchiveSelector.Pick(role, request, comp, scope?.Quest);
            if (picked == null) return null;
            if (youngOnly && !ArchiveSelector.StageAllowed(picked, kind.race, request.AllowedDevelopmentalStages)) return null;

            bool wasUsed = comp.HasUsedKey(picked.Key);
            int prevTick = comp.LastAppearanceTick;
            bool countedLeader = role == PastRole.Leader && !InPlay;
            if (forced) DevForce.Remaining--;
            if (scope != null) scope.Consumed = true;
            // Sleepers placed on the first map wait unseen; they must not hold back the first appearances.
            comp.MarkUsed(picked.Key, countsForCooldown: !exempt && !forced && InPlay);
            if (countedLeader) comp.worldGenLeaders++;

            bool keepBackstories = request.IsCreepJoiner;
            PawnRecipe.AdjustRequest(ref request, picked, out bool grownUp);
            return new PendingSubstitution
            {
                Archived = picked,
                Role = role,
                Scope = scope,
                GrownUp = grownUp,
                KeepBackstories = keepBackstories,
                WasUsed = wasUsed,
                PrevAppearanceTick = prevTick,
                Forced = forced,
                CountedWorldGenLeader = countedLeader,
            };
        }

        public static void Cancel(PendingSubstitution s)
        {
            var comp = GameComponent_PastLives.Instance;
            comp?.Unmark(s.Archived.Key, s.WasUsed, s.PrevAppearanceTick);
            if (s.CountedWorldGenLeader && comp != null) comp.worldGenLeaders--;
            if (s.Scope != null) s.Scope.Consumed = false;
            if (s.Forced) DevForce.Remaining++;
        }

        public static void Finish(Pawn pawn, PendingSubstitution s)
        {
            PawnRecipe.Apply(pawn, s.Archived, s.KeepBackstories, s.GrownUp);
            var variant = s.Scope?.Variant ?? DeadVariant.None;
            if (s.Role == PastRole.DeadReturn && variant == DeadVariant.Wanderer && PastLivesDefOf.PastLives_Returned != null)
                pawn.health.AddHediff(PastLivesDefOf.PastLives_Returned);
            if (s.Scope != null) s.Scope.Result = pawn;

            // The story is picked first: one that left marks decides the scars and trait.
            var arc = PastStories.PickArc(s.Role, variant, pawn, s.GrownUp, quest: s.Scope?.QuestName);
            bool marks = Hardship.Apply(pawn, s.Role, arc);
            string story = null;
            if (arc != null)
            {
                int yearsGrown = s.GrownUp ? Math.Max(1, pawn.ageTracker.AgeBiologicalYears - (int)(s.Archived.AgeBioTicks / 3600000L)) : 0;
                try { story = PastStories.Tell(arc, pawn, s.Archived, PastStories.PickFaction(arc), marks, yearsGrown, s.Role); }
                catch (Exception ex) { Log.Warning("[PastLives] story " + arc.defName + " failed: " + ex.Message); }
            }
            GameComponent_PastLives.Instance?.Register(pawn, s.Archived, s.Role, variant, story);
            Log.Message("[PastLives] " + s.Archived.DisplayName + " (colony \"" + s.Archived.Colony + "\") returns as "
                + s.Role + (variant != DeadVariant.None ? "/" + variant : "") + (pawn.Faction != null ? " of " + pawn.Faction.Name : ""));
        }

        private static PastRole Classify(SpawnScope scope, Faction faction, PawnKindDef kind)
        {
            // Entities (shambler swarms etc.) are not a place for the living.
            if (faction == null || faction.IsPlayer || !faction.def.humanlikeFaction || kind.mutant != null) return PastRole.None;
            if (scope.MapGen) return PastRole.Defender;
            var player = Faction.OfPlayerSilentFail;
            if (player != null && faction.HostileTo(player)) return PastRole.Raider;
            if (kind.trader) return PastRole.Trader;
            return PastRole.Visitor;
        }
    }
}
