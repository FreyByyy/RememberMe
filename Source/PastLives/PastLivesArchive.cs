using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace PastLives
{
    // Started from a separate [StaticConstructorOnStartup] class on purpose: the worker thread touches
    // PastLivesArchive's static fields, so waiting for it inside that class's own static constructor would deadlock.
    [StaticConstructorOnStartup]
    public static class PastLivesStartup
    {
        static PastLivesStartup() => PastLivesArchive.Initialize();
    }

    // Owns the archive at runtime. Loading and scanning run on a worker thread; the game thread only
    // ever sees finished collections, swapped in whole.
    //
    // Three kinds of pawns:
    //  - old saves (cached in archive.xml, cumulative);
    //  - editor imports from Pawn Editor / Character Editor (also kept in archive.xml, so they survive
    //    the other mod losing its list);
    //  - "my list": files in SaveData/PastLives/MyList, written by the Remember button.
    // Everything except old saves counts as "my list" for weighting, "only my list" and repeats.
    public static class PastLivesArchive
    {
        private static volatile ArchiveStore store = new ArchiveStore();
        private static volatile List<ArchivePawn> myList = new List<ArchivePawn>();
        private static volatile bool scanning;
        private static volatile string progress;
        private static readonly ConcurrentQueue<string> pendingLog = new ConcurrentQueue<string>();

        private static string archivePath;
        private static string savesFolder;
        private static HashSet<string> playerFactionDefs;
        private static Dictionary<string, string> kindToRace;

        public static string MyListFolderPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "PastLives", "MyList");
        private static string PawnEditorFolder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PawnEditor", "Pawn");
        private static string CharacterEditorSlots => Path.Combine(GenFilePaths.SaveDataFolderPath, "CharacterEditor", "pawnslots.txt");

        public static bool Scanning => scanning;
        public static string Progress => progress;

        // Every pawn currently eligible, "my list" first. A person who is both in "my list" and in an old
        // save (same colony and name) appears once, as the list version.
        public static IEnumerable<ArchivePawn> All
        {
            get
            {
                var settings = PastLivesMod.Settings;
                var list = myList;
                var saved = store;
                var keys = new HashSet<string>();
                var people = new HashSet<string>();
                foreach (var p in list)
                {
                    keys.Add(p.Key);
                    if (p.IdentityKey != null) people.Add(p.IdentityKey);
                    yield return p;
                }
                foreach (var p in saved.Pawns.Values)
                {
                    if (!p.IsListPawn || !settings.SourceEnabled(p.Source) || !keys.Add(p.Key)) continue;
                    if (p.IdentityKey != null) people.Add(p.IdentityKey);
                    yield return p;
                }
                if (settings.onlyMyList) yield break;
                foreach (var p in saved.Pawns.Values)
                {
                    if (p.IsListPawn || keys.Contains(p.Key) || (p.IdentityKey != null && people.Contains(p.IdentityKey))) continue;
                    yield return p;
                }
            }
        }

        // Counts by source for the settings window, regardless of toggles.
        public static IEnumerable<ArchivePawn> SavedColonists => store.Pawns.Values.Where(p => !p.IsListPawn);
        public static int CountMyList => myList.Count;

        // Dev tool; safe from any thread (goes through the pending log).
        public static void LogStats()
        {
            var saved = store.Pawns.Values.Where(p => !p.IsListPawn).ToList();
            var lines = saved.GroupBy(p => p.Colony).OrderBy(g => g.Key)
                .Select(g => "  " + g.Key + ": " + g.Count(p => !p.Dead) + " living, " + g.Count(p => p.Dead) + " dead");
            pendingLog.Enqueue("[PastLives] archive: " + saved.Count + " from old saves, " + myList.Count + " in my list, "
                + CountSource(ArchivePawn.SourcePawnEditor) + " from Pawn Editor, " + CountSource(ArchivePawn.SourceCharacterEditor)
                + " from Character Editor\n" + string.Join("\n", lines));
        }
        public static int CountSource(string source) => store.Pawns.Values.Count(p => p.Source == source);

        public static void Initialize()
        {
            archivePath = Path.Combine(GenFilePaths.SaveDataFolderPath, "PastLives", "archive.xml");
            savesFolder = GenFilePaths.SavedGamesFolderPath;
            playerFactionDefs = new HashSet<string>(DefDatabase<FactionDef>.AllDefs.Where(f => f.isPlayer).Select(f => f.defName));
            kindToRace = DefDatabase<PawnKindDef>.AllDefs.Where(k => k.race != null).ToDictionary(k => k.defName, k => k.race.defName);
            if (SelfTest.Active) SelfTest.ApplyOverrides();
            var scan = Rescan();
            if (SelfTest.Active)
            {
                // The self-test generates a world right away, so it needs the archive before that.
                scan?.Wait();
                FlushLog();
            }
        }

        public static Task Rescan(bool fromScratch = false)
        {
            if (scanning) return null;
            scanning = true;
            progress = null;
            var current = store;
            var settings = PastLivesMod.Settings;
            bool importPawnEditor = settings.importPawnEditor, importCharacterEditor = settings.importCharacterEditor;
            var listFolder = MyListFolderPath;
            return Task.Run(() =>
            {
                try
                {
                    Action<string> warn = w => pendingLog.Enqueue("!" + "[PastLives] " + w);
                    var s = fromScratch ? new ArchiveStore()
                        : current.Pawns.Count > 0 ? current.Clone()
                        : ArchiveStore.Load(archivePath);
                    int parsed = s.Update(savesFolder, playerFactionDefs, warn, (i, n) => progress = (i + 1) + "/" + n);

                    int imported = 0;
                    if (importPawnEditor)
                    {
                        var pe = EditorImport.PawnEditor(PawnEditorFolder, warn);
                        s.MergeImported(pe);
                        imported += pe.Count;
                    }
                    if (importCharacterEditor)
                    {
                        var ce = EditorImport.CharacterEditor(CharacterEditorSlots, playerFactionDefs, kindToRace, warn);
                        s.MergeImported(ce);
                        imported += ce.Count;
                    }
                    if (parsed > 0 || imported > 0 || fromScratch) s.Save(archivePath);

                    var list = MyListFolder.Load(listFolder, warn);
                    store = s;
                    myList = list;
                    pendingLog.Enqueue("[PastLives] archive ready: " + s.Pawns.Values.Count(p => !p.IsListPawn) + " colonists from old saves ("
                        + parsed + " saves read), " + list.Count + " in my list, " + imported + " imported from editors");
                }
                catch (Exception ex)
                {
                    pendingLog.Enqueue("!" + "[PastLives] archive scan failed: " + ex);
                }
                finally
                {
                    progress = null;
                    scanning = false;
                }
            });
        }

        // --- my list (game thread) ---

        public static ArchivePawn MyListEntry(string key) => myList.FirstOrDefault(p => p.Key == key);

        public static void AddToMyList(ArchivePawn p)
        {
            var old = MyListEntry(p.Key);
            if (old != null) DeleteFile(old.SourceFile);
            p.Source = ArchivePawn.SourceList;
            p.SourceFile = MyListFolder.Save(MyListFolderPath, p);
            myList = myList.Where(x => x.Key != p.Key).Append(p).ToList();
        }

        public static void RemoveFromMyList(string key)
        {
            var old = MyListEntry(key);
            if (old == null) return;
            DeleteFile(old.SourceFile);
            myList = myList.Where(x => x.Key != key).ToList();
        }

        // Forgets everything imported from one editor; whatever that editor still has comes back on the next rescan.
        public static void ForgetImported(string source)
        {
            var s = store.Clone();
            if (s.RemoveSource(source) == 0) return;
            s.Save(archivePath);
            store = s;
        }

        private static void DeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Warning("[PastLives] could not delete " + path + ": " + ex.Message); }
        }

        // Log calls are made on the game thread only.
        public static void FlushLog()
        {
            while (pendingLog.TryDequeue(out var msg))
            {
                if (msg.StartsWith("!")) Log.Warning(msg.Substring(1));
                else Log.Message(msg);
            }
        }
    }
}
