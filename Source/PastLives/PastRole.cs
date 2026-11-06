using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PastLives
{
    public enum PastRole
    {
        None,
        Joiner,
        Refugee,
        Visitor,
        Trader,
        Raider,
        Slave,
        Leader,
        DeadReturn,
        Prisoner,       // held captive somewhere and waiting to be freed, or brought to the colony as a prisoner
        Defender,       // lives in a camp, settlement or site the player's people enter
        Castaway,       // fell from the sky or was left wounded: pod and shuttle crashes, downed refugees
        Wild,           // walked into the wilderness long ago and forgot who they were
        Sleeper,        // lies in an ancient cryptosleep casket
    }

    public enum DeadVariant
    {
        None,
        Wanderer,
        CreepJoiner,
        Shambler,
        Ghoul,
    }

    // One "event" that may bring back a past colonist: a raid group, a caravan, a quest, a new leader.
    // The roll happens once per scope, so a raid of twenty pawns is no likelier to carry one than a lone wanderer.
    // It is made when the scope's first pawn is generated, since only then is the role known for groups.
    public class SpawnScope
    {
        public PastRole Role;            // None = decide from the faction and pawn kind
        public Faction Faction;
        public bool Consumed;
        public DeadVariant Variant;
        public Pawn Result;

        public QuestRoleDef Quest;       // the quest this scope belongs to, for its candidate filters
        public string QuestName;         // its QuestScriptDef, so stories can fit the exact quest
        public bool MapGen;              // inhabitants of a map being generated that is not the player's home
        public bool Blocked;             // nobody from the archive in this scope (e.g. the dead of a distress call)

        private bool? rolled;

        public bool Rolled => rolled == true;

        public void ForceRoll() => rolled = true;

        public bool Roll(PastRole role)
        {
            if (rolled == null) rolled = Rand.Chance(PastLivesMod.Settings.ChanceFor(role));
            return rolled.Value;
        }
    }

    public static class SpawnContext
    {
        [System.ThreadStatic] private static List<SpawnScope> stack;

        public static SpawnScope Top => stack != null && stack.Count > 0 ? stack[stack.Count - 1] : null;

        public static void Push(SpawnScope scope)
        {
            if (stack == null) stack = new List<SpawnScope>();
            stack.Add(scope);
        }

        public static void Pop(SpawnScope scope)
        {
            if (stack == null) return;
            int i = stack.LastIndexOf(scope);
            if (i >= 0) stack.RemoveRange(i, stack.Count - i);
        }

        // Our own dead-return incidents run vanilla quests; their inner scopes must not take over.
        public static bool DeadReturnPending => Top != null && Top.Role == PastRole.DeadReturn && !Top.Consumed;
    }
}
