using System;
using System.Linq;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class TrajectoriesIntegration
    {
        private const string LineObjectName = "Trajectories_LineRenderer";

        private static bool? _isAvailable;
        private static Renderer _line;

        public static bool IsAvailable
        {
            get
            {
                if (_isAvailable.HasValue)
                    return _isAvailable.Value;

                _isAvailable = AssemblyLoader.loadedAssemblies
                    .Any(a => a.name.Equals("Trajectories", StringComparison.OrdinalIgnoreCase));

                Debug.Log(_isAvailable.Value
                    ? "[JRTI-Trajectories]: Integration enabled"
                    : "[JRTI-Trajectories]: Trajectories not found");
                return _isAvailable.Value;
            }
        }

        public static Renderer Line
        {
            get
            {
                if (!IsAvailable)
                    return null;

                if (_line == null)
                    _line = FindLine();
                return _line;
            }
        }

        private static Renderer FindLine()
        {
            var mainCamera = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            var lineObject = mainCamera != null ? mainCamera.transform.Find(LineObjectName) : null;
            return lineObject != null ? lineObject.GetComponent<LineRenderer>() : null;
        }
    }
}
