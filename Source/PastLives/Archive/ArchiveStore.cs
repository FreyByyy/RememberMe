using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PastLives
{
    // The cumulative archive of past colonists. It only grows: deleting an old save does not erase
    // the people who lived in it. Autosaves of one colony collapse into one entry per colonist,
    // taken from the most recent save.
    public class ArchiveStore
    {
        public const int FormatVersion = 4;   // bump to force a full re-read after scanner fixes

        public Dictionary<string, ArchivePawn> Pawns = new Dictionary<string, ArchivePawn>();
        public Dictionary<string, string> FileStamps = new Dictionary<string, string>();   // file name -> "size:mtime"

        public static ArchiveStore Load(string path)
        {
            var store = new ArchiveStore();
            if (!File.Exists(path)) return store;
            var doc = XDocument.Load(path);
            if ((int?)doc.Root.Attribute("format") != FormatVersion) return store;
            foreach (var f in doc.Root.Elements("file")) store.FileStamps[f.Attribute("name").Value] = f.Attribute("stamp").Value;
            foreach (var e in doc.Root.Elements("pawn"))
            {
                var p = ArchivePawn.FromXml(e);
                if (p.Key != null) store.Pawns[p.Key] = p;
            }
            return store;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var root = new XElement("pastLivesArchive", new XAttribute("format", FormatVersion));
            foreach (var kv in FileStamps.OrderBy(k => k.Key)) root.Add(new XElement("file", new XAttribute("name", kv.Key), new XAttribute("stamp", kv.Value)));
            foreach (var p in Pawns.Values.OrderBy(p => p.Colony).ThenBy(p => p.DisplayName)) root.Add(p.ToXml());
            var tmp = path + ".tmp";
            new XDocument(root).Save(tmp);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public ArchiveStore Clone()
        {
            return new ArchiveStore
            {
                Pawns = new Dictionary<string, ArchivePawn>(Pawns),
                FileStamps = new Dictionary<string, string>(FileStamps),
            };
        }

        public static string Stamp(FileInfo f) => f.Length.ToString(CultureInfo.InvariantCulture) + ":" + f.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

        // Pawns imported from editor mods are kept here too, so they survive the other mod losing its list.
        public void MergeImported(IEnumerable<ArchivePawn> pawns)
        {
            foreach (var p in pawns)
                if (p.Key != null) Pawns[p.Key] = p;
        }

        public int RemoveSource(string source)
        {
            var keys = Pawns.Values.Where(p => p.Source == source).Select(p => p.Key).ToList();
            foreach (var k in keys) Pawns.Remove(k);
            return keys.Count;
        }

        // Scans every save that is new or changed since the last run. Returns the number of files parsed.
        public int Update(string savesFolder, ICollection<string> playerFactionDefs, Action<string> warn, Action<int, int> progress = null)
        {
            if (!Directory.Exists(savesFolder)) return 0;
            var files = new DirectoryInfo(savesFolder).GetFiles("*.rws")
                .Where(f => !FileStamps.TryGetValue(f.Name, out var s) || s != Stamp(f))
                .OrderBy(f => f.LastWriteTimeUtc)   // oldest first, so newer saves of a colony win
                .ToList();
            for (int i = 0; i < files.Count; i++)
            {
                progress?.Invoke(i, files.Count);
                var f = files[i];
                try
                {
                    foreach (var p in ArchiveScanner.ScanFile(f.FullName, playerFactionDefs))
                    {
                        if (Pawns.TryGetValue(p.Key, out var old) && old.SourceTimeUtc > p.SourceTimeUtc) continue;
                        Pawns[p.Key] = p;
                    }
                }
                catch (Exception ex)
                {
                    warn?.Invoke("could not read save " + f.Name + ": " + ex.Message);
                }
                // Broken saves are stamped too, so they are not re-read on every launch.
                FileStamps[f.Name] = Stamp(f);
            }
            return files.Count;
        }
    }
}
