using System.Collections.Generic;
using RimWorld;

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
    }

    public enum DeadVariant
    {
        None,
        Wanderer,
        CreepJoiner,
        Shambler,
    }

    // One "event" that may bring back a past colonist: a raid group, a caravan, a quest, a new leader.
    // The roll happens once per scope, so a raid of twenty pawns is no likelier to carry one than a lone wanderer.
    public class SpawnScope
    {
        public PastRole Role;            // None = decide from the faction and pawn kind
        public Faction Faction;
        public bool Rolled;
        public bool Consumed;
        public DeadVariant Variant;
        public Verse.Pawn Result;
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
