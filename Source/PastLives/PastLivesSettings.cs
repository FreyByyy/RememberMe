using System.Collections.Generic;
using Verse;

namespace PastLives
{
    public class PastLivesSettings : ModSettings
    {
        public bool enabled = true;
        public float eventChance = 0.03f;
        public float leaderChance = 0.15f;
        public float cooldownDays = 15f;
        public int maxWorldGenLeaders = 3;
        public bool letters = true;
        public bool includeChildren = true;

        public bool roleJoiner = true;
        public bool roleRefugee = true;
        public bool roleVisitor = true;
        public bool roleTrader = true;
        public bool roleRaider = true;
        public bool roleSlave = true;
        public bool roleLeader = true;
        public bool roleDead = true;

        public List<string> excludedColonies = new List<string>();

        public bool RoleEnabled(PastRole role)
        {
            switch (role)
            {
                case PastRole.Joiner: return roleJoiner;
                case PastRole.Refugee: return roleRefugee;
                case PastRole.Visitor: return roleVisitor;
                case PastRole.Trader: return roleTrader;
                case PastRole.Raider: return roleRaider;
                case PastRole.Slave: return roleSlave;
                case PastRole.Leader: return roleLeader;
                case PastRole.DeadReturn: return roleDead;
                default: return false;
            }
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref eventChance, "eventChance", 0.03f);
            Scribe_Values.Look(ref leaderChance, "leaderChance", 0.15f);
            Scribe_Values.Look(ref cooldownDays, "cooldownDays", 15f);
            Scribe_Values.Look(ref maxWorldGenLeaders, "maxWorldGenLeaders", 3);
            Scribe_Values.Look(ref letters, "letters", true);
            Scribe_Values.Look(ref includeChildren, "includeChildren", true);
            Scribe_Values.Look(ref roleJoiner, "roleJoiner", true);
            Scribe_Values.Look(ref roleRefugee, "roleRefugee", true);
            Scribe_Values.Look(ref roleVisitor, "roleVisitor", true);
            Scribe_Values.Look(ref roleTrader, "roleTrader", true);
            Scribe_Values.Look(ref roleRaider, "roleRaider", true);
            Scribe_Values.Look(ref roleSlave, "roleSlave", true);
            Scribe_Values.Look(ref roleLeader, "roleLeader", true);
            Scribe_Values.Look(ref roleDead, "roleDead", true);
            Scribe_Collections.Look(ref excludedColonies, "excludedColonies", LookMode.Value);
            if (excludedColonies == null) excludedColonies = new List<string>();
        }
    }
}
