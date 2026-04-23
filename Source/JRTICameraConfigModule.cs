namespace JustReadTheInstructions
{
    public class JRTICameraConfigModule : PartModule
    {
        [KSPField(isPersistant = true)]
        public string jrtiName = "";

        [KSPField(isPersistant = true)]
        public int jrtiId = 0;

        [KSPEvent(
            guiName = "Set Name",
            guiActiveEditor = true,
            guiActive = false,
            groupName = "JRTI",
            groupDisplayName = "JRTI",
            groupStartCollapsed = false
        )]
        public void EventSetName()
        {
            string current = jrtiName;
            PopupDialog.SpawnPopupDialog(new MultiOptionDialog(
                "JRTISetName",
                "Leave blank to use the default camera name.",
                "Set Camera Name",
                HighLogic.UISkin,
                new DialogGUITextInput(current, false, 64, s => { current = s; return s; }, 300f),
                new DialogGUIButton("Confirm", () => { jrtiName = current; }, true),
                new DialogGUIButton("Default", () => { jrtiName = ""; }, true)
            ), true, HighLogic.UISkin);
        }

        [KSPEvent(
            guiName = "Set ID",
            guiActiveEditor = true,
            guiActive = false,
            groupName = "JRTI",
            groupDisplayName = "JRTI",
            groupStartCollapsed = false
        )]
        public void EventSetId()
        {
            string current = jrtiId > 0 ? jrtiId.ToString() : "";
            PopupDialog.SpawnPopupDialog(new MultiOptionDialog(
                "JRTISetId",
                "Set a unique numeric ID (1 or higher) for this camera.",
                "Set Camera ID",
                HighLogic.UISkin,
                new DialogGUITextInput(current, false, 8, s => { current = s; return s; }, 200f),
                new DialogGUIButton("Confirm", () =>
                {
                    if (int.TryParse(current, out int id) && id >= 1)
                        jrtiId = id;
                }, true),
                new DialogGUIButton("Cancel", () => { }, true)
            ), true, HighLogic.UISkin);
        }
    }
}
