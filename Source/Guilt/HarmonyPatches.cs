using HarmonyLib;
using RimWorld;
using Verse;

namespace Guilt
{
    /// <summary>
    /// Deeds picked up from the memories the base game hands out, so anything that suppresses
    /// those memories suppresses the guilt with them.
    ///
    /// That only holds if the memory is checked rather than assumed. A Harmony prefix that
    /// returns false skips the original method, but every postfix still runs, so this has to
    /// confirm the memory genuinely landed before recording anything. Without that check, any
    /// mod that cancels one of these memories leaves the deed recorded anyway, and the same
    /// happens when the base game refuses it for exceeding a stack limit.
    ///
    /// The original memory is left alone. The immediate sting of having just done something and the
    /// slow weight of having done it are two different feelings, and this mod only adds the second.
    /// </summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    [HarmonyAfter("sociopath.trait")]
    public static class Patch_TryGainMemory
    {
        /// <summary>
        /// Marks that this call reached the original method at all. If an earlier prefix cancels,
        /// this never runs and the state arrives false, which is itself the answer: nothing happened.
        /// </summary>
        public static void Prefix(out bool __state)
        {
            __state = true;
        }

        public static void Postfix(MemoryThoughtHandler __instance, Thought_Memory newThought,
            Pawn otherPawn, bool __state)
        {
            if (!__state || newThought?.def == null)
            {
                return;
            }
            float weight = GU.WeightOf(newThought.def);
            if (weight <= 0f)
            {
                return;
            }
            if (!GU.WasAccepted(__instance, newThought))
            {
                return;
            }
            GameComponent_Guilt.Instance?.Record(__instance.pawn, otherPawn ?? newThought.otherPawn, weight);
        }
    }

    /// <summary>
    /// Watches for the base game merging an offered memory into an existing one. That path
    /// discards the offered object, so it is the only place a merge can be seen reliably.
    /// </summary>
    [HarmonyPatch(typeof(Thought_Memory), nameof(Thought_Memory.TryMergeWithExistingMemory))]
    public static class Patch_TryMergeWithExistingMemory
    {
        public static void Postfix(Thought_Memory __instance, bool __result)
        {
            if (__result && __instance?.def != null && GU.WeightOf(__instance.def) > 0f)
            {
                GU.NoteMerged(__instance);
            }
        }
    }

    /// <summary>
    /// Executions and prisoner sales. The base game hands the person who did it no memory at all:
    /// it files a tale, which other colonists then form an opinion from. The tale is therefore the
    /// only place the deed exists, so this is where it is picked up.
    ///
    /// The tale's arguments are the people involved, doer first.
    /// </summary>
    [HarmonyPatch(typeof(TaleRecorder), nameof(TaleRecorder.RecordTale),
        new[] { typeof(TaleDef), typeof(object[]) })]
    public static class Patch_RecordTale
    {
        public static void Postfix(TaleDef def, object[] args)
        {
            float weight = GU.WeightOfTale(def);
            if (weight <= 0f || args == null)
            {
                return;
            }
            Pawn doer = null;
            Pawn victim = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!(args[i] is Pawn p))
                {
                    continue;
                }
                if (doer == null) { doer = p; }
                else if (victim == null) { victim = p; break; }
            }
            if (doer == null)
            {
                return;
            }
            // A tale is filed whatever the colony believes, but the matching thought is not.
            // With Ideology, an ideoligion can hold executions to be required, respected or
            // simply nobody's business, and those precepts nullify the thought. Reading the
            // tale alone ignored all of that and handed a conscience to a colonist whose
            // religion demands the thing they just did. The thought shares the tale's name,
            // so the game's own eligibility check answers this properly, precepts included.
            ThoughtDef matching = DefDatabase<ThoughtDef>.GetNamedSilentFail(def.defName);
            if (matching != null && !ThoughtUtility.CanGetThought(doer, matching))
            {
                return;
            }
            GameComponent_Guilt.Instance?.Record(doer, victim, weight);
        }
    }

    /// <summary>
    /// Killing one of your own by your own hand. Heavier than anything on the memory list, and
    /// recorded with the victim's name so the mod knows whose family they now cannot face.
    /// </summary>
    [HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility), nameof(PawnDiedOrDownedThoughtsUtility.TryGiveThoughts),
        new[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
    public static class Patch_TryGiveThoughts
    {
        public static void Postfix(Pawn victim, DamageInfo? dinfo, PawnDiedOrDownedThoughtsKind thoughtsKind)
        {
            if (thoughtsKind != PawnDiedOrDownedThoughtsKind.Died || victim?.RaceProps == null ||
                !victim.RaceProps.Humanlike)
            {
                return;
            }
            if (!(dinfo?.Instigator is Pawn killer) || killer == victim)
            {
                return;
            }
            // Only killing your own people counts. A raider is not a thing anyone here regrets.
            bool oneOfOurs = victim.IsColonist ||
                             (victim.Faction != null && killer.Faction != null &&
                              victim.Faction == killer.Faction);
            if (!oneOfOurs || !killer.IsColonist)
            {
                return;
            }
            GameComponent_Guilt.Instance?.Record(killer, victim, GU.KilledOneOfOursWeight);
        }
    }
}
