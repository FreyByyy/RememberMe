using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PastLives
{
    public class PastLivesMod : Mod
    {
        public static PastLivesSettings Settings;

        private Vector2 colonyScroll;
        private Vector2 leftScroll;
        private float leftHeight;

        public PastLivesMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<PastLivesSettings>();
            new Harmony("frbyyy.rememberme").PatchAll();
        }

        public override string SettingsCategory() => "PastLives_ModName".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            PastLivesArchive.FlushLog();
            var left = inRect.LeftPart(0.48f);
            var right = inRect.RightPart(0.48f);

            var leftView = new Rect(0f, 0f, left.width - 16f, Mathf.Max(leftHeight, left.height));
            Widgets.BeginScrollView(left, ref leftScroll, leftView);
            var l = new Listing_Standard();
            l.Begin(leftView);
            l.CheckboxLabeled("PastLives_Enabled".Translate(), ref Settings.enabled);
            l.Gap(6f);
            l.Label("PastLives_EventChance".Translate(Settings.eventChance.ToStringPercent("0.#")), tooltip: "PastLives_EventChanceDesc".Translate());
            Settings.eventChance = l.Slider(Settings.eventChance, 0f, 0.25f);
            l.Label("PastLives_LeaderChance".Translate(Settings.leaderChance.ToStringPercent("0.#")), tooltip: "PastLives_LeaderChanceDesc".Translate());
            Settings.leaderChance = l.Slider(Settings.leaderChance, 0f, 1f);
            l.Label("PastLives_Cooldown".Translate(Settings.cooldownDays.ToString("0")), tooltip: "PastLives_CooldownDesc".Translate());
            Settings.cooldownDays = Mathf.Round(l.Slider(Settings.cooldownDays, 0f, 60f));
            l.Label("PastLives_MaxWorldLeaders".Translate(Settings.maxWorldGenLeaders));
            Settings.maxWorldGenLeaders = Mathf.RoundToInt(l.Slider(Settings.maxWorldGenLeaders, 0f, 10f));
            l.CheckboxLabeled("PastLives_Letters".Translate(), ref Settings.letters);
            l.CheckboxLabeled("PastLives_IncludeChildren".Translate(), ref Settings.includeChildren, "PastLives_IncludeChildrenDesc".Translate());

            l.GapLine();
            l.Label("PastLives_MyListHeader".Translate(), tooltip: "PastLives_MyListDesc".Translate());
            l.Label("PastLives_ListWeight".Translate(Settings.listWeight.ToString("0.#")), tooltip: "PastLives_ListWeightDesc".Translate());
            Settings.listWeight = Mathf.Round(l.Slider(Settings.listWeight, 1f, 20f) * 2f) / 2f;
            l.CheckboxLabeled("PastLives_OnlyMyList".Translate(), ref Settings.onlyMyList, "PastLives_OnlyMyListDesc".Translate());
            l.CheckboxLabeled("PastLives_AllowRepeats".Translate(), ref Settings.allowRepeats, "PastLives_AllowRepeatsDesc".Translate());
            bool pe = Settings.importPawnEditor, ce = Settings.importCharacterEditor;
            l.CheckboxLabeled("PastLives_ImportPawnEditor".Translate(), ref Settings.importPawnEditor, "PastLives_ImportPawnEditorDesc".Translate());
            l.CheckboxLabeled("PastLives_ImportCharacterEditor".Translate(), ref Settings.importCharacterEditor, "PastLives_ImportCharacterEditorDesc".Translate());
            if ((Settings.importPawnEditor && !pe) || (Settings.importCharacterEditor && !ce)) PastLivesArchive.Rescan();

            l.GapLine();
            l.Label("PastLives_Roles".Translate());
            l.CheckboxLabeled("PastLives_Role_Joiner".Translate(), ref Settings.roleJoiner);
            l.CheckboxLabeled("PastLives_Role_Refugee".Translate(), ref Settings.roleRefugee);
            l.CheckboxLabeled("PastLives_Role_Visitor".Translate(), ref Settings.roleVisitor);
            l.CheckboxLabeled("PastLives_Role_Trader".Translate(), ref Settings.roleTrader);
            l.CheckboxLabeled("PastLives_Role_Raider".Translate(), ref Settings.roleRaider);
            l.CheckboxLabeled("PastLives_Role_Slave".Translate(), ref Settings.roleSlave);
            l.CheckboxLabeled("PastLives_Role_Leader".Translate(), ref Settings.roleLeader);
            l.CheckboxLabeled("PastLives_Role_DeadReturn".Translate(), ref Settings.roleDead, "PastLives_Role_DeadReturnDesc".Translate());
            leftHeight = l.CurHeight + 12f;
            l.End();
            Widgets.EndScrollView();

            // Archive status, my list, per-colony switches.
            var r = new Listing_Standard();
            r.Begin(right);
            var saved = PastLivesArchive.SavedColonists.ToList();
            string status = PastLivesArchive.Scanning
                ? "PastLives_Scanning".Translate(PastLivesArchive.Progress ?? "...").Resolve()
                : "PastLives_ArchiveStatus".Translate(saved.Count(p => !p.Dead), saved.Count(p => p.Dead), saved.Select(p => p.Colony).Distinct().Count()).Resolve();
            r.Label(status);
            r.Label("PastLives_MyListStatus".Translate(PastLivesArchive.CountMyList,
                PastLivesArchive.CountSource(ArchivePawn.SourcePawnEditor), PastLivesArchive.CountSource(ArchivePawn.SourceCharacterEditor)));
            if (!PastLivesArchive.Scanning && r.ButtonText("PastLives_Rescan".Translate())) PastLivesArchive.Rescan();
            if (r.ButtonText("PastLives_OpenMyList".Translate())) OpenFolder(PastLivesArchive.MyListFolderPath);
            r.Label("PastLives_Colonies".Translate());
            r.End();

            var colonies = saved.GroupBy(p => p.Colony).OrderBy(g => g.Key).ToList();
            var outRect = new Rect(right.x, right.y + r.CurHeight + 4f, right.width, right.height - r.CurHeight - 4f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, colonies.Count * 26f);
            Widgets.BeginScrollView(outRect, ref colonyScroll, viewRect);
            float y = 0f;
            foreach (var g in colonies)
            {
                bool on = !Settings.excludedColonies.Contains(g.Key);
                bool was = on;
                var label = g.Key + "  (" + g.Count(p => !p.Dead) + " / †" + g.Count(p => p.Dead) + ")";
                Widgets.CheckboxLabeled(new Rect(0f, y, viewRect.width, 24f), label, ref on);
                if (on != was)
                {
                    if (on) Settings.excludedColonies.Remove(g.Key);
                    else Settings.excludedColonies.Add(g.Key);
                }
                y += 26f;
            }
            Widgets.EndScrollView();
        }

        private static void OpenFolder(string path)
        {
            try
            {
                System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                Messages.Message(path, MessageTypeDefOf.RejectInput, historical: false);
                Log.Warning("[PastLives] could not open " + path + ": " + ex.Message);
            }
        }
    }

    [DefOf]
    public static class PastLivesDefOf
    {
        public static HediffDef PastLives_Returned;
        public static PawnRelationDef PastLives_OldComrade;

        static PastLivesDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(PastLivesDefOf));
    }

    // Abandoned world drafts (going back from world generation) must not keep their leaders.
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    public static class Patch_GenerateWorld
    {
        public static void Prefix() => GameComponent_PastLives.Instance?.Notify_WorldGenerationStarted();
    }
}
