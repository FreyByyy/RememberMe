using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PastLives
{
    // Streams a .rws save and pulls out the player's colonists. The save is never fully loaded:
    // elements are built as they are read and every finished <li>/<thing> is dropped right away,
    // so memory stays small even for 30+ MB saves.
    public static class ArchiveScanner
    {
        private class PendingPawn
        {
            public ArchivePawn Pawn;
            public string Faction;
            public long BirthAbsTicks;
        }

        public static List<ArchivePawn> ScanFile(string path, ICollection<string> playerFactionDefs)
        {
            var info = new FileInfo(path);
            var pending = new List<PendingPawn>();
            var playerFactions = new Dictionary<string, string>();   // "Faction_16" -> colony name
            var roles = new Dictionary<string, List<string>>();       // "Thing_Human12" -> role defs
            var openPawns = new HashSet<XElement>();                  // pawn elements still being read
            string version = null, seed = null;
            long ticksGame = 0, gameStartAbsTick = 0;

            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Ignore,
                CheckCharacters = false,
            };

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            using (var reader = XmlReader.Create(stream, settings))
            {
                var stack = new Stack<XElement>();
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                        {
                            var el = new XElement(reader.LocalName);
                            if (reader.HasAttributes)
                            {
                                while (reader.MoveToNextAttribute()) el.SetAttributeValue(reader.LocalName, reader.Value);
                                reader.MoveToElement();
                            }
                            if (stack.Count > 0) stack.Peek().Add(el);
                            if (reader.IsEmptyElement) OnEnd(el);
                            else stack.Push(el);
                            break;
                        }
                        case XmlNodeType.Text:
                        case XmlNodeType.CDATA:
                            if (stack.Count > 0) stack.Peek().Add(new XText(reader.Value));
                            break;
                        case XmlNodeType.EndElement:
                            OnEnd(stack.Pop());
                            break;
                    }
                }
            }

            void OnEnd(XElement el)
            {
                var name = el.Name.LocalName;
                var parent = el.Parent?.Name.LocalName;
                switch (name)
                {
                    case "gameVersion" when version == null: version = el.Value; return;
                    case "seedString" when parent == "info": seed = el.Value; return;
                    case "ticksGame" when parent == "tickManager": long.TryParse(el.Value, out ticksGame); return;
                    case "gameStartAbsTick" when parent == "tickManager": long.TryParse(el.Value, out gameStartAbsTick); return;
                    case "kindDef" when parent == "li" || parent == "thing": openPawns.Add(el.Parent); return;
                }
                if (name != "li" && name != "thing") return;
                openPawns.Remove(el);

                if (parent == "allFactions")
                {
                    var def = el.Element("def")?.Value ?? "";
                    if (playerFactionDefs.Contains(def) || def.StartsWith("Player", StringComparison.Ordinal))
                        playerFactions["Faction_" + el.Element("loadID")?.Value] = el.Element("name")?.Value;
                }
                else if (el.Attribute("Class")?.Value == "Precept_RoleSingle")
                {
                    var holder = el.Element("chosenPawn")?.Element("pawn")?.Value;
                    var def = el.Element("def")?.Value;
                    if (!string.IsNullOrEmpty(holder) && holder != "null" && def != null)
                    {
                        if (!roles.TryGetValue(holder, out var list)) roles[holder] = list = new List<string>();
                        list.Add(def);
                    }
                }
                else if (IsColonistCandidate(el))
                {
                    var p = ReadPawn(el, parent == "pawnsDead", out var faction, out var birthAbs);
                    if (p != null) pending.Add(new PendingPawn { Pawn = p, Faction = faction, BirthAbsTicks = birthAbs });
                }
                // List items inside a pawn (traits, skills, genes...) must stay until the pawn itself is read.
                if (el.Parent != null && !InsideOpenPawn(el)) el.Remove();
            }

            bool InsideOpenPawn(XElement el)
            {
                if (openPawns.Count == 0) return false;
                for (var a = el.Parent; a != null; a = a.Parent)
                    if (openPawns.Contains(a)) return true;
                return false;
            }

            var result = new List<ArchivePawn>();
            long ticksAbs = gameStartAbsTick + ticksGame;
            foreach (var pp in pending)
            {
                if (pp.Faction == null || !playerFactions.TryGetValue(pp.Faction, out var colony)) continue;
                var p = pp.Pawn;
                p.Colony = string.IsNullOrWhiteSpace(colony) ? Path.GetFileNameWithoutExtension(path) : colony;
                p.GameVersion = version;
                p.SourceFile = Path.GetFileName(path);
                p.SourceTimeUtc = info.LastWriteTimeUtc;
                p.Key = (seed ?? Path.GetFileNameWithoutExtension(path)) + "#" + p.PawnId;
                p.AgeChronoTicks = Math.Max(p.AgeBioTicks, ticksAbs - pp.BirthAbsTicks);
                if (roles.TryGetValue("Thing_" + p.PawnId, out var r)) p.IdeoRoles.AddRange(r.Distinct());
                result.Add(p);
            }
            // One pawn can show up twice in a save (e.g. in a caravan and as a world pawn): keep the first.
            return result.GroupBy(p => p.Key).Select(g => g.First()).ToList();
        }

        private static bool IsColonistCandidate(XElement el)
        {
            if (el.Element("kindDef") == null || el.Element("ageTracker") == null || el.Element("def") == null) return false;
            var story = el.Element("story");
            if (story == null || story.Attribute("IsNull") != null) return false;
            var skills = el.Element("skills");   // vehicles and other story-bearing non-people have no skills
            if (skills == null || skills.Attribute("IsNull") != null) return false;
            var mutant = el.Element("mutant");
            if (mutant != null && mutant.Attribute("IsNull") == null) return false;   // ghouls and other mutants
            return true;
        }

        private static ArchivePawn ReadPawn(XElement el, bool inDeadList, out string faction, out long birthAbs)
        {
            faction = el.Element("faction")?.Value;
            var p = new ArchivePawn
            {
                PawnId = el.Element("id")?.Value,
                RaceDef = el.Element("def")?.Value,
                KindDef = el.Element("kindDef")?.Value,
                Gender = el.Element("gender")?.Value ?? "Male",   // the game omits the default value, Male
            };
            if (p.PawnId == null) { birthAbs = 0; return null; }

            var name = el.Element("name");
            if (name != null && name.Attribute("IsNull") == null)
            {
                if (name.Attribute("Class")?.Value == "NameSingle") p.NameSingle = name.Element("name")?.Value;
                else
                {
                    p.NameFirst = name.Element("first")?.Value;
                    p.NameNick = name.Element("nick")?.Value;
                    p.NameLast = name.Element("last")?.Value;
                }
            }

            var story = el.Element("story");
            p.Childhood = V(story, "childhood");
            p.Adulthood = V(story, "adulthood");
            p.BodyType = V(story, "bodyType");
            p.HeadType = V(story, "headType");
            p.HairDef = V(story, "hairDef");
            p.HairColor = V(story, "hairColor");
            p.SkinColorOverride = V(story, "skinColorOverride");
            p.FurDef = V(story, "furDef");
            p.FavoriteColor = V(story, "favoriteColorDef");
            foreach (var t in Lis(story?.Element("traits")?.Element("allTraits")))
            {
                // Traits granted by genes come back with the genes, so only the pawn's own traits are kept.
                var src = V(t, "sourceGene");
                if (src != null && src != "null") continue;
                p.Traits.Add(new ArchivePawn.TraitEntry { Def = V(t, "def"), Degree = I(V(t, "degree")) });
            }

            var style = el.Element("style");
            p.Beard = V(style, "beardDef");
            p.FaceTattoo = V(style, "faceTattoo");
            p.BodyTattoo = V(style, "bodyTattoo");

            var age = el.Element("ageTracker");
            p.AgeBioTicks = Lg(V(age, "ageBiologicalTicks"));
            birthAbs = Lg(V(age, "birthAbsTicks"));

            foreach (var s in Lis(el.Element("skills")?.Element("skills")))
                p.Skills.Add(new ArchivePawn.SkillEntry { Def = V(s, "def"), Level = I(V(s, "level")), Passion = V(s, "passion") });

            var genes = el.Element("genes");
            if (genes != null && genes.Attribute("IsNull") == null)
            {
                p.HasGenes = true;
                p.Xenotype = V(genes, "xenotype");
                p.XenotypeName = V(genes, "xenotypeName");
                p.XenotypeIcon = V(genes, "iconDef");
                p.Endogenes.AddRange(Lis(genes.Element("endogenes")).Select(g => V(g, "def")).Where(d => d != null));
                p.Xenogenes.AddRange(Lis(genes.Element("xenogenes")).Select(g => V(g, "def")).Where(d => d != null));
            }

            var health = el.Element("healthTracker");
            p.Dead = inDeadList || V(health, "healthState") == "Dead";
            foreach (var h in Lis(health?.Element("hediffSet")?.Element("hediffs")))
            {
                var part = h.Element("part");
                p.Hediffs.Add(new ArchivePawn.HediffEntry
                {
                    Def = V(h, "def"),
                    Body = V(part, "body"),
                    PartIndex = part == null || part.Attribute("IsNull") != null ? -1 : I(V(part, "index"), -1),
                    Level = I(V(h, "level"), 1),
                    Severity = F(V(h, "severity")),
                });
            }

            foreach (var t in Lis(el.Element("royalty")?.Element("titles")))
                p.Titles.Add(new ArchivePawn.TitleEntry { Def = V(t, "def"), Faction = V(t, "faction") });

            var guest = el.Element("guest");
            var slaveFaction = V(guest, "slaveFaction");
            p.Slave = V(guest, "guestStatus") == "Slave" || (slaveFaction != null && slaveFaction != "null");

            var creep = el.Element("creepjoiner");
            p.CreepJoiner = creep != null && creep.Attribute("IsNull") == null;

            foreach (var v in Lis(el.Element("records")?.Element("records")?.Element("vals")))
                p.Records.Add(F(v.Value));

            return p;
        }

        private static IEnumerable<XElement> Lis(XElement e) => e == null ? Enumerable.Empty<XElement>() : e.Elements("li");

        private static string V(XElement e, string child)
        {
            var c = e?.Element(child);
            if (c == null || c.Attribute("IsNull") != null) return null;
            return c.Value;
        }

        private static int I(string s, int def = 0) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;
        private static long Lg(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
        private static float F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
    }
}
