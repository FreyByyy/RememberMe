using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace PastLives
{
    // Raw snapshot of a colonist as found in an old save. Holds only strings and numbers so it can be
    // built on a background thread without touching game defs; defs are resolved when the pawn is used.
    public class ArchivePawn
    {
        // Where a pawn came from. Everything except old saves counts as "my list".
        public const string SourceSave = "save";
        public const string SourceList = "list";
        public const string SourcePawnEditor = "pawneditor";
        public const string SourceCharacterEditor = "chareditor";

        // HediffEntry.Body value meaning "whatever body the pawn's race has" (editors store no body def).
        public const string OwnBody = "*";

        public string Key;              // "<world seed>#<pawn id>", stable across autosaves of one colony
        public string Source = SourceSave;
        public string SourceFile;
        public DateTime SourceTimeUtc;
        public string GameVersion;
        public string Colony;
        public string PawnId;
        public string WorldId;          // persistentRandomValue of the world the pawn lived in; null if unknown

        public string RaceDef;
        public string KindDef;
        public string Gender;
        public string NameFirst, NameNick, NameLast, NameSingle;

        public string Childhood, Adulthood;
        public string BodyType, HeadType, HairDef, HairColor, SkinColorOverride, FurDef, FavoriteColor;
        public string Beard, FaceTattoo, BodyTattoo;
        public List<TraitEntry> Traits = new List<TraitEntry>();
        public List<SkillEntry> Skills = new List<SkillEntry>();

        public long AgeBioTicks;
        public long AgeChronoTicks;

        public bool HasGenes;
        public string Xenotype, XenotypeName, XenotypeIcon;
        public List<string> Endogenes = new List<string>();
        public List<string> Xenogenes = new List<string>();

        public List<HediffEntry> Hediffs = new List<HediffEntry>();
        public List<TitleEntry> Titles = new List<TitleEntry>();
        public List<string> IdeoRoles = new List<string>();
        public List<float> Records = new List<float>();
        public float Kills = -1f;       // known kill count when records come by name (editors); -1 = use Records

        public bool Dead;
        public bool Slave;
        public bool CreepJoiner;

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(NameSingle)) return NameSingle;
                if (!string.IsNullOrEmpty(NameNick) && NameNick != NameFirst && NameNick != NameLast)
                    return NameFirst + " '" + NameNick + "' " + NameLast;
                return (NameFirst + " " + NameLast).Trim();
            }
        }

        public bool IsListPawn => Source != null && Source != SourceSave;

        public bool HasColony => !string.IsNullOrEmpty(Colony);

        // Used to recognise one person who is both in an old save and in "my list".
        public string IdentityKey => HasColony ? Colony + "|" + DisplayName : null;

        public string ShortName => !string.IsNullOrEmpty(NameNick) ? NameNick : (NameSingle ?? NameFirst ?? "?");

        public bool IsIdeoLeader => IdeoRoles.Any(r => r.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) >= 0);

        public bool HasOtherIdeoRole => IdeoRoles.Any(r => r.IndexOf("Leader", StringComparison.OrdinalIgnoreCase) < 0);

        public struct TraitEntry { public string Def; public int Degree; }
        public struct SkillEntry { public string Def; public int Level; public string Passion; }
        public struct TitleEntry { public string Def; public string Faction; }
        public struct HediffEntry { public string Def; public string Body; public int PartIndex; public int Level; public float Severity; }

        // --- archive file (de)serialization ---

        public XElement ToXml()
        {
            var e = new XElement("pawn",
                A("key", Key), A("src", Source == SourceSave ? null : Source), A("file", SourceFile), A("time", SourceTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)),
                A("ver", GameVersion), A("colony", Colony), A("id", PawnId), A("world", WorldId), A("race", RaceDef), A("kind", KindDef),
                A("gender", Gender), A("first", NameFirst), A("nick", NameNick), A("last", NameLast), A("single", NameSingle),
                A("child", Childhood), A("adult", Adulthood), A("body", BodyType), A("head", HeadType), A("hair", HairDef),
                A("hairColor", HairColor), A("skin", SkinColorOverride), A("fur", FurDef), A("favColor", FavoriteColor),
                A("beard", Beard), A("faceTat", FaceTattoo), A("bodyTat", BodyTattoo),
                A("ageBio", AgeBioTicks.ToString(CultureInfo.InvariantCulture)), A("ageChrono", AgeChronoTicks.ToString(CultureInfo.InvariantCulture)),
                A("hasGenes", HasGenes ? "1" : null), A("xeno", Xenotype), A("xenoName", XenotypeName), A("xenoIcon", XenotypeIcon),
                A("dead", Dead ? "1" : null), A("slave", Slave ? "1" : null), A("creep", CreepJoiner ? "1" : null),
                A("kills", Kills >= 0f ? Kills.ToString(CultureInfo.InvariantCulture) : null));
            foreach (var t in Traits) e.Add(new XElement("trait", A("def", t.Def), A("deg", t.Degree.ToString(CultureInfo.InvariantCulture))));
            foreach (var s in Skills) e.Add(new XElement("skill", A("def", s.Def), A("lvl", s.Level.ToString(CultureInfo.InvariantCulture)), A("passion", s.Passion)));
            foreach (var g in Endogenes) e.Add(new XElement("endo", A("def", g)));
            foreach (var g in Xenogenes) e.Add(new XElement("xeno", A("def", g)));
            foreach (var h in Hediffs)
                e.Add(new XElement("hediff", A("def", h.Def), A("body", h.Body), A("part", h.PartIndex.ToString(CultureInfo.InvariantCulture)),
                    A("lvl", h.Level.ToString(CultureInfo.InvariantCulture)), A("sev", h.Severity.ToString(CultureInfo.InvariantCulture))));
            foreach (var t in Titles) e.Add(new XElement("title", A("def", t.Def), A("faction", t.Faction)));
            foreach (var r in IdeoRoles) e.Add(new XElement("role", A("def", r)));
            if (Records.Count > 0) e.Add(new XElement("records", string.Join(";", Records.Select(v => v.ToString(CultureInfo.InvariantCulture)))));
            return e;
        }

        public static ArchivePawn FromXml(XElement e)
        {
            var p = new ArchivePawn
            {
                Key = S(e, "key"), Source = S(e, "src") ?? SourceSave, Kills = e.Attribute("kills") != null ? F(e, "kills") : -1f, SourceFile = S(e, "file"), SourceTimeUtc = new DateTime(L(e, "time"), DateTimeKind.Utc),
                GameVersion = S(e, "ver"), Colony = S(e, "colony"), PawnId = S(e, "id"), WorldId = S(e, "world"), RaceDef = S(e, "race"), KindDef = S(e, "kind"),
                Gender = S(e, "gender"), NameFirst = S(e, "first"), NameNick = S(e, "nick"), NameLast = S(e, "last"), NameSingle = S(e, "single"),
                Childhood = S(e, "child"), Adulthood = S(e, "adult"), BodyType = S(e, "body"), HeadType = S(e, "head"), HairDef = S(e, "hair"),
                HairColor = S(e, "hairColor"), SkinColorOverride = S(e, "skin"), FurDef = S(e, "fur"), FavoriteColor = S(e, "favColor"),
                Beard = S(e, "beard"), FaceTattoo = S(e, "faceTat"), BodyTattoo = S(e, "bodyTat"),
                AgeBioTicks = L(e, "ageBio"), AgeChronoTicks = L(e, "ageChrono"),
                HasGenes = S(e, "hasGenes") == "1", Xenotype = S(e, "xeno"), XenotypeName = S(e, "xenoName"), XenotypeIcon = S(e, "xenoIcon"),
                Dead = S(e, "dead") == "1", Slave = S(e, "slave") == "1", CreepJoiner = S(e, "creep") == "1",
            };
            foreach (var t in e.Elements("trait")) p.Traits.Add(new TraitEntry { Def = S(t, "def"), Degree = (int)L(t, "deg") });
            foreach (var s in e.Elements("skill")) p.Skills.Add(new SkillEntry { Def = S(s, "def"), Level = (int)L(s, "lvl"), Passion = S(s, "passion") });
            foreach (var g in e.Elements("endo")) p.Endogenes.Add(S(g, "def"));
            foreach (var g in e.Elements("xeno")) p.Xenogenes.Add(S(g, "def"));
            foreach (var h in e.Elements("hediff"))
                p.Hediffs.Add(new HediffEntry { Def = S(h, "def"), Body = S(h, "body"), PartIndex = (int)L(h, "part"), Level = (int)L(h, "lvl"), Severity = F(h, "sev") });
            foreach (var t in e.Elements("title")) p.Titles.Add(new TitleEntry { Def = S(t, "def"), Faction = S(t, "faction") });
            foreach (var r in e.Elements("role")) p.IdeoRoles.Add(S(r, "def"));
            var rec = e.Element("records")?.Value;
            if (!string.IsNullOrEmpty(rec))
                p.Records = rec.Split(';').Select(v => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f).ToList();
            return p;
        }

        private static XAttribute A(string name, string value) => value == null ? null : new XAttribute(name, value);
        private static string S(XElement e, string name) => e.Attribute(name)?.Value;
        private static long L(XElement e, string name) => long.TryParse(S(e, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
        private static float F(XElement e, string name) => float.TryParse(S(e, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }
}
