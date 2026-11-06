using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace PastLives
{
    // The single place where a past colonist can replace a generated pawn.
    // Other mods may skip the generation and hand back a pawn that already exists (Eternal Pawns returns its
    // veterans this way). Harmony still runs our prefix and postfix then, so __runOriginal tells us: a pawn
    // that was not generated just now is never rewritten into someone else.
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_GeneratePawn
    {
        public static void Prefix(ref PawnGenerationRequest request, bool __runOriginal, out PendingSubstitution __state)
        {
            __state = null;
            if (!__runOriginal) return;
            try { __state = Substitution.TryBegin(ref request); }
            catch (Exception ex) { Log.ErrorOnce("[PastLives] substitution failed: " + ex, 0x5A57_0001); }
        }

        public static void Postfix(Pawn __result, bool __runOriginal, PendingSubstitution __state)
        {
            if (__state == null) return;
            // A prefix that ran after ours skipped the generation: give the person back to the archive.
            if (!__runOriginal || __result == null)
            {
                Substitution.Cancel(__state);
                return;
            }
            try { Substitution.Finish(__result, __state); }
            catch (Exception ex) { Log.ErrorOnce("[PastLives] restoring a past colonist failed: " + ex, 0x5A57_0002); }
        }
    }

    // Raids, caravans, visitors and settlement inhabitants all come through pawn group generation.
    // Groups made while a map other than the player's home is generated are the people who live there.
    [HarmonyPatch(typeof(PawnGroupKindWorker), nameof(PawnGroupKindWorker.GeneratePawns),
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool) })]
    public static class Patch_GroupGeneratePawns
    {
        public static void Prefix(PawnGroupMakerParms parms, out SpawnScope __state)
        {
            __state = null;
            if (SpawnContext.DeadReturnPending) return;
            // A quest that makes its people as a group (sanguophage meetings, beggars) keeps its own scope and filters.
            var top = SpawnContext.Top;
            if (top?.Quest != null && !top.Consumed) return;
            __state = new SpawnScope { Faction = parms.faction };
            var map = MapGenerator.mapBeingGenerated;
            if (map != null && !map.IsPlayerHome)
            {
                __state.MapGen = true;
                // A distress call's villagers are made to be found dead.
                if (map.Parent is Site site && site.parts.Any(p => p.def.defName.StartsWith("DistressCall"))) __state.Blocked = true;
            }
            SpawnContext.Push(__state);
        }

        public static Exception Finalizer(Exception __exception, SpawnScope __state) => Scopes.End(__exception, __state);
    }

    // Every quest goes through here; QuestRoleDefs say which of them may bring someone back, and as whom.
    [HarmonyPatch(typeof(QuestGen), nameof(QuestGen.Generate))]
    public static class Patch_QuestGenerate
    {
        public static void Prefix(QuestScriptDef root, out SpawnScope __state)
        {
            __state = null;
            var def = QuestRoleDef.For(root);
            if (def == null || SpawnContext.DeadReturnPending) return;
            __state = Scopes.Begin(def.role);
            __state.Quest = def;
            __state.QuestName = root.defName;
            __state.Variant = def.variant;
        }

        public static Exception Finalizer(Exception __exception, SpawnScope __state) => Scopes.End(__exception, __state);
    }

    // A wild man or woman wanders in.
    [HarmonyPatch(typeof(IncidentWorker_WildManWandersIn), "TryExecuteWorker")]
    public static class Patch_WildManWandersIn
    {
        public static void Prefix(out SpawnScope __state) => __state = SpawnContext.DeadReturnPending ? null : Scopes.Begin(PastRole.Wild);
        public static Exception Finalizer(Exception __exception, SpawnScope __state) => Scopes.End(__exception, __state);
    }

    // The sleepers of ancient cryptosleep caskets, friendly or not. The half-eaten one is left out: that pawn
    // dies right after it is made.
    [HarmonyPatch]
    public static class Patch_AncientPodContents
    {
        private static readonly string[] Methods =
        {
            "GenerateFriendlyAncient", "GenerateIncappedAncient", "GenerateAngryAncient", "GenerateSlave",
            "GenerateFriendlyAncient_NewTemp", "GenerateIncappedAncient_NewTemp", "GenerateAngryAncient_NewTemp", "GenerateSlave_NewTemp",
        };

        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in Methods)
            {
                var m = AccessTools.DeclaredMethod(typeof(ThingSetMaker_MapGen_AncientPodContents), name);
                if (m != null) yield return m;
            }
        }

        public static void Prefix(out SpawnScope __state) => __state = SpawnContext.DeadReturnPending ? null : Scopes.Begin(PastRole.Sleeper);
        public static Exception Finalizer(Exception __exception, SpawnScope __state) => Scopes.End(__exception, __state);
    }

    // A dead colonist who comes back as a ghoul gets our letter instead of the vanilla one.
    [HarmonyPatch]
    public static class Patch_GhoulPodLetter
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "SendLetter", "SendLetter_NewTemp" })
            {
                var m = AccessTools.DeclaredMethod(typeof(QuestNode_Root_RefugeePodCrash_Ghoul), name);
                if (m != null) yield return m;
            }
        }

        public static bool Prefix(Pawn pawn)
        {
            var entry = GameComponent_PastLives.Instance?.EntryFor(pawn);
            if (entry == null) return true;
            GameComponent_PastLives.Instance.SendLetter(entry, pawn);
            return false;
        }
    }

    public static class Scopes
    {
        public static SpawnScope Begin(PastRole role)
        {
            var scope = new SpawnScope { Role = role };
            SpawnContext.Push(scope);
            return scope;
        }

        public static Exception End(Exception ex, SpawnScope scope)
        {
            if (scope != null) SpawnContext.Pop(scope);
            return ex;
        }
    }

    // New faction leaders: at world generation and whenever a leader dies and is replaced.
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryGenerateNewLeader))]
    public static class Patch_TryGenerateNewLeader
    {
        public static void Prefix(Faction __instance, out SpawnScope __state)
        {
            __state = Scopes.Begin(PastRole.Leader);
            __state.Faction = __instance;
        }

        public static void Postfix(Faction __instance, SpawnScope __state)
        {
            if (__state?.Result != null && __instance.leader == __state.Result)
                GameComponent_PastLives.Instance?.Notify_BecameLeader(__state.Result, __instance);
        }

        public static Exception Finalizer(Exception __exception, SpawnScope __state) => Scopes.End(__exception, __state);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetInspectString))]
    public static class Patch_PawnInspectString
    {
        public static void Postfix(Pawn __instance, ref string __result)
        {
            var entry = GameComponent_PastLives.Instance?.EntryFor(__instance);
            if (entry == null) return;
            var line = ((entry.wasDead ? "PastLives_InspectDead" : "PastLives_Inspect") + GameComponent_PastLives.SavedSuffix(entry)).Translate(__instance.Named("PAWN"), entry.colony.Named("COLONY")).Resolve();
            __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
        }
    }
}
