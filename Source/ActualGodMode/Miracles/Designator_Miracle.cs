using RimWorld;
using UnityEngine;
using Verse;

namespace ActualGodMode
{
    /// <summary>Click-to-target casting of one miracle level (toolbar casting; gestures come in M3).</summary>
    public class Designator_Miracle : Designator
    {
        private readonly MiracleDef miracle;
        private readonly int levelIndex;

        private MiracleLevel Level => miracle.levels[levelIndex];

        public Designator_Miracle(MiracleDef miracle, int levelIndex)
        {
            this.miracle = miracle;
            this.levelIndex = levelIndex;
            defaultLabel = miracle.LevelLabel(levelIndex);
            defaultDesc = miracle.description;
            icon = miracle.Icon;
            useMouseIcon = true;
        }

        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            return MiracleUtility.CanCast(miracle, levelIndex, loc, Map);
        }

        public override void DesignateSingleCell(IntVec3 c)
        {
            AcceptanceReport result = MiracleUtility.TryCast(miracle, levelIndex, c, Map);
            if (!result.Accepted && !result.Reason.NullOrEmpty())
            {
                Messages.Message(result.Reason, MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        public override void SelectedUpdate()
        {
            InfluenceUtility.DrawInfluence(Map);
            IntVec3 mouse = UI.MouseCell();
            if (mouse.InBounds(Map))
            {
                miracle.Worker.DrawPreview(mouse, Map, Level);
            }
        }

        public override void DrawMouseAttachments()
        {
            base.DrawMouseAttachments();
            Vector2 pos = Event.current.mousePosition + new Vector2(20f, 20f);
            MiracleCostLabel.Draw(new Rect(pos.x, pos.y - 18f, 260f, 22f), null, Level, UI.MouseCell(), Map);
        }
    }
}
