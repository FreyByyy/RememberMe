using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;
using Verse.Grammar;

namespace PastLives
{
    // Which faction a story names as FACTION.
    public enum StoryFaction
    {
        None,
        Pirate,         // a pirate faction of this world
        SpacePirate,    // space stories: with Odyssey the salvagers, otherwise any pirates
        Peaceful,       // any faction that is not a pirate one, hostile or not
    }

    public class StoryTrait
    {
        public TraitDef def;
        public int degree;
    }

    // One kind of story a returned colonist can tell: who may tell it, which faction it names, and what
    // marks it leaves (only with the experimental hardships setting). The text lives in a RulePackDef so
    // it is translated like any vanilla grammar.
    public class StoryArcDef : Def
    {
        public List<PastRole> roles = new List<PastRole>();
        public List<DeadVariant> variants = new List<DeadVariant>();   // DeadReturn only; empty = every form
        public StoryFaction faction;
        public float commonality = 1f;
        public RulePackDef rules;
        public List<HediffDef> scars = new List<HediffDef>();
        public StoryTrait trait;

        public bool Fits(PastRole role, DeadVariant variant) =>
            roles.Contains(role) && (role != PastRole.DeadReturn || variants.Count == 0 || variants.Contains(variant));

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (rules == null) yield return "no rules";
            if (roles.Count == 0) yield return "no roles";
        }
    }

    // Builds the story told in the "familiar face" letter with the game's own grammar engine. Root symbol
    // r_story; constants for rule conditions: PAWN_gender, hasColony, hasFaction, hasHome, scars, grownUp,
    // hasComrade (all "True"/"False" except the gender).
    public static class PastStories
    {
        public static StoryArcDef PickArc(PastRole role, DeadVariant variant)
        {
            if (!PastLivesMod.Settings.stories) return null;
            return DefDatabase<StoryArcDef>.AllDefs.Where(a => a.Fits(role, variant))
                .TryRandomElementByWeight(a => a.commonality, out var arc) ? arc : null;
        }

        public static Faction PickFaction(StoryArcDef arc)
        {
            if (arc.faction == StoryFaction.None || Find.FactionManager == null) return null;
            var all = Find.FactionManager.AllFactionsListForReading
                .Where(f => !f.IsPlayer && !f.Hidden && !f.defeated && !f.temporary && f.def.humanlikeFaction).ToList();
            if (arc.faction == StoryFaction.Peaceful)
                return all.Where(f => !f.def.permanentEnemy).TryRandomElement(out var peaceful) ? peaceful : null;
            if (arc.faction == StoryFaction.SpacePirate && ModsConfig.OdysseyActive
                && all.Where(f => f.def.defName == "Salvagers").TryRandomElement(out var salvagers))
                return salvagers;
            // Salvagers are kept for space stories; on the ground "pirates" means the ordinary kind.
            var pirates = all.Where(f => f.def.permanentEnemy && f.def.defName != "Salvagers").ToList();
            // Savage tribes are permanent enemies too, but "pirates" means guns and ships.
            if (pirates.Where(f => f.def.techLevel >= TechLevel.Industrial).TryRandomElement(out var modern)) return modern;
            return pirates.TryRandomElement(out var any) ? any : null;
        }

        public static string Tell(StoryArcDef arc, Pawn pawn, ArchivePawn a, Faction faction, bool marks, int yearsGrown)
        {
            var request = new GrammarRequest();
            var c = request.Constants;
            if (PastLivesDefOf.PastLives_StoryCommon != null) request.Includes.Add(PastLivesDefOf.PastLives_StoryCommon);
            request.Includes.Add(arc.rules);
            request.Rules.AddRange(GrammarUtility.RulesForPawn("PAWN", pawn, c));
            c["PAWN_gender"] = pawn.gender.ToString();

            Set(c, "hasColony", a.HasColony);
            if (a.HasColony) request.Rules.Add(new Rule_String("COLONY", a.Colony));

            Set(c, "hasFaction", faction != null);
            if (faction != null) request.Rules.AddRange(GrammarUtility.RulesForFaction("FACTION", faction, c));

            var home = pawn.Faction;
            bool hasHome = home != null && !home.IsPlayer;
            Set(c, "hasHome", hasHome);
            if (hasHome) request.Rules.AddRange(GrammarUtility.RulesForFaction("HOME", home, c));

            Set(c, "scars", marks);
            Set(c, "grownUp", yearsGrown > 0);
            request.Rules.Add(new Rule_String("YEARS", YearsText(yearsGrown)));

            var comrade = FindComrade(pawn, a);
            Set(c, "hasComrade", comrade != null);
            if (comrade != null) request.Rules.AddRange(GrammarUtility.RulesForPawn("COMRADE", comrade, c, addRelationInfoSymbol: false));

            var text = GrammarResolver.Resolve("r_story", request, "PastLives story " + arc.defName);
            // Empty optional sentences ([comrade], [childhood]) leave double spaces behind.
            return GenText.CapitalizeSentences(Regex.Replace(text, @"\s{2,}", " ").Trim());
        }

        private static void Set(Dictionary<string, string> c, string key, bool value) => c[key] = value ? "True" : "False";

        // "1 year", "3 years"; languages with three plural forms (Russian) set the Few form apart from Many.
        public static string YearsText(int n)
        {
            string few = "PastLives_YearsFew".Translate(n), many = "PastLives_YearsMany".Translate(n);
            if (n == 1) return "PastLives_YearsOne".Translate(n);
            if (few == many) return many;
            int d = n % 10, h = n % 100;
            if (d == 1 && h != 11) return "PastLives_YearsOne".Translate(n);
            if (d >= 2 && d <= 4 && (h < 12 || h > 14)) return few;
            return many;
        }

        // A colonist of yours who lived in the same old colony.
        private static Pawn FindComrade(Pawn pawn, ArchivePawn a)
        {
            var comp = GameComponent_PastLives.Instance;
            if (comp == null || !a.HasColony || Find.Maps == null) return null;
            foreach (var map in Find.Maps)
                foreach (var p in map.mapPawns.FreeColonists)
                    if (p != pawn && comp.EntryFor(p)?.colony == a.Colony) return p;
            return null;
        }
    }
}
