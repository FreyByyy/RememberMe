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
    // Owns the archive at runtime. Loading and scanning run on a worker thread; the game thread only
    // ever sees a finished ArchiveStore, swapped in whole.
    // Started from a separate [StaticConstructorOnStartup] class on purpose: the worker thread touches this
    // class's static fields, so waiting for it inside this class's own static constructor would deadlock.
    [StaticConstructorOnStartup]
    public static class PastLivesStartup
    {
        static PastLivesStartup() => PastLivesArchive.Initialize();
    }

    public static class PastLivesArchive
    {
        private static volatile ArchiveStore store = new ArchiveStore();
        private static volatile bool scanning;
        private static volatile string progress;
        private static readonly ConcurrentQueue<string> pendingLog = new ConcurrentQueue<string>();

        private static string archivePath;
        private static string savesFolder;
        private static HashSet<string> playerFactionDefs;

        public static IEnumerable<ArchivePawn> All => store.Pawns.Values;
        public static int Count => store.Pawns.Count;
        public static bool Scanning => scanning;
        public static string Progress => progress;

        public static void Initialize()
        {
            archivePath = Path.Combine(GenFilePaths.SaveDataFolderPath, "PastLives", "archive.xml");
            savesFolder = GenFilePaths.SavedGamesFolderPath;
            playerFactionDefs = new HashSet<string>(DefDatabase<FactionDef>.AllDefs.Where(f => f.isPlayer).Select(f => f.defName));
            var scan = Rescan();
            if (SelfTest.Active)
            {
                // The self-test generates a world right away, so it needs the archive before that.
                scan?.Wait();
                FlushLog();
                SelfTest.ApplyOverrides();
            }
        }

        public static Task Rescan(bool fromScratch = false)
        {
            if (scanning) return null;
            scanning = true;
            progress = null;
            var current = store;
            return Task.Run(() =>
            {
                try
                {
                    var s = fromScratch ? new ArchiveStore()
                        : current.Pawns.Count > 0 ? current.Clone()
                        : ArchiveStore.Load(archivePath);
                    int parsed = s.Update(savesFolder, playerFactionDefs, w => pendingLog.Enqueue("[PastLives] " + w),
                        (i, n) => progress = (i + 1) + "/" + n);
                    if (parsed > 0 || fromScratch) s.Save(archivePath);
                    store = s;
                    pendingLog.Enqueue("[PastLives] archive ready: " + s.Pawns.Count + " past colonists from "
                        + s.Pawns.Values.Select(p => p.Colony).Distinct().Count() + " colonies (" + parsed + " saves read)");
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

        // Log calls are made on the game thread only.
        public static void FlushLog()
        {
            while (pendingLog.TryDequeue(out var msg))
            {
                if (msg.StartsWith("!")) Log.Warning(msg.Substring(1));
                else Log.Message(msg);
            }
        }

        public static IEnumerable<string> Colonies => store.Pawns.Values.Select(p => p.Colony).Distinct().OrderBy(c => c);
    }
}
