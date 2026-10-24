using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace PastLives
{
    // "My list": one file per pawn in SaveData/PastLives/MyList. Files written by the Remember button use
    // the archive format; a pawn file in the game's own format (e.g. copied from Pawn Editor) works too.
    // Deleting a file removes the pawn from the list.
    public static class MyListFolder
    {
        public static List<ArchivePawn> Load(string folder, Action<string> warn)
        {
            var result = new List<ArchivePawn>();
            if (!Directory.Exists(folder)) return result;
            foreach (var file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    var root = XDocument.Load(file).Root;
                    ArchivePawn p;
                    if (root?.Name.LocalName == "pawn") p = ArchivePawn.FromXml(root);
                    else
                    {
                        p = ArchiveScanner.ScanPawnFile(file);
                        if (p != null) p.Key = "file#" + Path.GetFileName(file);
                    }
                    if (p?.Key == null) continue;
                    p.Source = ArchivePawn.SourceList;
                    p.SourceFile = file;
                    result.Add(p);
                }
                catch (Exception ex)
                {
                    warn?.Invoke("my list file " + Path.GetFileName(file) + " skipped: " + ex.Message);
                }
            }
            return result;
        }

        public static string Save(string folder, ArchivePawn p)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileNameFor(p));
            new XDocument(p.ToXml()).Save(path);
            return path;
        }

        // Readable and unique: "Chris Moon (Веллари) 3f2a91c0.xml".
        private static string FileNameFor(ArchivePawn p)
        {
            var label = p.DisplayName + (p.HasColony ? " (" + p.Colony + ")" : "");
            foreach (var c in Path.GetInvalidFileNameChars()) label = label.Replace(c, '_');
            if (label.Length > 80) label = label.Substring(0, 80);
            byte[] hash;
            using (var sha = SHA1.Create()) hash = sha.ComputeHash(Encoding.UTF8.GetBytes(p.Key));
            return label.Trim() + " " + string.Concat(hash.Take(4).Select(b => b.ToString("x2"))) + ".xml";
        }
    }
}
