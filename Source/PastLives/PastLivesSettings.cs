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
        }
    }
}
