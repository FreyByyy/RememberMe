using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PastLives
{
    // Chooses which past colonist fits a given role. Each candidate gets a weight from what they did in
    // their old life (ideology roles, titles, skills, kills, traits, backstory) and how well they fit the
    // faction they would join; one is then picked at random by weight.
    public static class ArchiveSelector
    {
        private static readonly string[] BloodthirstyTraits = { "Bloodlust", "Psychopath", "Brawler", "TriggerHappy", "Abrasive" };

        public static ArchivePawn Pick(PastRole role, PawnGenerationRequest request, GameComponent_PastLives comp)
        {
            var race = request.KindDef?.race;
            if (race == null) return null;
            var settings = PastLivesMod.Settings;
            bool wantDead = role == PastRole.DeadReturn;

            var weighted = new List<(ArchivePawn pawn, float weight)>();
            foreach (var p in PastLivesArchive.All)
            {
                if (!Eligible(p, wantDead, race, comp)) continue;
                if (request.FixedGender.HasValue && race.race.hasGenders && p.Gender != request.FixedGender.Value.ToString()) continue;
                if (request.MustBeCapableOfViolence && IncapableOfViolence(p)) continue;
                float w = RoleWeight(p, role) * FactionFit(p, request.Faction);
                if (p.IsListPawn) w *= settings.listWeight;
                if (w > 0f) weighted.Add((p, w));
            }
            if (weighted.Count == 0) return null;
            return weighted.RandomElementByWeight(x => x.weight).pawn;
        }

        public static bool AnyCandidate(PastRole role, ThingDef race, GameComponent_PastLives comp)
        {
            bool wantDead = role == PastRole.DeadReturn;
            return PastLivesArchive.All.Any(p => Eligible(p, wantDead, race, comp));
        }

        private static bool Eligible(ArchivePawn p, bool wantDead, ThingDef race, GameComponent_PastLives comp)
        {
            var settings = PastLivesMod.Settings;
            if (p.Dead != wantDead || comp.IsUsed(p)) return false;
            // Colony switches are for old saves; anything in "my list" was chosen on purpose.
            if (!p.IsListPawn && settings.excludedColonies.Contains(p.Colony)) return false;
            if (p.RaceDef == null || !RaceCompatible(p.RaceDef, race)) return false;
            return settings.includeChildren || !IsChild(p, race);
        }

        // Creepjoiners use their own race def, which is a human in every way that matters here.
        public static bool RaceCompatible(string archived, ThingDef target)
        {
            if (archived == target.defName) return true;
            bool humanish(string d) => d == "Human" || d == "CreepJoiner";
            return humanish(archived) && humanish(target.defName);
        }

        public static float AdultAge(ThingDef race)
        {
            var stages = race?.race?.lifeStageAges;
            if (stages == null || stages.Count == 0) return 18f;
            var adult = stages.LastOrDefault(s => s.def.developmentalStage.Adult());
            return adult?.minAge ?? 18f;
        }

        public static bool IsChild(ArchivePawn p, ThingDef race) => p.AgeBioTicks / 3600000f < AdultAge(race);

        // --- role weights ---

        public static float RoleWeight(ArchivePawn p, PastRole role)
        {
            int shooting = Skill(p, "Shooting"), melee = Skill(p, "Melee"), social = Skill(p, "Social");
            float seniority = TitleSeniority(p);
            bool leader = p.IsIdeoLeader;
            switch (role)
            {
                case PastRole.Leader:
                    return 0.25f + (leader ? 6f : 0f) + (p.HasOtherIdeoRole ? 1.5f : 0f)
                        + Math.Min(seniority / 150f, 4f) + social / 8f;

                case PastRole.Raider:
                {
                    if (IncapableOfViolence(p)) return 0f;
                    float w = 0.4f + (shooting + melee) / 16f + Math.Min(Kills(p) / 25f, 3f);
                    w += 0.7f * p.Traits.Count(t => BloodthirstyTraits.Contains(t.Def));
                    if (p.IdeoRoles.Any(r => r.Contains("Commissar") || r.Contains("Military") || r.Contains("Warlord"))) w += 1f;
                    if (p.Traits.Any(t => t.Def == "Kind")) w *= 0.5f;
                    if (leader) w *= 0.5f;   // former leaders are saved for leadership
                    return w;
                }

                case PastRole.Trader:
                {
                    float w = 0.3f + social / 6f;
                    if (p.IdeoRoles.Any(r => r.Contains("Merchant"))) w += 2f;
                    if (BackstoryCategories(p).Contains("Trader")) w += 1f;
                    return w;
                }

                case PastRole.Visitor:
                    return (1f + social / 20f) * (leader ? 0.7f : 1f);

                case PastRole.Slave:
                    return (p.Slave ? 2.5f : 0.5f) * (seniority > 0f || leader ? 0.3f : 1f);

                case PastRole.Joiner:
                case PastRole.Refugee:
                    return leader ? 0.7f : 1f;

                case PastRole.DeadReturn:
                    return 1f + p.Skills.Sum(s => s.Level) / 100f;

                default:
                    return 0f;
            }
        }

        public static float FactionFit(ArchivePawn p, Faction faction)
        {
            if (faction == null || faction.def == null) return 1f;
            float f = 1f;

            if (faction.def.HasRoyalTitles) f *= p.Titles.Count > 0 ? 3f : 0.8f;

            bool tribal = BackstoryCategories(p).Any(c => c.IndexOf("Tribal", StringComparison.OrdinalIgnoreCase) >= 0);
            if (faction.def.techLevel <= TechLevel.Neolithic) f *= tribal ? 2.5f : 0.5f;
            else if (tribal) f *= 0.6f;

            if (faction.def.permanentEnemy && p.Traits.Any(t => BloodthirstyTraits.Contains(t.Def))) f *= 1.5f;

            var set = faction.def.xenotypeSet;
            if (ModsConfig.BiotechActive && set != null && !string.IsNullOrEmpty(p.Xenotype) && p.Xenotype != "Baseliner")
            {
                bool match = false;
                for (int i = 0; i < set.Count; i++)
                    if (set[i].xenotype?.defName == p.Xenotype) { match = true; break; }
                f *= match ? 3f : 0.7f;
            }
            return f;
        }

        // --- helpers over raw archive data ---

        private static T Def<T>(string name) where T : Def => string.IsNullOrEmpty(name) ? null : DefDatabase<T>.GetNamedSilentFail(name);

        private static int Skill(ArchivePawn p, string def)
        {
            foreach (var s in p.Skills) if (s.Def == def) return s.Level;
            return 0;
        }

        private static float TitleSeniority(ArchivePawn p)
        {
            float best = 0f;
            foreach (var t in p.Titles)
            {
                var def = Def<RoyalTitleDef>(t.Def);
                if (def != null && def.seniority > best) best = def.seniority;
            }
            return best;
        }

        // Records are stored by def index, so they only mean something when the record list matches ours.
        private static float Kills(ArchivePawn p)
        {
            if (p.Kills >= 0f) return p.Kills;
            var kills = RecordDefOf.Kills;
            if (kills == null || p.Records.Count != DefDatabase<RecordDef>.DefCount || kills.index >= p.Records.Count) return 0f;
            return p.Records[kills.index];
        }

        public static IEnumerable<string> BackstoryCategories(ArchivePawn p)
        {
            foreach (var name in new[] { p.Childhood, p.Adulthood })
            {
                var def = name == null ? null : Def<BackstoryDef>(name);
                if (def?.spawnCategories == null) continue;
                foreach (var c in def.spawnCategories) yield return c;
            }
        }

        public static bool IncapableOfViolence(ArchivePawn p)
        {
            foreach (var name in new[] { p.Childhood, p.Adulthood })
            {
                var def = name == null ? null : Def<BackstoryDef>(name);
                if (def != null && (def.workDisables & WorkTags.Violent) != 0) return true;
            }
            foreach (var t in p.Traits)
            {
                var def = Def<TraitDef>(t.Def);
                if (def != null && (def.disabledWorkTags & WorkTags.Violent) != 0) return true;
            }
            return false;
        }
    }
}
