using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PastLives
{
    // Rebuilds a past colonist on top of a freshly generated pawn. The game generates the pawn for its
    // role (faction, gear, ideology, relations to nobody); we then restore who they were: name, look,
    // traits, skills, backstory, genes and prosthetics. Anything whose def is no longer loaded is skipped.
    public static class PawnRecipe
    {
        // Applied before generation, so gear, body and apparel already fit the person.
        public static void AdjustRequest(ref PawnGenerationRequest request, ArchivePawn a, out bool grownUp)
        {
            var race = request.KindDef.race;
            if (race.race.hasGenders && Enum.TryParse<Gender>(a.Gender, out var gender) && gender != Gender.None)
                request.FixedGender = gender;

            float bio = a.AgeBioTicks / 3600000f;
            float chrono = Math.Max(bio, a.AgeChronoTicks / 3600000f);
            float adultAge = ArchiveSelector.AdultAge(race);
            grownUp = bio < adultAge;
            if (grownUp)
            {
                // Children from old colonies come back grown: years have passed since then.
                float grown = Rand.Range(adultAge, adultAge + 6f);
                chrono += grown - bio;
                bio = grown;
            }
            request.FixedBiologicalAge = bio;
            request.FixedChronologicalAge = chrono;
            request.BiologicalAgeRange = null;
            request.ExcludeBiologicalAgeRange = null;
            request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
            request.ForceGenerateNewPawn = true;
            request.CanGeneratePawnRelations = false;

            if (ModsConfig.BiotechActive && a.HasGenes && string.IsNullOrEmpty(a.XenotypeName))
            {
                var xeno = Def<XenotypeDef>(a.Xenotype);
                if (xeno != null)
                {
                    request.ForcedXenotype = xeno;
                    request.AllowedXenotypes = null;
                    request.ForceBaselinerChance = 0f;
                }
            }
        }

        public static void Apply(Pawn pawn, ArchivePawn a, bool keepBackstories, bool grownUp)
        {
            Step(pawn, "name", () => ApplyName(pawn, a));
            Step(pawn, "genes", () => ApplyGenes(pawn, a));
            Step(pawn, "traits", () => ApplyTraits(pawn, a));
            if (!keepBackstories) Step(pawn, "backstory", () => ApplyBackstories(pawn, a, grownUp));
            Step(pawn, "skills", () => ApplySkills(pawn, a, grownUp));
            Step(pawn, "appearance", () => ApplyAppearance(pawn, a, grownUp));
            Step(pawn, "prosthetics", () => ApplyAddedParts(pawn, a));
            Step(pawn, "title", () => ApplyTitle(pawn, a));

            pawn.skills?.Notify_SkillDisablesChanged();
            pawn.Notify_DisabledWorkTypesChanged();
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            PortraitsCache.SetDirty(pawn);
        }

        private static void Step(Pawn pawn, string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { Log.Warning("[PastLives] could not restore " + what + " for " + pawn + ": " + ex.Message); }
        }

        private static void ApplyName(Pawn pawn, ArchivePawn a)
        {
            if (!string.IsNullOrEmpty(a.NameSingle)) pawn.Name = new NameSingle(a.NameSingle);
            else if (!string.IsNullOrEmpty(a.NameFirst) || !string.IsNullOrEmpty(a.NameLast))
            {
                pawn.Name = new NameTriple(a.NameFirst ?? "", a.NameNick ?? a.NameFirst ?? "", a.NameLast ?? "");
                if (pawn.story != null && !string.IsNullOrEmpty(a.NameLast)) pawn.story.birthLastName = a.NameLast;
            }
        }

        private static void ApplyGenes(Pawn pawn, ArchivePawn a)
        {
            if (!ModsConfig.BiotechActive || pawn.genes == null || !a.HasGenes) return;
            var endo = a.Endogenes.Select(Def<GeneDef>).ToList();
            var xeno = a.Xenogenes.Select(Def<GeneDef>).ToList();
            if (endo.Contains(null) || xeno.Contains(null)) return;   // a gene mod is gone: keep the generated xenotype

            foreach (var g in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(g);
            foreach (var g in endo) pawn.genes.AddGene(g, xenogene: false);
            foreach (var g in xeno) pawn.genes.AddGene(g, xenogene: true);

            pawn.genes.SetXenotypeDirect(Def<XenotypeDef>(a.Xenotype) ?? XenotypeDefOf.Baseliner);
            if (!string.IsNullOrEmpty(a.XenotypeName))
            {
                pawn.genes.xenotypeName = a.XenotypeName;
                pawn.genes.iconDef = Def<XenotypeIconDef>(a.XenotypeIcon);
            }
        }

        private static void ApplyTraits(Pawn pawn, ArchivePawn a)
        {
            var traits = pawn.story?.traits;
            if (traits == null || a.Traits.Count == 0) return;
            foreach (var t in traits.allTraits.Where(t => t.sourceGene == null).ToList()) traits.RemoveTrait(t);
            foreach (var t in a.Traits)
            {
                var def = Def<TraitDef>(t.Def);
                if (def == null || traits.HasTrait(def) || traits.allTraits.Any(x => x.def.ConflictsWith(def))) continue;
                traits.GainTrait(new Trait(def, t.Degree, forced: true));
            }
        }

        private static void ApplyBackstories(Pawn pawn, ArchivePawn a, bool grownUp)
        {
            var story = pawn.story;
            if (story == null) return;
            // A grown-up child keeps the childhood and adult life the generator just gave them when the old
            // backstory was a "still a child" one (those are only valid for pawns who are children now).
            var child = Def<BackstoryDef>(a.Childhood);
            bool activeChild = child?.spawnCategories != null && child.spawnCategories.Contains("Child");
            if (child != null && child.slot == BackstorySlot.Childhood && !(grownUp && activeChild)) story.Childhood = child;
            var adult = Def<BackstoryDef>(a.Adulthood);
            if (!grownUp && adult != null && adult.slot == BackstorySlot.Adulthood) story.Adulthood = adult;
        }

        private static void ApplySkills(Pawn pawn, ArchivePawn a, bool grownUp)
        {
            if (pawn.skills == null) return;
            foreach (var s in a.Skills)
            {
                var def = Def<SkillDef>(s.Def);
                if (def == null) continue;
                var rec = pawn.skills.GetSkill(def);
                if (rec == null) continue;
                var passion = Enum.TryParse<Passion>(s.Passion ?? "None", out var p) ? p : Passion.None;
                int level = s.Level + (grownUp && passion != Passion.None ? Rand.RangeInclusive(2, 5) : 0);
                rec.Level = Mathf.Clamp(level, SkillRecord.MinLevel, SkillRecord.MaxLevel);
                rec.passion = passion;
                rec.xpSinceLastLevel = rec.XpRequiredForLevelUp * Rand.Range(0f, 0.5f);
            }
        }

        private static void ApplyAppearance(Pawn pawn, ArchivePawn a, bool grownUp)
        {
            var story = pawn.story;
            if (story == null) return;
            if (!grownUp && Def<BodyTypeDef>(a.BodyType) is BodyTypeDef body) story.bodyType = body;
            if (Def<HeadTypeDef>(a.HeadType) is HeadTypeDef head && (head.gender == Gender.None || head.gender == pawn.gender)) story.headType = head;
            if (Def<HairDef>(a.HairDef) is HairDef hair) story.hairDef = hair;
            if (TryColor(a.HairColor, out var hairColor)) story.HairColor = hairColor;
            if (TryColor(a.SkinColorOverride, out var skin)) story.skinColorOverride = skin;
            if (Def<FurDef>(a.FurDef) is FurDef fur) story.furDef = fur;
            if (Def<ColorDef>(a.FavoriteColor) is ColorDef fav) story.favoriteColor = fav;

            var style = pawn.style;
            if (style == null) return;
            if (Def<BeardDef>(a.Beard) is BeardDef beard) style.beardDef = beard;
            if (ModsConfig.IdeologyActive)
            {
                if (Def<TattooDef>(a.FaceTattoo) is TattooDef face) style.FaceTattoo = face;
                if (Def<TattooDef>(a.BodyTattoo) is TattooDef bodyTat) style.BodyTattoo = bodyTat;
            }
        }

        private static void ApplyAddedParts(Pawn pawn, ArchivePawn a)
        {
            var body = pawn.RaceProps.body;
            foreach (var h in a.Hediffs)
            {
                var def = Def<HediffDef>(h.Def);
                if (def == null || !def.countsAsAddedPartOrImplant) continue;
                BodyPartRecord part = null;
                if (h.PartIndex >= 0)
                {
                    if (h.Body != body.defName && h.Body != ArchivePawn.OwnBody) continue;
                    part = body.GetPartAtIndex(h.PartIndex);
                    if (part == null) continue;
                }
                if (pawn.health.hediffSet.HasHediff(def, part)) continue;
                if (part != null && pawn.health.hediffSet.PartIsMissing(part) && part.parent != null && pawn.health.hediffSet.PartIsMissing(part.parent)) continue;
                var added = pawn.health.AddHediff(def, part);
                if (added is Hediff_Level leveled && h.Level > 1) leveled.SetLevelTo(h.Level);
            }
        }

        private static void ApplyTitle(Pawn pawn, ArchivePawn a)
        {
            var faction = pawn.Faction;
            if (a.Titles.Count == 0 || pawn.royalty == null || faction == null || faction.IsPlayer || !faction.def.HasRoyalTitles) return;
            var best = a.Titles.Select(t => Def<RoyalTitleDef>(t.Def))
                .Where(d => d != null && faction.def.RoyalTitlesAwardableInSeniorityOrderForReading.Contains(d))
                .OrderByDescending(d => d.seniority).FirstOrDefault();
            if (best == null) return;
            var current = pawn.royalty.GetCurrentTitle(faction);
            if (current == null || current.seniority < best.seniority)
                pawn.royalty.SetTitle(faction, best, grantRewards: false, rewardsOnlyForNewestTitle: false, sendLetter: false);
        }

        private static T Def<T>(string name) where T : Def => string.IsNullOrEmpty(name) ? null : DefDatabase<T>.GetNamedSilentFail(name);

        private static bool TryColor(string s, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            try { color = ParseHelper.FromString<Color>(s); return true; }
            catch { return false; }
        }
    }
}
