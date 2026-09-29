using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class CameraLookup
    {
        private static Camera[] _buffer = new Camera[16];

        public static Camera FindActive(string cameraName)
        {
            if (_buffer.Length < Camera.allCamerasCount)
                _buffer = new Camera[Camera.allCamerasCount];

            int count = Camera.GetAllCameras(_buffer);
            for (int i = 0; i < count; i++)
            {
                if (_buffer[i].name == cameraName)
                    return _buffer[i];
            }

            return null;
        }
    }
}
