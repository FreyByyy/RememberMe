using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PastLives
{
    // Which quests may bring a past colonist, and as whom. Quests are named by their QuestScriptDef so that a
    // missing DLC or mod only leaves an unused name behind; other mods' quests can be added the same way.
    public class QuestRoleDef : Def
    {
        public List<string> quests = new List<string>();
        public PastRole role;
        public DeadVariant variant;
        public string requiredGene;          // only people who carried this gene (sanguophages)
        public float titleFactor = 1f;       // weight for people who held a royal title

        private static Dictionary<string, QuestRoleDef> byQuest;

        public static QuestRoleDef For(QuestScriptDef quest)
        {
            if (quest == null) return null;
            if (byQuest == null)
            {
                byQuest = new Dictionary<string, QuestRoleDef>();
                foreach (var def in DefDatabase<QuestRoleDef>.AllDefs)
                    foreach (var q in def.quests) byQuest[q] = def;
            }
            return byQuest.TryGetValue(quest.defName, out var r) ? r : null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (var e in base.ConfigErrors()) yield return e;
            if (role == PastRole.None) yield return "no role";
            if (quests.Count == 0) yield return "no quests";
        }
    }
}
