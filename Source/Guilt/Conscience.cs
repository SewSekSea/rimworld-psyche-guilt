using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Guilt
{
    /// <summary>One thing a colonist did, or let happen, and when.</summary>
    public class Deed : IExposable
    {
        public Pawn doer;
        public Pawn victim;      // may be null: not every deed has a named victim
        public float weight;
        public int tick;
        public float relief;     // 0..1, how much of this has been talked out

        public Deed() { }

        public Deed(Pawn doer, Pawn victim, float weight)
        {
            this.doer = doer;
            this.victim = victim;
            this.weight = weight;
            tick = Find.TickManager.TicksGame;
        }

        public bool Valid => doer != null && !doer.Dead;

        /// <summary>Days since it happened.</summary>
        public float AgeDays => (Find.TickManager.TicksGame - tick) / (float)GenDate.TicksPerDay;

        /// <summary>What it still weighs today, after fading and after anything confession took off.</summary>
        public float CurrentWeight
        {
            get
            {
                float half = Tuning.HalfLifeDays;
                if (half <= 0f)
                {
                    return 0f;
                }
                float faded = weight * (float)Math.Pow(0.5, AgeDays / half);
                return faded * (1f - Mathf01(relief));
            }
        }

        /// <summary>True once it is old enough to stop counting at all.</summary>
        public bool Forgotten => AgeDays > Tuning.HalfLifeDays * Tuning.ForgetAfterHalfLives;

        private static float Mathf01(float v)
        {
            if (v < 0f) { return 0f; }
            return v > 1f ? 1f : v;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref doer, "doer");
            Scribe_References.Look(ref victim, "victim");
            Scribe_Values.Look(ref weight, "weight", 0f);
            Scribe_Values.Look(ref tick, "tick", 0);
            Scribe_Values.Look(ref relief, "relief", 0f);
        }
    }

    /// <summary>The conscience ledger: every deed, and what each colonist is carrying because of it.</summary>
    public class GameComponent_Guilt : GameComponent
    {
        private const int TickInterval = 500;

        private List<Deed> deeds = new List<Deed>();
        private static GameComponent_Guilt instance;

        public GameComponent_Guilt(Game game)
        {
            instance = this;
        }

        public static GameComponent_Guilt Instance => instance ?? Current.Game?.GetComponent<GameComponent_Guilt>();

        public override void FinalizeInit()
        {
            instance = this;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref deeds, "deeds", LookMode.Deep);
            if (deeds == null)
            {
                deeds = new List<Deed>();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                deeds.RemoveAll(d => d == null || d.doer == null);
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }
            for (int i = deeds.Count - 1; i >= 0; i--)
            {
                Deed d = deeds[i];
                if (d == null || !d.Valid || d.Forgotten)
                {
                    deeds.RemoveAt(i);
                }
            }
            CheckCallousing();
        }

        // ---- recording ---------------------------------------------------

        public void Record(Pawn doer, Pawn victim, float weight)
        {
            if (weight <= 0f || !GU.DeedWeighsOn(doer, victim))
            {
                return;
            }
            // Machiavelli: someone they had already written off does not weigh the same.
            if (victim != null && GU.RatedDeadWeight != null && GU.RatedDeadWeight.Worker != null)
            {
                try
                {
                    if (GU.RatedDeadWeight.Worker.CurrentSocialState(doer, victim).Active)
                    {
                        weight *= 0.5f;
                    }
                }
                catch
                {
                    // Another mod can refuse for its own reasons. Never let that break a death.
                }
            }
            deeds.Add(new Deed(doer, victim, weight));
        }

        // ---- queries -----------------------------------------------------

        /// <summary>Everything this colonist is still carrying, added up.</summary>
        public float Load(Pawn p)
        {
            float total = 0f;
            for (int i = 0; i < deeds.Count; i++)
            {
                Deed d = deeds[i];
                if (d != null && d.doer == p && d.Valid)
                {
                    total += d.CurrentWeight;
                }
            }
            return total;
        }

        /// <summary>Everything this colonist is carrying. Used by the health tab and the development tests.</summary>
        public List<Deed> DeedsOf(Pawn p, List<Deed> into)
        {
            into.Clear();
            for (int i = 0; i < deeds.Count; i++)
            {
                Deed d = deeds[i];
                if (d != null && d.doer == p && d.Valid)
                {
                    into.Add(d);
                }
            }
            return into;
        }

        public int DeedCount(Pawn p)
        {
            int n = 0;
            for (int i = 0; i < deeds.Count; i++)
            {
                if (deeds[i] != null && deeds[i].doer == p && deeds[i].Valid)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>True if this colonist killed someone closely related to the other one.</summary>
        public bool WrongedKinOf(Pawn doer, Pawn other)
        {
            for (int i = 0; i < deeds.Count; i++)
            {
                Deed d = deeds[i];
                if (d == null || d.doer != doer || d.victim == null || !d.Valid || d.CurrentWeight <= 0.05f)
                {
                    continue;
                }
                if (GU.IsKinOf(other, d.victim))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Telling someone takes a share of the weight off everything they are carrying.</summary>
        public void Confess(Pawn doer)
        {
            for (int i = 0; i < deeds.Count; i++)
            {
                Deed d = deeds[i];
                if (d != null && d.doer == doer && d.Valid)
                {
                    d.relief += Tuning.ConfessionRelief;
                    if (d.relief > 1f)
                    {
                        d.relief = 1f;
                    }
                }
            }
        }

        // ---- callousing --------------------------------------------------

        private void CheckCallousing()
        {
            if (!Tuning.Callousing)
            {
                return;
            }
            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (!GU.CanFeelGuilt(p) || GU.IsCalloused(p))
                {
                    continue;
                }
                if (Load(p) >= Tuning.CallousAt)
                {
                    p.health.AddHediff(GU_DefOf.GU_Calloused);
                    if (PawnUtility.ShouldSendNotificationAbout(p))
                    {
                        Find.LetterStack.ReceiveLetter(
                            "Something has closed",
                            $"{p.LabelShortCap} has done enough now that it has stopped reaching them.\n\n" +
                            "They still know what they did. It simply does not weigh on them the way it did. " +
                            "They will not feel the next one either.",
                            LetterDefOf.NegativeEvent, p);
                    }
                }
            }
        }
    }
}
