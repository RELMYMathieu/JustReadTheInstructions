using UnityEngine;

namespace JustReadTheInstructions
{
    public class CameraSynchronizer : MonoBehaviour
    {
        public Camera SourceCamera { get; set; }
        public bool RotationOnly { get; set; }

        void OnPreRender() => Sync();

        public void ManualSync() => Sync();

        private void Sync()
        {
            if (SourceCamera == null || SourceCamera.transform == null || transform == null)
                return;

            if (!RotationOnly)
                transform.position = ScaledSpace.LocalToScaledSpace(SourceCamera.transform.position);
            transform.rotation = SourceCamera.transform.rotation;
        }
    }
}