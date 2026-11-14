using System.Collections.Generic;
using Verse;

namespace ActualGodMode
{
    /// <summary>
    /// The player's drop rules, saved per game: for each <see cref="DropCategoryDef"/>, what a colonist
    /// set down on such a thing does. A category with nothing set takes its parent's action (when it
    /// applies there), else its own default. Defaults are "do the work there", as without rules.
    /// </summary>
    public class DropRules : GameComponent
    {
        private Dictionary<string, DropAction> rules = new Dictionary<string, DropAction>();

        public static DropRules Instance => Current.Game?.GetComponent<DropRules>();

        public DropRules(Game game)
        {
        }

        /// <summary>The action set on this category itself (null = same as its parent).</summary>
        public DropAction? Explicit(DropCategoryDef def) =>
            rules.TryGetValue(def.defName, out DropAction a) && def.actions.Contains(a) ? a : (DropAction?)null;

        public void Set(DropCategoryDef def, DropAction? action)
        {
            if (action.HasValue && def.actions.Contains(action.Value))
            {
                rules[def.defName] = action.Value;
            }
            else
            {
                rules.Remove(def.defName);
            }
        }

        public void ResetAll() => rules.Clear();

        public DropAction Effective(DropCategoryDef def)
        {
            DropAction? own = Explicit(def);
            if (own.HasValue)
            {
                return own.Value;
            }
            if (def.parent != null)
            {
                DropAction inherited = Effective(def.parent);
                if (def.actions.Contains(inherited))
                {
                    return inherited;
                }
            }
            return def.defaultAction;
        }

        /// <summary>The effective action, even without a game (the default).</summary>
        public static DropAction For(DropCategoryDef def) => Instance?.Effective(def) ?? def.defaultAction;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref rules, "rules", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                rules ??= new Dictionary<string, DropAction>();
            }
        }
    }
}
