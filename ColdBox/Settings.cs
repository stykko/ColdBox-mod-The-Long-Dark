using ModSettings;
using UnityEngine;

namespace ColdBox
{
    // Uses ModSettings.dll (already installed): shows up in the in-game "Mod Settings" menu.
    internal class ColdBoxSettings : JsonModSettings
    {
        internal static ColdBoxSettings Instance { get; private set; }

        /// <summary>Interface language of the mod (Mod Settings texts, action menu, messages).</summary>
        public enum UiLanguage { English = 0, Russian = 1 }

        [Section("Language / Язык")]

        [Name("Language / Язык")]
        [Description("Interface language of the mod. / Язык интерфейса мода.")]
        [Choice("English", "Русский")]
        public UiLanguage language = UiLanguage.English;

        [Section("Snow Cooler")]

        [Name("Snow load duration (days)")]
        [Description("How many game days a full snow load keeps the cooler cold (5 to 10). This is also the snow consumption speed.")]
        [Slider(5f, 10f, 11)]
        public float daysPerLoad = 7f;

        [Name("Food decay while cold")]
        [Description("Fraction of the normal decay that stored food suffers while there is snow (cooling efficiency). 1 = same as outside, 0.5 = half as fast.")]
        [Slider(0.1f, 1f, 19)]
        public float decayMultiplier = 0.5f;

        [Name("Water from melted snow (liters)")]
        [Description("Liters of NON-potable water that a full snow load leaves once it has melted.")]
        [Slider(1f, 10f, 10)]
        public float litersPerLoad = 5f;

        [Name("Food capacity (kg)")]
        [Description("Maximum weight of food that fits in the cooler.")]
        [Slider(10f, 100f, 19)]
        public float capacityKg = 40f;

        [Name("Enable water from melted snow")]
        [Description("When enabled, melted snow can be collected as non-potable water (added to the player's water supply).")]
        public bool enableWater = true;

        [Section("Interaction")]

        [Name("Time to open the lid (seconds)")]
        [Description("How long the interact button must be held while looking at the cooler.")]
        [Slider(0.3f, 3f, 28)]
        public float holdSeconds = 1f;

        [Name("Interaction distance (m)")]
        [Slider(1f, 4f, 31)]
        public float interactDistance = 2.2f;

        [Name("Fallback menu (keys 1-5)")]
        [Description("Uses a simple built-in menu instead of the game action picker. Enable it only if the game picker does not display correctly.")]
        public bool useFallbackMenu = false;

        [Name("Lid opening angle (degrees)")]
        [Description("How far the lid opens.")]
        [Slider(30f, 110f, 17)]
        public float lidOpenAngle = 60f;

        [Name("Cold mist when opened")]
        [Description("A little mist comes out when you open the cooler while it still has snow.")]
        public bool enableSteam = true;

        [Name("Flip lid side")]
        [Description("The lid normally opens away from you. Enable this if it opens towards you instead.")]
        public bool invertLidSide = false;

        [Section("Placement")]

        [Name("Degrees per rotation step")]
        [Slider(5f, 45f, 9)]
        public float rotateStepDegrees = 15f;

        [Section("Crafting")]

        [Name("Craftable at the workbench")]
        [Description("Adds the Snow Cooler recipe (10 cedar wood, 5 scrap metal, 5 tarps, quality tools). Takes effect on the next game start.")]
        public bool enableCrafting = true;

        [Name("Crafting time (minutes)")]
        [Slider(30f, 480f, 16)]
        public float craftMinutes = 240f;

        /// <summary>Game hours that a full snow load lasts.</summary>
        internal float HoursPerLoad => daysPerLoad * 24f;

        internal static void Init()
        {
            Instance = new ColdBoxSettings();
            Instance.AddToModSettings("Snow Cooler");
        }
    }
}
