using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Guilt
{
    /// <summary>The conscience thought, scaled by the intensity setting.</summary>
    public class Thought_Conscience : Thought_Situational
    {
        public override float MoodOffset()
        {
            float baseOffset = base.MoodOffset();
            return baseOffset == 0f ? 0f : baseOffset * Tuning.Intensity;
        }
    }

    public class ThoughtWorker_Conscience : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            GameComponent_Guilt ledger = GameComponent_Guilt.Instance;
            if (ledger == null || !GU.CanFeelGuilt(p))
            {
                return ThoughtState.Inactive;
            }
            float load = ledger.Load(p);
            if (load < Tuning.StageOne)
            {
                return ThoughtState.Inactive;
            }
            // Once something has closed, it never presses harder than the first stage again.
            if (GU.IsCalloused(p))
            {
                return ThoughtState.ActiveAtStage(0);
            }
            if (load >= Tuning.StageThree)
            {
                return ThoughtState.ActiveAtStage(2);
            }
            return load >= Tuning.StageTwo ? ThoughtState.ActiveAtStage(1) : ThoughtState.ActiveAtStage(0);
        }
    }

    /// <summary>The flat, quiet cost of having stopped feeling it.</summary>
    public class ThoughtWorker_Calloused : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            return GU.IsCalloused(p) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>They cannot be around the family of someone they killed.</summary>
    public class ThoughtWorker_CantLookAtThem : ThoughtWorker
    {
        protected override ThoughtState CurrentSocialStateInternal(Pawn p, Pawn other)
        {
            GameComponent_Guilt ledger = GameComponent_Guilt.Instance;
            if (ledger == null || !Tuning.CantLookAtThem || !GU.CanFeelGuilt(p) || other == null || other == p)
            {
                return ThoughtState.Inactive;
            }
            return ledger.WrongedKinOf(p, other) ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Telling someone. Only available to a colonist genuinely carrying something, and never
    /// to the family of the person they wronged: that is a different conversation entirely.
    /// </summary>
    public class InteractionWorker_Confess : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            GameComponent_Guilt ledger = GameComponent_Guilt.Instance;
            if (ledger == null || !Tuning.Confession || !GU.CanFeelGuilt(initiator))
            {
                return 0f;
            }
            if (recipient?.RaceProps == null || !recipient.RaceProps.Humanlike ||
                recipient.DevelopmentalStage.Juvenile())
            {
                return 0f;
            }
            if (GU.IsCalloused(initiator) || ledger.Load(initiator) < Tuning.StageTwo)
            {
                return 0f;
            }
            // Not to the bereaved.
            if (ledger.WrongedKinOf(initiator, recipient))
            {
                return 0f;
            }
            // They tell someone they trust.
            if (initiator.relations != null && initiator.relations.OpinionOf(recipient) < 20)
            {
                return 0f;
            }
            return Tuning.ConfessWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            GameComponent_Guilt ledger = GameComponent_Guilt.Instance;
            if (ledger == null)
            {
                return;
            }
            ledger.Confess(initiator);
            initiator.needs?.mood?.thoughts?.memories?.TryGainMemory(GU_DefOf.GU_Confessed);
        }
    }
}
