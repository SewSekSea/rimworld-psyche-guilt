using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Guilt
{
    [DefOf]
    public static class GU_DefOf
    {
        public static ThoughtDef GU_Conscience;
        public static ThoughtDef GU_Confessed;
        public static ThoughtDef GU_KnowsWhatTheyDid;
        public static HediffDef GU_Calloused;
        public static InteractionDef GU_Confess;

        static GU_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GU_DefOf));
        }
    }

    /// <summary>All the tuning numbers in one place. The ones players can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static GuiltSettings S => GuiltMod.Settings;

        /// <summary>Days for a deed to weigh half what it did. Guilt fades; it does not vanish.</summary>
        public static float HalfLifeDays => S.halfLifeDays;

        /// <summary>Overall multiplier on how hard a conscience presses.</summary>
        public static float Intensity => S.intensity;

        /// <summary>Total weight at which something closes instead.</summary>
        public static float CallousAt => S.callousAt;

        public static float ConfessWeight => S.confessWeight;
        public static bool CantLookAtThem => S.cantLookAtThem;
        public static bool Confession => S.confession;
        public static bool Callousing => S.callousing;

        /// <summary>Stage thresholds for the conscience thought.</summary>
        public const float StageOne = 1f;
        public const float StageTwo = 2.5f;
        public const float StageThree = 5f;

        /// <summary>How much of the load telling someone takes off.</summary>
        public const float ConfessionRelief = 0.45f;

        /// <summary>A deed stops counting entirely once it is this many half-lives old.</summary>
        public const float ForgetAfterHalfLives = 6f;
    }

    [StaticConstructorOnStartup]
    public static class GU
    {
        /// <summary>
        /// Deeds the base game records as a memory on the person who did it, or who heard about it.
        /// </summary>
        public static readonly Dictionary<ThoughtDef, float> DeedWeights = new Dictionary<ThoughtDef, float>();

        /// <summary>
        /// Deeds the base game does not record as a memory at all. An execution is a "tale": other
        /// colonists form an opinion of the executioner from it, but the executioner is handed nothing
        /// to remember. Those are picked up from the tale instead, which is the only place they exist.
        /// </summary>
        public static readonly Dictionary<TaleDef, float> TaleWeights = new Dictionary<TaleDef, float>();

        /// <summary>Machiavelli's rating, if installed. Someone they had written off weighs less on them.</summary>
        public static readonly ThoughtDef RatedDeadWeight;

        static GU()
        {
            // Done with their own hands, and remembered.
            Add("ButcheredHumanlikeCorpse", 0.8f);
            Add("ColonistBanishedToDie", 1.2f);
            Add("PrisonerBanishedToDie", 0.9f);

            // Done with their own hands, but recorded only as a tale.
            AddTale("ExecutedPrisoner", 1f);
            AddTale("SoldPrisoner", 0.5f);

            // Knew it happened and let it.
            Add("KnowColonistExecuted", 0.3f);
            Add("KnowGuestExecuted", 0.3f);
            Add("KnowColonistOrganHarvested", 0.35f);
            Add("KnowGuestOrganHarvested", 0.35f);
            Add("KnowButcheredHumanlikeCorpse", 0.2f);
            Add("KnowPrisonerSold", 0.15f);
            Add("KnowPrisonerDiedInnocent", 0.4f);

            RatedDeadWeight = DefDatabase<ThoughtDef>.GetNamedSilentFail("MAC_DeadWeight");

            new Harmony("guilt.conscience").PatchAll();
            Log.Message($"[Guilt] Loaded. Weighing {DeedWeights.Count} remembered deeds and {TaleWeights.Count} recorded ones.");
        }

        private static void Add(string defName, float weight)
        {
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                DeedWeights[def] = weight;
            }
        }

        private static void AddTale(string defName, float weight)
        {
            TaleDef def = DefDatabase<TaleDef>.GetNamedSilentFail(defName);
            if (def != null)
            {
                TaleWeights[def] = weight;
            }
        }

        /// <summary>The weight of this memory as a deed, or 0 if it is not one.</summary>
        public static float WeightOf(ThoughtDef def)
        {
            if (def == null)
            {
                return 0f;
            }
            return DeedWeights.TryGetValue(def, out float w) ? w : 0f;
        }

        /// <summary>The weight of this tale as a deed, or 0 if it is not one.</summary>
        public static float WeightOfTale(TaleDef def)
        {
            if (def == null)
            {
                return 0f;
            }
            return TaleWeights.TryGetValue(def, out float w) ? w : 0f;
        }

        /// <summary>Killing one of your own, by your own hand. The heaviest thing on the list.</summary>
        public const float KilledOneOfOursWeight = 1.6f;

        public static bool CanFeelGuilt(Pawn p)
        {
            if (p == null || p.Dead || p.RaceProps == null || !p.RaceProps.Humanlike ||
                p.needs?.mood == null || p.DevelopmentalStage.Juvenile())
            {
                return false;
            }
            // The same traits the thought nullifies. Checked here too so nothing is even recorded.
            if (p.story?.traits != null &&
                (p.story.traits.HasTrait(TraitDefOf.Psychopath) || p.story.traits.HasTrait(TraitDefOf.Bloodlust)))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// Offers that the base game merged into an existing memory rather than adding.
        ///
        /// A memory can be taken on in two ways: added to the list as a new object, or merged into
        /// an existing one, which discards the offered object entirely. It can also be refused, by
        /// the base game or by another mod cancelling the call. Telling the three apart by
        /// inspecting the list afterwards does not work: several memories gained in the same tick
        /// all sit at age zero, so a merge leaves no visible trace. The merge is therefore observed
        /// where it happens instead of being inferred.
        ///
        /// Keyed by the offered object so that nested calls, which happen whenever one thought
        /// spawns another, cannot be confused with each other.
        /// </summary>
        private static readonly HashSet<Thought_Memory> mergedOffers = new HashSet<Thought_Memory>();

        public static void NoteMerged(Thought_Memory offered)
        {
            if (offered == null)
            {
                return;
            }
            // Nothing should accumulate here; each offer is claimed by the call that made it.
            if (mergedOffers.Count > 64)
            {
                mergedOffers.Clear();
            }
            mergedOffers.Add(offered);
        }

        /// <summary>Did the colonist genuinely take this memory on? Consumes the merge note.</summary>
        public static bool WasAccepted(MemoryThoughtHandler handler, Thought_Memory offered)
        {
            bool merged = offered != null && mergedOffers.Remove(offered);
            if (merged)
            {
                return true;
            }
            List<Thought_Memory> memories = handler?.Memories;
            return memories != null && offered != null && memories.Contains(offered);
        }

        /// <summary>The Sociopath trait, if that mod is installed. Looked up fresh, never required.</summary>
        private static TraitDef SociopathDef => DefDatabase<TraitDef>.GetNamedSilentFail("SP_Sociopath");

        /// <summary>
        /// Whether this deed should weigh on this person at all, given who it was done to.
        ///
        /// Executions and prisoner sales reach this mod through a tale rather than a memory, and
        /// the Sociopath mod does not touch tales. Without this check a Sociopath would quietly
        /// accumulate guilt for exactly the deeds they are supposed to feel nothing about.
        ///
        /// The inner circle test here mirrors the Sociopath mod's own rule rather than calling into
        /// it, so this mod never depends on it. If that mod's inner-circle setting is changed from
        /// its default, this stays on the default line.
        /// </summary>
        public static bool DeedWeighsOn(Pawn doer, Pawn victim)
        {
            if (!CanFeelGuilt(doer))
            {
                return false;
            }
            TraitDef socio = SociopathDef;
            if (socio == null || doer.story?.traits == null || !doer.story.traits.HasTrait(socio))
            {
                return true;
            }
            // A Sociopath still feels it for the few people who actually matter to them.
            return victim != null && InInnerCircle(doer, victim);
        }

        private static bool InInnerCircle(Pawn pawn, Pawn other)
        {
            if (pawn == null || other == null || pawn == other)
            {
                return false;
            }
            foreach (PawnRelationDef rel in pawn.GetRelations(other))
            {
                if (rel.familyByBloodRelation || rel == PawnRelationDefOf.Spouse ||
                    rel == PawnRelationDefOf.Lover || rel == PawnRelationDefOf.Fiance)
                {
                    return true;
                }
            }
            return pawn.relations != null && pawn.relations.OpinionOf(other) >= 60;
        }

        public static bool IsCalloused(Pawn p)
        {
            return p?.health?.hediffSet != null && p.health.hediffSet.HasHediff(GU_DefOf.GU_Calloused);
        }

        /// <summary>Close family of the dead: the people the guilty one cannot face.</summary>
        public static bool IsKinOf(Pawn person, Pawn victim)
        {
            if (person == null || victim == null || person == victim || person.relations == null)
            {
                return false;
            }
            foreach (PawnRelationDef rel in person.GetRelations(victim))
            {
                if (rel.familyByBloodRelation || rel == PawnRelationDefOf.Spouse ||
                    rel == PawnRelationDefOf.Lover || rel == PawnRelationDefOf.Fiance)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
