using UnityEngine;

namespace JustReadTheInstructions
{
    public class ScattererAtmosphereBinding : MonoBehaviour
    {
        private Camera _camera;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnPreRender() => ScattererScaledAtmosphere.Bind(_camera);

        void OnPostRender() => ScattererScaledAtmosphere.RestoreMain();
    }
}
