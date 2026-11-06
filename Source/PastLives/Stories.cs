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

    // What kind of people the teller lives among now, so a tribal raider is not told as a pirate.
    public enum HomeKind
    {
        None,           // no faction, the player's, or the ancients of a cryptosleep casket
        Pirate,
        Tribal,
        Outlander,
        Empire,
        Salvager,
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
        public List<HomeKind> homes = new List<HomeKind>();             // empty = told among anyone
        public bool grownUpOnly;                                         // only for people who were children back then
        public List<string> quests = new List<string>();                 // told only in these quests (QuestScriptDef names); empty = any
        public float commonality = 1f;
        public RulePackDef rules;
        public List<HediffDef> scars = new List<HediffDef>();
        public StoryTrait trait;

        public bool Fits(PastRole role, DeadVariant variant) =>
            roles.Contains(role) && (role != PastRole.DeadReturn || variants.Count == 0 || variants.Contains(variant));

        public bool FitsHome(HomeKind home) => homes.Count == 0 || homes.Contains(home);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (rules == null) yield return "no rules";
            if (roles.Count == 0) yield return "no roles";
        }
    }

    // Builds the story told in the "familiar face" letter with the game's own grammar engine. Root symbol
    // r_story; constants for rule conditions: PAWN_gender, hasColony, hasFaction, hasHome, scars, grownUp,
    // hasComrade, defender (all "True"/"False" except the gender).
    public static class PastStories
    {
        // Stories that fit the role, the form of a dead return and the teller's people; among them, the ones not
        // heard lately are likelier (see GameComponent_PastLives.ArcWeight). remember = false leaves the rotation as it is.
        public static StoryArcDef PickArc(PastRole role, DeadVariant variant, Pawn pawn, bool grownUp = false, bool remember = true, string quest = null)
        {
            if (!PastLivesMod.Settings.stories) return null;
            // A baby has no story to tell yet.
            if (pawn != null && pawn.DevelopmentalStage.Baby()) return null;
            var home = HomeOf(pawn?.Faction);
            var fitting = DefDatabase<StoryArcDef>.AllDefs.Where(a => a.Fits(role, variant) && (grownUp || !a.grownUpOnly)).ToList();
            var forHome = fitting.Where(a => a.FitsHome(home)).ToList();
            if (forHome.Count > 0) fitting = forHome;
            // Stories written for particular quests (a crashed pod, a downed refugee) stay with them; a quest no
            // story names, e.g. one added by another mod, may hear any of them.
            var forQuest = fitting.Where(a => a.quests.Count == 0 || (quest != null && a.quests.Contains(quest))).ToList();
            if (forQuest.Count > 0) fitting = forQuest;
            if (fitting.Count == 0) return null;

            var comp = GameComponent_PastLives.Instance;
            float Weight(StoryArcDef a) => a.commonality * (comp?.ArcWeight(a) ?? 1f);
            // Everything that fits was told just now: the rotation starts over rather than going silent.
            var arc = fitting.Any(a => Weight(a) > 0f) ? fitting.RandomElementByWeight(Weight) : fitting.RandomElementByWeight(a => a.commonality);
            if (remember) comp?.Notify_ArcTold(arc);
            return arc;
        }

        public static HomeKind HomeOf(Faction f)
        {
            if (f == null || f.IsPlayer || f.def == null || !f.def.humanlikeFaction || f.def.defName.StartsWith("Ancients")) return HomeKind.None;
            if (f.def.defName == "Salvagers") return HomeKind.Salvager;
            if (f.def.HasRoyalTitles) return HomeKind.Empire;
            if (f.def.techLevel <= TechLevel.Neolithic) return HomeKind.Tribal;
            if (f.def.permanentEnemy) return HomeKind.Pirate;
            return HomeKind.Outlander;
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

        public static string Tell(StoryArcDef arc, Pawn pawn, ArchivePawn a, Faction faction, bool marks, int yearsGrown, PastRole role = PastRole.None)
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

            // Raider stories also serve the people met in their own camps; only the last sentence differs.
            Set(c, "defender", role == PastRole.Defender);
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
