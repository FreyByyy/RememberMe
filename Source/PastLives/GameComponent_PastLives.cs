using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RimWorld;
using Verse;

namespace PastLives
{
    public class PastLifeEntry : IExposable
    {
        public string thingId;
        public string key;
        public string colony;
        public string name;
        public PastRole role;
        public DeadVariant variant;
        public bool wasDead;
        public bool notified;
        public string factionName;
        public string story;

        public void ExposeData()
        {
            Scribe_Values.Look(ref thingId, "thingId");
            Scribe_Values.Look(ref key, "key");
            Scribe_Values.Look(ref colony, "colony");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref role, "role");
            Scribe_Values.Look(ref variant, "variant");
            Scribe_Values.Look(ref wasDead, "wasDead");
            Scribe_Values.Look(ref notified, "notified");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref story, "story");
        }
    }

    // Per-world memory: who from the archive already appeared here, when the last one did,
    // and which pawns still owe the player a "familiar face" letter.
    // Pawns are tracked by ThingID rather than by reference, so a pawn the world later discards
    // leaves no dangling cross-reference in the save.
    public class GameComponent_PastLives : GameComponent
    {
        public static GameComponent_PastLives Instance => Current.Game?.GetComponent<GameComponent_PastLives>();

        private HashSet<string> usedKeys = new HashSet<string>();
        private List<PastLifeEntry> entries = new List<PastLifeEntry>();
        private int lastAppearanceTick = -9999999;
        public int worldGenLeaders;

        private Dictionary<string, PastLifeEntry> byThingId;

        public GameComponent_PastLives(Game game) { }

        private HashSet<string> aliveCache, aliveNamesCache;
        private int aliveCacheFrame = -1;

        // With repeats on, a "my list" pawn may come back again once every earlier version of them in this
        // world is dead or gone.
        public bool IsUsed(ArchivePawn p)
        {
            if (!usedKeys.Contains(p.Key)) return false;
            if (!PastLivesMod.Settings.allowRepeats || !p.IsListPawn) return true;
            RefreshAlive();
            return entries.Any(e => e.key == p.Key && e.thingId != null && aliveCache.Contains(e.thingId));
        }

        // The archive also reads saves of the game being played, and "my list" may hold its colonists:
        // nobody from this world may take a new pawn's place, and neither may anyone who shares a name
        // with a living pawn (e.g. a current colonist exported to Pawn Editor).
        public bool AlreadyHere(ArchivePawn p)
        {
            if (FromThisWorld(p)) return true;
            RefreshAlive();
            var name = p.DisplayName;
            return !string.IsNullOrEmpty(name) && aliveNamesCache.Contains(name);
        }

        private static bool FromThisWorld(ArchivePawn p)
        {
            var info = Find.World?.info;
            if (info == null) return false;
            if (p.WorldId != null) return p.WorldId == info.persistentRandomValue.ToString(CultureInfo.InvariantCulture);
            // No world id (a save without one, or a "my list" file written before ids were stored): old saves
            // and the Remember button both key pawns by world seed.
            return (p.Source == ArchivePawn.SourceSave || p.Source == ArchivePawn.SourceList)
                && p.Key != null && p.Key.StartsWith(info.seedString + "#", StringComparison.Ordinal);
        }

        private void RefreshAlive()
        {
            if (aliveCache != null && aliveCacheFrame == UnityEngine.Time.frameCount) return;
            var alive = PawnsFinder.AllMapsWorldAndTemporary_Alive;
            aliveCache = new HashSet<string>(alive.Select(x => x.ThingID));
            aliveNamesCache = new HashSet<string>(alive.Where(x => x.RaceProps.Humanlike && x.Name != null).Select(x => x.Name.ToStringFull));
            aliveCacheFrame = UnityEngine.Time.frameCount;
        }

        public bool CooldownOver => Find.TickManager == null
            || Find.TickManager.TicksGame - lastAppearanceTick >= PastLivesMod.Settings.cooldownDays * GenDate.TicksPerDay;

        public IReadOnlyList<PastLifeEntry> Entries => entries;

        public void MarkUsed(string key, bool countsForCooldown)
        {
            usedKeys.Add(key);
            if (countsForCooldown && Find.TickManager != null) lastAppearanceTick = Find.TickManager.TicksGame;
        }

        public PastLifeEntry EntryFor(Pawn pawn)
        {
            if (pawn == null || entries.Count == 0) return null;
            if (byThingId == null) byThingId = entries.Where(e => e.thingId != null).GroupBy(e => e.thingId).ToDictionary(g => g.Key, g => g.Last());
            return byThingId.TryGetValue(pawn.ThingID, out var e) ? e : null;
        }

        public void Register(Pawn pawn, ArchivePawn a, PastRole role, DeadVariant variant, string story = null)
        {
            var entry = new PastLifeEntry
            {
                thingId = pawn.ThingID,
                key = a.Key,
                colony = a.Colony,
                name = pawn.Name?.ToStringShort ?? a.ShortName,
                role = role,
                variant = variant,
                wasDead = a.Dead,
                factionName = pawn.Faction?.Name,
                story = story,
            };
            entries.Add(entry);
            byThingId = null;
            LinkComrades(pawn, entry);
        }

        // Two people from the same old colony remember each other.
        private void LinkComrades(Pawn pawn, PastLifeEntry entry)
        {
            var def = PastLivesDefOf.PastLives_OldComrade;
            if (def == null || pawn.relations == null || string.IsNullOrEmpty(entry.colony)) return;
            var ids = new HashSet<string>(entries.Where(e => e != entry && e.colony == entry.colony && e.thingId != null).Select(e => e.thingId));
            if (ids.Count == 0) return;
            foreach (var other in PawnsFinder.AllMapsWorldAndTemporary_Alive)
            {
                if (other == pawn || other.relations == null || !ids.Contains(other.ThingID)) continue;
                if (!pawn.relations.DirectRelationExists(def, other)) pawn.relations.AddDirectRelation(def, other);
            }
        }

        // Dev tool: everyone may appear in this world again.
        public void ResetUsed()
        {
            usedKeys.Clear();
            lastAppearanceTick = -9999999;
        }

        public static string WithStory(string text, PastLifeEntry e) => string.IsNullOrEmpty(e.story) ? text : text + "\n\n" + e.story;

        public void Notify_WorldGenerationStarted()
        {
            usedKeys.Clear();
            entries.Clear();
            byThingId = null;
            worldGenLeaders = 0;
        }

        public void Notify_BecameLeader(Pawn pawn, Faction faction)
        {
            var entry = EntryFor(pawn);
            if (entry == null) return;
            entry.factionName = faction.Name;
            if (!Substitution.WorldGenerating) SendLetter(entry, pawn);
        }

        public override void StartedNewGame()
        {
            // Leaders chosen while the world was generated are announced once the colony lands.
            var pending = entries.Where(e => e.role == PastRole.Leader && !e.notified).ToList();
            if (pending.Count == 0) return;
            foreach (var e in pending) e.notified = true;
            if (!PastLivesMod.Settings.letters) return;
            var lines = pending.Select(e => ("PastLives_LeaderLine" + SavedSuffix(e)).Translate(e.name.Named("PAWN"), e.colony.Named("COLONY"), (e.factionName ?? "?").Named("FACTION")).Resolve());
            Find.LetterStack.ReceiveLetter("PastLives_LetterLabel_WorldLeaders".Translate(),
                "PastLives_Letter_WorldLeaders".Translate() + "\n\n" + string.Join("\n", lines), LetterDefOf.NeutralEvent);
        }

        public override void GameComponentUpdate()
        {
            PastLivesArchive.FlushLog();
            SelfTest.Update();
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 250 == 0) CheckSpawned();
        }

        public void CheckSpawned()
        {
            if (!entries.Any(e => !e.notified && e.role != PastRole.Leader)) return;
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    var e = EntryFor(pawn);
                    if (e != null && !e.notified) SendLetter(e, pawn);
                }
            }
        }

        // Pawns from "my list" without a known colony get texts that say "one of your saved colonists".
        public static string SavedSuffix(PastLifeEntry e) => string.IsNullOrEmpty(e.colony) ? "_Saved" : "";

        public void SendLetter(PastLifeEntry e, Pawn pawn)
        {
            e.notified = true;
            if (!PastLivesMod.Settings.letters) return;
            string key = (e.role == PastRole.DeadReturn ? "PastLives_Letter_Dead_" + e.variant : "PastLives_Letter_" + e.role) + SavedSuffix(e);
            var text = key.Translate(pawn.Named("PAWN"), e.colony.Named("COLONY"), (pawn.Faction?.Name ?? e.factionName ?? "").Named("FACTION"));
            var label = (e.role == PastRole.DeadReturn ? "PastLives_LetterLabel_Dead" : "PastLives_LetterLabel").Translate(pawn.Named("PAWN"));
            LetterDef type = e.role == PastRole.Raider ? LetterDefOf.ThreatSmall
                : e.role == PastRole.DeadReturn ? LetterDefOf.NeutralEvent
                : e.role == PastRole.Joiner || e.role == PastRole.Refugee ? LetterDefOf.PositiveEvent
                : LetterDefOf.NeutralEvent;
            Find.LetterStack.ReceiveLetter(label, WithStory(text, e), type, new LookTargets(pawn));
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref usedKeys, "usedKeys", LookMode.Value);
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            Scribe_Values.Look(ref lastAppearanceTick, "lastAppearanceTick", -9999999);
            Scribe_Values.Look(ref worldGenLeaders, "worldGenLeaders");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (usedKeys == null) usedKeys = new HashSet<string>();
                if (entries == null) entries = new List<PastLifeEntry>();
                byThingId = null;
            }
        }
    }
}
