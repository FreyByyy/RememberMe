using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PastLives
{
    // The Remember / Forget button on colonists, and the snapshot it stores in "my list".
    public static class MyList
    {
        // Same key form as old saves ("<world seed>#<pawn id>"), so a remembered colonist and the same
        // colonist found later in a save are recognised as one person.
        public static string KeyFor(Pawn p) => (Find.World?.info?.seedString ?? "world") + "#" + p.ThingID;

        public static bool Contains(Pawn p) => PastLivesArchive.MyListEntry(KeyFor(p)) != null;

        public static void Remember(Pawn p)
        {
            var a = Snapshot(p);
            a.Key = KeyFor(p);
            PastLivesArchive.AddToMyList(a);
            Messages.Message("PastLives_Remembered".Translate(p.Named("PAWN")), p, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        public static void Forget(Pawn p)
        {
            PastLivesArchive.RemoveFromMyList(KeyFor(p));
            Messages.Message("PastLives_Forgotten".Translate(p.Named("PAWN")), p, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        public static ArchivePawn Snapshot(Pawn p)
        {
            var a = new ArchivePawn
            {
                Source = ArchivePawn.SourceList,
                PawnId = p.ThingID,
                WorldId = Find.World?.info?.persistentRandomValue.ToString(CultureInfo.InvariantCulture),
                RaceDef = p.def.defName,
                KindDef = p.kindDef?.defName,
                Gender = p.gender.ToString(),
                Colony = p.Faction?.IsPlayer == true ? p.Faction.Name : Faction.OfPlayerSilentFail?.Name,
                GameVersion = VersionControl.CurrentVersionStringWithRev,
                SourceTimeUtc = System.DateTime.UtcNow,
                AgeBioTicks = p.ageTracker.AgeBiologicalTicks,
                AgeChronoTicks = p.ageTracker.AgeChronologicalTicks,
                Dead = p.Dead,
                Slave = p.IsSlave,
                CreepJoiner = p.creepjoiner != null,
            };

            if (p.Name is NameTriple triple) { a.NameFirst = triple.First; a.NameNick = triple.Nick; a.NameLast = triple.Last; }
            else if (p.Name != null) a.NameSingle = p.Name.ToStringFull;

            var story = p.story;
            if (story != null)
            {
                a.Childhood = story.Childhood?.defName;
                a.Adulthood = story.Adulthood?.defName;
                a.BodyType = story.bodyType?.defName;
                a.HeadType = story.headType?.defName;
                a.HairDef = story.hairDef?.defName;
                a.HairColor = Rgba(story.HairColor);
                a.SkinColorOverride = story.skinColorOverride.HasValue ? Rgba(story.skinColorOverride.Value) : null;
                a.FurDef = story.furDef?.defName;
                a.FavoriteColor = story.favoriteColor?.defName;
                foreach (var t in story.traits.allTraits.Where(t => t.sourceGene == null))
                    a.Traits.Add(new ArchivePawn.TraitEntry { Def = t.def.defName, Degree = t.Degree });
            }
            if (p.style != null)
            {
                a.Beard = p.style.beardDef?.defName;
                a.FaceTattoo = p.style.FaceTattoo?.defName;
                a.BodyTattoo = p.style.BodyTattoo?.defName;
            }
            if (p.skills != null)
                foreach (var s in p.skills.skills)
                    a.Skills.Add(new ArchivePawn.SkillEntry { Def = s.def.defName, Level = s.levelInt, Passion = s.passion.ToString() });

            if (p.genes != null)
            {
                a.HasGenes = true;
                a.Xenotype = p.genes.Xenotype?.defName;
                a.XenotypeName = p.genes.xenotypeName;
                a.XenotypeIcon = p.genes.iconDef?.defName;
                a.Endogenes.AddRange(p.genes.Endogenes.Select(g => g.def.defName));
                a.Xenogenes.AddRange(p.genes.Xenogenes.Select(g => g.def.defName));
            }

            var body = p.RaceProps.body;
            foreach (var h in p.health.hediffSet.hediffs.Where(h => h.def.countsAsAddedPartOrImplant))
                a.Hediffs.Add(new ArchivePawn.HediffEntry
                {
                    Def = h.def.defName,
                    Body = body.defName,
                    PartIndex = h.Part != null ? body.GetIndexOfPart(h.Part) : -1,
                    Level = h is Hediff_Level lvl ? lvl.level : 1,
                    Severity = h.Severity,
                });

            if (p.royalty != null)
                foreach (var t in p.royalty.AllTitlesForReading)
                    a.Titles.Add(new ArchivePawn.TitleEntry { Def = t.def.defName, Faction = t.faction?.GetUniqueLoadID() });

            var role = p.Ideo?.GetRole(p);
            if (role != null) a.IdeoRoles.Add(role.def.defName);

            if (p.records != null) a.Kills = p.records.GetValue(RecordDefOf.Kills);
            return a;
        }

        private static string Rgba(Color c) =>
            string.Format(CultureInfo.InvariantCulture, "RGBA({0:0.000}, {1:0.000}, {2:0.000}, {3:0.000})", c.r, c.g, c.b, c.a);
    }

    [StaticConstructorOnStartup]
    public static class MyListTextures
    {
        public static readonly Texture2D Remember = ContentFinder<Texture2D>.Get("UI/PastLives/Remember");
        public static readonly Texture2D Forget = ContentFinder<Texture2D>.Get("UI/PastLives/Forget");
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_PawnGizmos
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            var p = __instance;
            if (!PastLivesMod.Settings.enabled || !p.RaceProps.Humanlike || p.Dead || !(p.IsColonist || p.IsSlaveOfColony)) yield break;

            bool remembered = MyList.Contains(p);
            yield return new Command_Action
            {
                defaultLabel = (remembered ? "PastLives_Forget" : "PastLives_Remember").Translate(),
                defaultDesc = (remembered ? "PastLives_ForgetDesc" : "PastLives_RememberDesc").Translate(p.Named("PAWN")),
                icon = remembered ? MyListTextures.Forget : MyListTextures.Remember,
                action = () => { if (remembered) MyList.Forget(p); else MyList.Remember(p); },
            };
        }
    }
}
