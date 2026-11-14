using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace ActualGodMode
{
    /// <summary>What a colonist set down by the god's hand does with what's under them (see <see cref="DropRules"/>).</summary>
    public enum DropAction
    {
        /// <summary>The first work they're assigned to there (the vanilla right-click "Prioritize ..."): the default.</summary>
        Work,
        /// <summary>Haul the thing to storage (hauling work only).</summary>
        Haul,
        Wear,
        Equip,
        /// <summary>Eat it (or take it, for drugs).</summary>
        Eat,
        /// <summary>Just set them down.</summary>
        Ignore,
    }

    /// <summary>
    /// A kind of thing a colonist can be set down on, in a tree like the storage filter (items → apparel,
    /// buildings → workbenches, plants → trees, …). Each offers the actions that make sense for it; the
    /// player picks one per category in the Drop rules window, and children inherit from their parent.
    /// Matching is done by the <see cref="workerClass"/> (and <see cref="thingCategories"/> for items).
    /// </summary>
    public class DropCategoryDef : Def
    {
        public DropCategoryDef parent;
        public int order;
        public Type workerClass = typeof(DropCategoryWorker);
        /// <summary>Vanilla item categories this one covers (items only; empty = any).</summary>
        public List<ThingCategoryDef> thingCategories = new List<ThingCategoryDef>();
        public List<DropAction> actions = new List<DropAction>();
        /// <summary>Used when nothing is set here and the parent's action doesn't apply to this category.</summary>
        public DropAction defaultAction = DropAction.Work;

        [Unsaved] private DropCategoryWorker workerInt;
        [Unsaved] private List<DropCategoryDef> childrenInt;

        public DropCategoryWorker Worker => workerInt ??= MakeWorker();

        private DropCategoryWorker MakeWorker()
        {
            var w = (DropCategoryWorker)Activator.CreateInstance(workerClass);
            w.def = this;
            return w;
        }

        public List<DropCategoryDef> Children =>
            childrenInt ??= DefDatabase<DropCategoryDef>.AllDefsListForReading.Where(d => d.parent == this).OrderBy(d => d.order).ToList();

        public static IEnumerable<DropCategoryDef> TopLevel =>
            DefDatabase<DropCategoryDef>.AllDefsListForReading.Where(d => d.parent == null).OrderBy(d => d.order);

        public int Depth => parent == null ? 0 : parent.Depth + 1;

        /// <summary>The most specific category for this thing (null if none takes it).</summary>
        public static DropCategoryDef For(Thing t)
        {
            foreach (DropCategoryDef top in TopLevel)
            {
                if (top.Worker.Matches(t))
                {
                    return top.Deepest(c => c.Worker.Matches(t));
                }
            }
            return null;
        }

        /// <summary>The category for a bare cell (sowing ground and the like).</summary>
        public static DropCategoryDef ForCell(IntVec3 cell, Map map)
        {
            foreach (DropCategoryDef top in TopLevel)
            {
                if (top.Worker.MatchesCell(cell, map))
                {
                    return top.Deepest(c => c.Worker.MatchesCell(cell, map));
                }
            }
            return null;
        }

        private DropCategoryDef Deepest(Func<DropCategoryDef, bool> matches)
        {
            foreach (DropCategoryDef child in Children)
            {
                if (matches(child))
                {
                    return child.Deepest(matches);
                }
            }
            return this;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (actions.Count == 0)
            {
                yield return "no actions";
            }
            if (!actions.Contains(defaultAction))
            {
                yield return $"defaultAction {defaultAction} is not one of its actions";
            }
            if (!typeof(DropCategoryWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must be a DropCategoryWorker";
            }
        }
    }

    /// <summary>Decides which things (or cells) belong to a <see cref="DropCategoryDef"/>.</summary>
    public class DropCategoryWorker
    {
        public DropCategoryDef def;

        public virtual bool Matches(Thing t) => false;

        public virtual bool MatchesCell(IntVec3 cell, Map map) => false;
    }

    /// <summary>Items (anything haulable), narrowed by the def's vanilla thing categories.</summary>
    public class DropCategoryWorker_Item : DropCategoryWorker
    {
        public override bool Matches(Thing t) =>
            t.def.category == ThingCategory.Item
            && (def.thingCategories.Count == 0 || def.thingCategories.Any(c => t.def.IsWithinCategory(c)));
    }

    /// <summary>Buildings, blueprints and frames.</summary>
    public class DropCategoryWorker_Building : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t.def.category == ThingCategory.Building || t is Blueprint || t is Frame;
    }

    /// <summary>Workbenches: buildings with bills.</summary>
    public class DropCategoryWorker_Workbench : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Building && t is IBillGiver;
    }

    /// <summary>Blueprints and frames: things waiting to be built.</summary>
    public class DropCategoryWorker_Construction : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Blueprint || t is Frame;
    }

    public class DropCategoryWorker_Plant : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Plant;
    }

    public class DropCategoryWorker_Tree : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Plant p && p.def.plant.IsTree;
    }

    public class DropCategoryWorker_Filth : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Filth;
    }

    public class DropCategoryWorker_Person : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Pawn p && !p.RaceProps.Animal;
    }

    public class DropCategoryWorker_Animal : DropCategoryWorker
    {
        public override bool Matches(Thing t) => t is Pawn p && p.RaceProps.Animal;
    }

    /// <summary>The bare ground (sowing a growing zone, and the like): whatever is under nothing else.</summary>
    public class DropCategoryWorker_Ground : DropCategoryWorker
    {
        public override bool MatchesCell(IntVec3 cell, Map map) => true;
    }
}
