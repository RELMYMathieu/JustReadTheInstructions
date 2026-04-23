namespace JustReadTheInstructions
{
    public class JRTICameraConfigModule : PartModule
    {
        [KSPField(isPersistant = true)]
        public string jrtiName = "";

        [KSPField(isPersistant = true)]
        public int jrtiId = 0;
    }
}
