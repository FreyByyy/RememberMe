using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PastLives
{
    // Experimental: a returned colonist bears marks of the years between lives. Old healed scars on random
    // outer body parts (never lost limbs, never eyes, ears, jaw or the like) and a trait fitting who they
    // became. A story that left a mark decides which scars and which trait.
    public static class Hardship
    {
        public struct TraitPick
        {
            public string Def;
            public int Degree;
            public TraitPick(string def, int degree) { Def = def; Degree = degree; }
        }

        // The game gives up to three traits at birth; one more on top is the limit for a new one.
        public const int MaxTraits = 4;

        private static readonly string[] DefaultScars = { "Cut", "Scratch", "Bite", "Gunshot", "Burn", "Crush", "Stab" };

        private static readonly TraitPick[] DeadTraits =
        {
            new TraitPick("Nerves", 2), new TraitPick("Nerves", -1), new TraitPick("Masochist", 0),
            new TraitPick("PsychicSensitivity", 1), new TraitPick("NaturalMood", -1),
        };
        private static readonly TraitPick[] RaiderTraits =
        {
            new TraitPick("Bloodlust", 0), new TraitPick("Abrasive", 0), new TraitPick("Brawler", 0),
            new TraitPick("ShootingAccuracy", -1), new TraitPick("Tough", 0),
        };
        private static readonly TraitPick[] GuestTraits =
        {
            new TraitPick("Industriousness", 2), new TraitPick("NaturalMood", 1), new TraitPick("Nerves", 2), new TraitPick("SpeedOffset", 1),
        };
        private static readonly TraitPick[] SlaveTraits =
        {
            new TraitPick("NaturalMood", -1), new TraitPick("Nerves", -1), new TraitPick("Tough", 0),
        };

        public static TraitPick[] TraitsFor(PastRole role)
        {
            switch (role)
            {
                case PastRole.DeadReturn: return DeadTraits;
                case PastRole.Raider: return RaiderTraits;
                case PastRole.Slave: return SlaveTraits;
                default: return GuestTraits;
            }
        }

        // Returns true when the story's own marks were applied, so the story may speak of them.
        public static bool Apply(Pawn pawn, PastRole role, StoryArcDef arc)
        {
            var settings = PastLivesMod.Settings;
            if (!settings.hardships) return false;
            bool storyMarks = arc != null && (arc.scars.Count > 0 || arc.trait != null);

            float scarChance = role == PastRole.DeadReturn ? settings.scarChanceDead : settings.scarChanceOther;
            if (arc != null && arc.scars.Count > 0) AddScars(pawn, arc.scars, settings.scarCount.RandomInRange);
            else if (Rand.Chance(scarChance)) AddScars(pawn, DefaultScars.Select(d => DefDatabase<HediffDef>.GetNamedSilentFail(d)).Where(d => d != null).ToList(), settings.scarCount.RandomInRange);

            if (arc?.trait != null) TryGainTrait(pawn, arc.trait.def, arc.trait.degree);
            else if (Rand.Chance(settings.traitChance)) TryGainTrait(pawn, TraitsFor(role));

            pawn.Notify_DisabledWorkTypesChanged();
            pawn.skills?.Notify_SkillDisablesChanged();
            return storyMarks;
        }

        public static int AddScars(Pawn pawn, IList<HediffDef> kinds, int count)
        {
            var set = pawn.health?.hediffSet;
            if (set == null || kinds == null || kinds.Count == 0) return 0;
            var parts = set.GetNotMissingParts()
                .Where(p => p.depth == BodyPartDepth.Outside && p.def.permanentInjuryChanceFactor > 0f && !Delicate(p)
                    && !set.PartOrAnyAncestorHasDirectlyAddedParts(p))
                .ToList();
            int added = 0;
            for (int i = 0; i < count && parts.Count > 0; i++)
            {
                var part = parts.RandomElementByWeight(p => p.coverageAbs + 0.01f);
                float maxHealth = part.def.GetMaxHealth(pawn);
                if (maxHealth <= 3f) { parts.Remove(part); i--; continue; }
                if (!(HediffMaker.MakeHediff(kinds.RandomElement(), pawn, part) is Hediff_Injury injury)) continue;
                var permanent = injury.TryGetComp<HediffComp_GetsPermanent>();
                if (permanent == null) continue;
                // Well below the part's health: a scar, never a lost part.
                injury.Severity = Mathf.Min(Rand.Range(2f, 6f), maxHealth * 0.4f);
                permanent.IsPermanent = true;
                pawn.health.AddHediff(injury, part);
                added++;
            }
            return added;
        }

        // Scars there would take sight, hearing, breath, speech or the mind.
        private static bool Delicate(BodyPartRecord part)
        {
            var tags = part.def.tags;
            if (tags == null) return false;
            return tags.Contains(BodyPartTagDefOf.SightSource) || tags.Contains(BodyPartTagDefOf.HearingSource)
                || tags.Contains(BodyPartTagDefOf.BreathingSource) || tags.Contains(BodyPartTagDefOf.ConsciousnessSource)
                || tags.Contains(BodyPartTagDefOf.EatingSource) || tags.Contains(BodyPartTagDefOf.TalkingSource);
        }

        public static bool TryGainTrait(Pawn pawn, TraitPick[] pool)
        {
            var options = pool.Where(t => DefDatabase<TraitDef>.GetNamedSilentFail(t.Def) != null).ToList();
            while (options.Count > 0)
            {
                var pick = options.RandomElement();
                if (TryGainTrait(pawn, DefDatabase<TraitDef>.GetNamed(pick.Def), pick.Degree)) return true;
                options.Remove(pick);
            }
            return false;
        }

        // Added on top of what the pawn has; skipped on a conflict or when the pawn has enough traits.
        public static bool TryGainTrait(Pawn pawn, TraitDef def, int degree)
        {
            var traits = pawn.story?.traits;
            if (traits == null || def == null) return false;
            if (traits.allTraits.Count(t => t.sourceGene == null) >= MaxTraits) return false;
            if (traits.HasTrait(def) || traits.allTraits.Any(t => t.def.ConflictsWith(def) || def.ConflictsWith(t))) return false;
            if (def.degreeDatas != null && def.degreeDatas.Count > 0 && !def.degreeDatas.Any(d => d.degree == degree)) return false;
            traits.GainTrait(new Trait(def, degree, forced: true));
            return true;
        }
    }
}
