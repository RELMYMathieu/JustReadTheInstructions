using System;
using UnityEngine;

namespace JustReadTheInstructions
{
    public static class GalaxySkyboxFade
    {
        private const double SunAboveHorizonStartDegrees = 75.0;
        private const double SunAboveHorizonRate = 0.025;
        private const double GlareMarginDegrees = 10.0;
        private const double GlareRate = 0.05;

        private static readonly MaterialPropertyBlock FeedBlock = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock GameBlock = new MaterialPropertyBlock();
        private static GalaxyCubeControl _cube;
        private static Renderer[] _renderers;

        public static void Render(Camera galaxyCamera, Camera viewCamera)
        {
            var cube = GalaxyCubeControl.Instance;
            if (cube == null || !cube.isActiveAndEnabled || cube.sunRef == null || viewCamera == null)
            {
                galaxyCamera.Render();
                return;
            }

            var renderers = RenderersOf(cube);
            if (renderers.Length == 0)
            {
                galaxyCamera.Render();
                return;
            }

            renderers[0].GetPropertyBlock(GameBlock);
            FeedBlock.SetColor(PropertyIDs._Color, Color.Lerp(cube.maxGalaxyColor, cube.minGalaxyColor, FadeFor(cube, viewCamera)));
            SetBlock(renderers, FeedBlock);
            galaxyCamera.Render();
            SetBlock(renderers, GameBlock);
        }

        private static Renderer[] RenderersOf(GalaxyCubeControl cube)
        {
            if (_cube != cube || _renderers == null)
            {
                _cube = cube;
                _renderers = cube.GetComponentsInChildren<Renderer>();
            }
            return _renderers;
        }

        private static void SetBlock(Renderer[] renderers, MaterialPropertyBlock block)
        {
            foreach (var renderer in renderers)
            {
                if (renderer != null)
                    renderer.SetPropertyBlock(block);
            }
        }

        private static float FadeFor(GalaxyCubeControl cube, Camera viewCamera)
        {
            float glare = GlareFade(cube, viewCamera);
            if (MapView.MapIsEnabled)
                return glare;

            Vector3d position = viewCamera.transform.position;
            var body = FlightGlobals.getMainBody(position);
            if (body == null)
                return glare;

            double pressure = FlightGlobals.getStaticPressure(body.GetAltitude(position), body) * PhysicsGlobals.KpaToAtmospheres;
            if (pressure <= 0.0)
                return glare;

            double sunElevation = UtilMath.Clamp01((AngleDegrees(-cube.sunRef.sunDirection, FlightGlobals.getUpAxis(position)) - SunAboveHorizonStartDegrees) * SunAboveHorizonRate);
            float daytime = (float)pressure * Mathf.Lerp(cube.daytimeFadeLimit, 0f, (float)sunElevation);
            float atmosphere = Mathf.Lerp(0f, cube.atmosFadeLimit, (float)pressure * cube.airPressureFade);
            return atmosphere + daytime + glare;
        }

        private static float GlareFade(GalaxyCubeControl cube, Camera viewCamera)
        {
            if (!SunInSight(viewCamera.transform.position))
                return 0f;

            double sunAngle = AngleDegrees(-cube.sunRef.sunDirection, viewCamera.transform.forward);
            double halfFov = viewCamera.fieldOfView * 0.5;
            float awayFromSun = sunAngle < halfFov ? 0f : Mathf.Clamp01((float)((sunAngle + GlareMarginDegrees - halfFov) * GlareRate));
            return Mathf.Lerp(cube.glareFadeLimit, 0f, awayFromSun);
        }

        private static bool SunInSight(Vector3d worldPosition)
        {
            var sun = ScaledSun.Instance;
            if (sun == null)
                return false;

            Vector3 origin = ScaledSpace.LocalToScaledSpace(worldPosition);
            Vector3 toSun = sun.transform.position - origin;
            int scaledScenery = 1 << LayerMask.NameToLayer("Scaled Scenery");
            return Physics.Raycast(new Ray(origin, toSun), out var hit, toSun.magnitude, scaledScenery)
                && hit.collider.gameObject == sun.gameObject;
        }

        private static double AngleDegrees(Vector3d from, Vector3d to)
        {
            double angle = Math.Acos(Vector3d.Dot(from, to)) * (180.0 / Math.PI);
            return double.IsNaN(angle) ? 0.0 : angle;
        }
    }
}
