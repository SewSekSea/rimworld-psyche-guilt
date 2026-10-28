using UnityEngine;
using Verse;

namespace Guilt
{
    public class GuiltSettings : ModSettings
    {
        public float halfLifeDays = 25f;
        public float intensity = 1f;
        public float callousAt = 9f;
        public float confessWeight = 0.2f;

        public bool cantLookAtThem = true;
        public bool confession = true;
        public bool callousing = true;

        public override void ExposeData()
        {
            GuiltSettings d = new GuiltSettings();
            Scribe_Values.Look(ref halfLifeDays, "halfLifeDays", d.halfLifeDays);
            Scribe_Values.Look(ref intensity, "intensity", d.intensity);
            Scribe_Values.Look(ref callousAt, "callousAt", d.callousAt);
            Scribe_Values.Look(ref confessWeight, "confessWeight", d.confessWeight);
            Scribe_Values.Look(ref cantLookAtThem, "cantLookAtThem", true);
            Scribe_Values.Look(ref confession, "confession", true);
            Scribe_Values.Look(ref callousing, "callousing", true);
        }

        public void Reset()
        {
            GuiltSettings d = new GuiltSettings();
            halfLifeDays = d.halfLifeDays;
            intensity = d.intensity;
            callousAt = d.callousAt;
            confessWeight = d.confessWeight;
            cantLookAtThem = confession = callousing = true;
        }
    }

    public class GuiltMod : Mod
    {
        public static GuiltSettings Settings = new GuiltSettings();

        public GuiltMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<GuiltSettings>();
        }

        public override string SettingsCategory() => "Guilt";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            GuiltSettings s = Settings;
            Listing_Standard list = new Listing_Standard { ColumnWidth = (inRect.width - 34f) / 2f };
            list.Begin(inRect);

            list.Label("How a conscience works");
            s.halfLifeDays = list.SliderLabeled($"A deed weighs half as much after {s.halfLifeDays:0} days",
                s.halfLifeDays, 5f, 90f, 0.6f,
                "Guilt fades. It does not vanish. A shorter figure makes colonists get over things quickly; a longer one makes a record of what you have asked them to do.");
            s.intensity = list.SliderLabeled($"Felt at {s.intensity:P0}",
                s.intensity, 0.25f, 2f, 0.6f);
            s.callousAt = list.SliderLabeled($"Something closes at a load of {s.callousAt:0.0}",
                s.callousAt, 4f, 20f, 0.6f,
                "One execution weighs 1.0 and killing one of your own weighs 1.6, before fading. Raise this to make colonists carry more before they stop feeling it.");
            s.confessWeight = list.SliderLabeled($"Confession frequency {s.confessWeight:0.00}",
                s.confessWeight, 0f, 1f, 0.6f,
                "How often a colonist carrying something tells someone they trust. Vanilla chit-chat is 1.00.");

            list.NewColumn();
            list.Label("Optional behaviours");
            list.CheckboxLabeled("Can't look at them", ref s.cantLookAtThem,
                "A colonist who killed someone cannot be around that person's close family. The guilt points outwards as a lower opinion of the bereaved.");
            list.CheckboxLabeled("Confession", ref s.confession,
                "A colonist carrying enough will tell someone they trust. It lifts a good share of the weight, and costs them that person's good opinion for a while.");
            list.CheckboxLabeled("Callousing", ref s.callousing,
                "Past a certain load, a colonist stops feeling it. Switch this off and guilt simply keeps accumulating with no upper end.");

            list.Gap(12f);
            list.Label("Psychopaths and the bloodlusting never feel any of this. Neither do Sociopaths, for anyone outside their inner circle, if you run that mod.");

            list.Gap(24f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }
            list.End();
        }
    }
}
