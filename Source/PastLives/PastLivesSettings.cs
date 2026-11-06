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
        public bool rolePrisoner = true;
        public bool roleDefender = true;
        public bool roleCastaway = true;
        public bool roleWild = true;
        public bool roleSleeper = true;

        // Per-role factor on the chance (the leader chance for leaders, the event chance for everyone else).
        public Dictionary<string, float> roleChanceFactors = new Dictionary<string, float>();
        public bool rolesExpanded = true;

        // Roles shown in the settings, in this order.
        public static readonly PastRole[] ConfigurableRoles =
        {
            PastRole.Joiner, PastRole.Refugee, PastRole.Castaway, PastRole.Visitor, PastRole.Trader, PastRole.Raider,
            PastRole.Defender, PastRole.Prisoner, PastRole.Slave, PastRole.Wild, PastRole.Sleeper, PastRole.Leader, PastRole.DeadReturn,
        };

        // "My list": pawns remembered in game plus editor imports.
        public float listWeight = 3f;
        public bool onlyMyList;
        public bool allowRepeats;
        public bool importPawnEditor = true;
        public bool importCharacterEditor;   // experimental
        public bool showRememberButton = true;

        // Stories in the "familiar face" letters.
        public bool stories = true;

        // Experimental: new scars and traits for returned colonists.
        public bool hardships;
        public float scarChanceDead = 1f;
        public float scarChanceOther = 0.3f;
        public IntRange scarCount = new IntRange(1, 3);
        public float traitChance = 0.3f;

        public List<string> excludedColonies = new List<string>();

        public bool SourceEnabled(string source)
        {
            switch (source)
            {
                case ArchivePawn.SourcePawnEditor: return importPawnEditor;
                case ArchivePawn.SourceCharacterEditor: return importCharacterEditor;
                default: return true;
            }
        }

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
                case PastRole.Prisoner: return rolePrisoner;
                case PastRole.Defender: return roleDefender;
                case PastRole.Castaway: return roleCastaway;
                case PastRole.Wild: return roleWild;
                case PastRole.Sleeper: return roleSleeper;
                default: return false;
            }
        }

        public void SetRoleEnabled(PastRole role, bool on)
        {
            switch (role)
            {
                case PastRole.Joiner: roleJoiner = on; break;
                case PastRole.Refugee: roleRefugee = on; break;
                case PastRole.Visitor: roleVisitor = on; break;
                case PastRole.Trader: roleTrader = on; break;
                case PastRole.Raider: roleRaider = on; break;
                case PastRole.Slave: roleSlave = on; break;
                case PastRole.Leader: roleLeader = on; break;
                case PastRole.DeadReturn: roleDead = on; break;
                case PastRole.Prisoner: rolePrisoner = on; break;
                case PastRole.Defender: roleDefender = on; break;
                case PastRole.Castaway: roleCastaway = on; break;
                case PastRole.Wild: roleWild = on; break;
                case PastRole.Sleeper: roleSleeper = on; break;
            }
        }

        public float ChanceFactor(PastRole role) => roleChanceFactors.TryGetValue(role.ToString(), out var f) ? f : 1f;

        public void SetChanceFactor(PastRole role, float f)
        {
            if (System.Math.Abs(f - 1f) < 0.001f) roleChanceFactors.Remove(role.ToString());
            else roleChanceFactors[role.ToString()] = f;
        }

        public float ChanceFor(PastRole role) =>
            UnityEngine.Mathf.Clamp01((role == PastRole.Leader ? leaderChance : eventChance) * ChanceFactor(role));

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
            Scribe_Values.Look(ref rolePrisoner, "rolePrisoner", true);
            Scribe_Values.Look(ref roleDefender, "roleDefender", true);
            Scribe_Values.Look(ref roleCastaway, "roleCastaway", true);
            Scribe_Values.Look(ref roleWild, "roleWild", true);
            Scribe_Values.Look(ref roleSleeper, "roleSleeper", true);
            Scribe_Values.Look(ref rolesExpanded, "rolesExpanded", true);
            Scribe_Collections.Look(ref roleChanceFactors, "roleChanceFactors", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref listWeight, "listWeight", 3f);
            Scribe_Values.Look(ref onlyMyList, "onlyMyList");
            Scribe_Values.Look(ref allowRepeats, "allowRepeats");
            Scribe_Values.Look(ref importPawnEditor, "importPawnEditor", true);
            Scribe_Values.Look(ref importCharacterEditor, "importCharacterEditor");
            Scribe_Values.Look(ref showRememberButton, "showRememberButton", true);
            Scribe_Values.Look(ref stories, "stories", true);
            Scribe_Values.Look(ref hardships, "hardships");
            Scribe_Values.Look(ref scarChanceDead, "scarChanceDead", 1f);
            Scribe_Values.Look(ref scarChanceOther, "scarChanceOther", 0.3f);
            Scribe_Values.Look(ref scarCount, "scarCount", new IntRange(1, 3));
            Scribe_Values.Look(ref traitChance, "traitChance", 0.3f);
            Scribe_Collections.Look(ref excludedColonies, "excludedColonies", LookMode.Value);
            if (excludedColonies == null) excludedColonies = new List<string>();
            if (roleChanceFactors == null) roleChanceFactors = new Dictionary<string, float>();
        }
    }
}
