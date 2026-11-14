using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// The Drop rules window (from the Divine tab): the <see cref="DropCategoryDef"/> tree, one row per
    /// category with its action. Click to pick one of the actions that make sense there, or "same as"
    /// the parent. Saved with the game (<see cref="DropRules"/>).
    /// </summary>
    public class Window_DropRules : Window
    {
        private const float RowHeight = 28f;
        private const float Indent = 18f;
        private const float ButtonWidth = 190f;

        private Vector2 scroll;

        public override Vector2 InitialSize => new Vector2(520f, 640f);

        public Window_DropRules()
        {
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            forcePause = true;
        }

        public static string ActionLabel(DropAction action) => ("AGM_Drop_Action_" + action).Translate();

        public override void DoWindowContents(Rect inRect)
        {
            DropRules rules = DropRules.Instance;
            if (rules == null)
            {
                return;
            }
            using (new TextBlock(GameFont.Medium))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f), "AGM_DropRules".Translate());
            }
            using (new TextBlock(GameFont.Tiny, ColoredText.SubtleGrayColor))
            {
                Widgets.Label(new Rect(inRect.x, inRect.y + 34f, inRect.width, 46f), "AGM_DropRules_Intro".Translate());
            }

            List<DropCategoryDef> rows = new List<DropCategoryDef>();
            foreach (DropCategoryDef top in DropCategoryDef.TopLevel)
            {
                AddWithChildren(top, rows);
            }
            float top0 = inRect.y + 84f;
            Rect outRect = new Rect(inRect.x, top0, inRect.width, inRect.height - (top0 - inRect.y) - CloseButSize.y - 8f);
            Rect view = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, view);
            for (int i = 0; i < rows.Count; i++)
            {
                DrawRow(new Rect(0f, i * RowHeight, view.width, RowHeight), rows[i], rules, i);
            }
            Widgets.EndScrollView();

            Rect reset = new Rect(inRect.x, inRect.yMax - CloseButSize.y, 160f, CloseButSize.y);
            if (Widgets.ButtonText(reset, "AGM_DropRules_Reset".Translate()))
            {
                rules.ResetAll();
            }
        }

        private static void AddWithChildren(DropCategoryDef def, List<DropCategoryDef> rows)
        {
            rows.Add(def);
            foreach (DropCategoryDef child in def.Children)
            {
                AddWithChildren(child, rows);
            }
        }

        private static void DrawRow(Rect row, DropCategoryDef def, DropRules rules, int index)
        {
            if (index % 2 == 1)
            {
                Widgets.DrawLightHighlight(row);
            }
            Widgets.DrawHighlightIfMouseover(row);
            if (!def.description.NullOrEmpty())
            {
                TooltipHandler.TipRegion(row, def.description);
            }
            float x = row.x + def.Depth * Indent;
            using (new TextBlock(TextAnchor.MiddleLeft))
            {
                Widgets.Label(new Rect(x, row.y, row.width - ButtonWidth - (x - row.x), row.height), def.LabelCap);
            }

            DropAction effective = rules.Effective(def);
            bool inherited = !rules.Explicit(def).HasValue && def.parent != null;
            Rect button = new Rect(row.xMax - ButtonWidth, row.y + 2f, ButtonWidth, row.height - 4f);
            string label = ActionLabel(effective);
            Color textColor = inherited ? Color.gray : (effective == DropAction.Ignore ? ColoredText.SubtleGrayColor : Widgets.NormalOptionColor);
            if (Widgets.ButtonText(button, inherited ? "AGM_DropRules_Inherited".Translate(label) : (TaggedString)label,
                    drawBackground: true, doMouseoverSound: true, textColor: textColor))
            {
                var options = new List<FloatMenuOption>();
                if (def.parent != null)
                {
                    options.Add(new FloatMenuOption("AGM_DropRules_SameAs".Translate(def.parent.label, ActionLabel(rules.Effective(def.parent))),
                        () => rules.Set(def, null)));
                }
                foreach (DropAction action in def.actions)
                {
                    options.Add(new FloatMenuOption(ActionLabel(action), () => rules.Set(def, action)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
        }
    }
}
