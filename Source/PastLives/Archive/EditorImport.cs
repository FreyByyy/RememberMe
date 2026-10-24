using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PastLives
{
    // Optional imports from other mods' saved pawns. Neither mod is required: if its files are not
    // there, nothing is imported. Only files are read; the other mods' code is never called.
    public static class EditorImport
    {
        // Pawn Editor saves each pawn as a separate XML file in the game's own format.
        public static List<ArchivePawn> PawnEditor(string folder, Action<string> warn)
        {
            var result = new List<ArchivePawn>();
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    var p = ArchiveScanner.ScanPawnFile(file);
                    if (p == null) continue;
                    p.Source = ArchivePawn.SourcePawnEditor;
                    p.Key = "pe#" + p.PawnId + "#" + p.DisplayName;
                    result.Add(p);
                }
                catch (Exception ex)
                {
                    warn?.Invoke("Pawn Editor file " + Path.GetFileName(file) + " skipped: " + ex.Message);
                }
            }
            return result;
        }

        // Character Editor keeps its pawn slots in one text file, one slot per line, 57 fields separated
        // by '*'. The format is undocumented and may change, hence "experimental" in the settings.
        // Field numbers follow Character Editor's PresetPawn.Param (P00_id ... P56_favColorDef).
        public static List<ArchivePawn> CharacterEditor(string slotsFile, ICollection<string> playerFactionDefs,
            IDictionary<string, string> kindToRace, Action<string> warn)
        {
            var result = new List<ArchivePawn>();
            if (!File.Exists(slotsFile)) return result;
            var lines = File.ReadAllText(slotsFile, Encoding.UTF8).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    var p = ParseCharacterEditorSlot(line, playerFactionDefs, kindToRace);
                    if (p == null) continue;
                    p.SourceFile = Path.GetFileName(slotsFile) + ":" + (i + 1);
                    p.SourceTimeUtc = File.GetLastWriteTimeUtc(slotsFile);
                    result.Add(p);
                }
                catch (Exception ex)
                {
                    warn?.Invoke("Character Editor slot " + (i + 1) + " skipped: " + ex.Message);
                }
            }
            return result;
        }

        private static ArchivePawn ParseCharacterEditorSlot(string line, ICollection<string> playerFactionDefs, IDictionary<string, string> kindToRace)
        {
            var f = line.Split('*');
            if (f.Length < 49) return null;   // older or broken slot
            string F(int i) => i < f.Length && f[i].Length > 0 ? f[i] : null;

            if (F(54) != null) return null;   // mutants (ghouls) are not people for our purposes

            var p = new ArchivePawn
            {
                Source = ArchivePawn.SourceCharacterEditor,
                KindDef = F(1),
                PawnId = F(0),
                AgeBioTicks = Long(F(4)),
                AgeChronoTicks = Long(F(5)),
                Gender = F(6) == "2" ? "Female" : F(6) == "1" ? "Male" : "None",
                BodyType = F(7),
                HairDef = F(12),
                HairColor = HexToRgba(F(14)),
                SkinColorOverride = HexToRgba(F(17)),
                Childhood = F(23),
                Adulthood = F(24),
                Beard = F(35),
                FaceTattoo = F(36),
                BodyTattoo = F(37),
                HeadType = F(47),
                Xenotype = F(48),
                XenotypeName = F(49),
                FavoriteColor = F(56),
            };
            if (p.KindDef == null || !kindToRace.TryGetValue(p.KindDef, out var race)) return null;
            p.RaceDef = race;

            var name = F(3) ?? F(0) ?? "";
            var parts = name.Split('?');
            if (parts.Length == 3) { p.NameFirst = parts[0]; p.NameNick = parts[1]; p.NameLast = parts[2]; }
            else p.NameSingle = name;

            // "Colony name|FactionDef": only a player faction is a colony.
            var faction = (F(2) ?? "").Split('|');
            if (faction.Length == 2 && (playerFactionDefs.Contains(faction[1]) || faction[1].StartsWith("Player", StringComparison.Ordinal)))
                p.Colony = faction[0];

            foreach (var s in Items(F(20)))
            {
                var x = s.Split('|');
                if (x.Length < 3) continue;
                p.Skills.Add(new ArchivePawn.SkillEntry { Def = x[0], Level = Int(x[1]), Passion = PassionName(x[2]) });
            }
            foreach (var s in Items(F(21)))
            {
                var x = s.Split('|');
                p.Traits.Add(new ArchivePawn.TraitEntry { Def = x[0], Degree = x.Length > 1 ? Int(x[1]) : 0 });
            }
            foreach (var s in Items(F(22)))
            {
                // def|severity|partDef|partIndex|permanent|level|...: the part index is in the pawn's own body.
                var x = s.Split('|');
                if (x.Length < 6) continue;
                p.Hediffs.Add(new ArchivePawn.HediffEntry
                {
                    Def = x[0],
                    Severity = Float(x[1]),
                    Body = x[3].Length > 0 ? ArchivePawn.OwnBody : null,
                    PartIndex = x[3].Length > 0 ? Int(x[3]) : -1,
                    Level = Math.Max(1, Int(x[5])),
                });
            }
            foreach (var s in Items(F(29)))
            {
                var x = s.Split('|');
                if (x.Length >= 2 && x[0] == "Kills") p.Kills = Float(x[1]);
            }
            if (F(34) != null) p.Titles.Add(new ArchivePawn.TitleEntry { Def = F(34) });

            var endo = Items(F(50)).ToList();
            var xeno = Items(F(51)).ToList();
            if (p.Xenotype != null || endo.Count > 0 || xeno.Count > 0)
            {
                p.HasGenes = true;
                p.Endogenes.AddRange(endo);
                p.Xenogenes.AddRange(xeno);
            }

            p.Key = "ce#" + name + "#" + (F(2) ?? "");
            return p;
        }

        private static IEnumerable<string> Items(string s) => string.IsNullOrEmpty(s)
            ? Enumerable.Empty<string>()
            : s.Split(':').Where(x => x.Length > 0);

        private static string PassionName(string s)
        {
            switch (s)
            {
                case "0": return "None";
                case "1": return "Minor";
                case "2": return "Major";
                default: return s;
            }
        }

        // "7A-4D-2B-FF" -> "RGBA(0.478, 0.302, 0.169, 1.000)", the form the game itself parses.
        private static string HexToRgba(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            var b = hex.Split('-');
            if (b.Length != 4) return null;
            var c = new float[4];
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(b[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
                c[i] = v / 255f;
            }
            return string.Format(CultureInfo.InvariantCulture, "RGBA({0:0.000}, {1:0.000}, {2:0.000}, {3:0.000})", c[0], c[1], c[2], c[3]);
        }

        private static int Int(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        private static long Long(string s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
        private static float Float(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v
            : float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) ? v : 0f;
    }
}
