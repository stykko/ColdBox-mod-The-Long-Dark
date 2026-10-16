using Il2CppInterop.Runtime.Injection;
using MelonLoader;

[assembly: MelonInfo(typeof(ColdBox.Core), "ColdBox", "0.5.0", "Jere")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace ColdBox
{
    public class Core : MelonMod
    {
        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<ColdBoxBehaviour>();
            ColdBoxSettings.Init();
            LoggerInstance.Msg("ColdBox loaded. Craft the Snow Cooler at the workbench; hold the interact button on a placed cooler to open the lid.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            ColdBoxSave.MarkSceneChanged();
        }

        public override void OnUpdate()
        {
            ColdBoxManager.Update();
        }

        public override void OnGUI()
        {
            ColdBoxManager.DrawFallbackMenu();
        }
    }
}
