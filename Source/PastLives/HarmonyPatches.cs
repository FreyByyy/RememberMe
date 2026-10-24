using System;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace PastLives
{
    // The single place where a past colonist can replace a generated pawn.
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_GeneratePawn
    {
        public static void Prefix(ref PawnGenerationRequest request, out PendingSubstitution __state)
        {
            __state = null;
            try { __state = Substitution.TryBegin(ref request); }
            catch (Exception ex) { Log.ErrorOnce("[PastLives] substitution failed: " + ex, 0x5A57_0001); }
        }

        public static void Postfix(Pawn __result, PendingSubstitution __state)
        {
            if (__state == null || __result == null) return;
            try { Substitution.Finish(__result, __state); }
            catch (Exception ex) { Log.ErrorOnce("[PastLives] restoring a past colonist failed: " + ex, 0x5A57_0002); }
        }
    }

    // Raids, caravans, visitors and settlement inhabitants all come through pawn group generation.
    [HarmonyPatch(typeof(PawnGroupKindWorker), nameof(PawnGroupKindWorker.GeneratePawns),
        new[] { typeof(PawnGroupMakerParms), typeof(PawnGroupMaker), typeof(bool) })]
    public static class Patch_GroupGeneratePawns
    {
        public static void Prefix(PawnGroupMakerParms parms, out SpawnScope __state)
        {
            __state = null;
            if (SpawnContext.DeadReturnPending) return;
            __state = new SpawnScope { Faction = parms.faction, Rolled = Rand.Chance(PastLivesMod.Settings.eventChance) };
            SpawnContext.Push(__state);
        }

        public static Exception Finalizer(Exception __exception, SpawnScope __state)
        {
            if (__state != null) SpawnContext.Pop(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(QuestNode_Root_WandererJoin), "RunInt")]
    public static class Patch_WandererJoin
    {
        public static void Prefix(out SpawnScope __state) => __state = QuestScopes.Begin(PastRole.Joiner);
        public static Exception Finalizer(Exception __exception, SpawnScope __state) => QuestScopes.End(__exception, __state);
    }

    [HarmonyPatch(typeof(QuestNode_Root_Hospitality_Refugee), "RunInt")]
    public static class Patch_HospitalityRefugee
    {
        public static void Prefix(out SpawnScope __state) => __state = QuestScopes.Begin(PastRole.Refugee);
        public static Exception Finalizer(Exception __exception, SpawnScope __state) => QuestScopes.End(__exception, __state);
    }

    public static class QuestScopes
    {
        public static SpawnScope Begin(PastRole role)
        {
            if (SpawnContext.DeadReturnPending) return null;
            var scope = new SpawnScope { Role = role, Rolled = Rand.Chance(PastLivesMod.Settings.eventChance) };
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
            __state = new SpawnScope
            {
                Role = PastRole.Leader,
                Faction = __instance,
                Rolled = Rand.Chance(PastLivesMod.Settings.leaderChance),
            };
            SpawnContext.Push(__state);
        }

        public static void Postfix(Faction __instance, SpawnScope __state)
        {
            if (__state?.Result != null && __instance.leader == __state.Result)
                GameComponent_PastLives.Instance?.Notify_BecameLeader(__state.Result, __instance);
        }

        public static Exception Finalizer(Exception __exception, SpawnScope __state)
        {
            if (__state != null) SpawnContext.Pop(__state);
            return __exception;
        }
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
