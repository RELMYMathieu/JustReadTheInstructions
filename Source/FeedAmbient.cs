using UnityEngine;
using UnityEngine.Rendering;

namespace JustReadTheInstructions
{
    internal static class FeedAmbient
    {
        private static readonly int LightModelAmbient = Shader.PropertyToID("glstate_lightmodel_ambient");
        private static readonly int DeferredAmbient = Shader.PropertyToID("legacyAmbientColor");
        private static bool _logged;

        internal readonly struct Saved
        {
            public readonly Color Light;
            public readonly SphericalHarmonicsL2 Probe;
            public readonly Vector4 LightModel;
            public readonly Color Deferred;

            public Saved(Color light, SphericalHarmonicsL2 probe, Vector4 lightModel, Color deferred)
            {
                Light = light;
                Probe = probe;
                LightModel = lightModel;
                Deferred = deferred;
            }
        }

        public static Saved Override(Color target)
        {
            var saved = new Saved(RenderSettings.ambientLight, RenderSettings.ambientProbe,
                Shader.GetGlobalVector(LightModelAmbient), Shader.GetGlobalColor(DeferredAmbient));
            var ratio = Ratio(target, saved.Light);

            var probe = saved.Probe;
            for (int channel = 0; channel < 3; channel++)
                for (int coefficient = 0; coefficient < 9; coefficient++)
                    probe[channel, coefficient] *= ratio[channel];

            RenderSettings.ambientLight = target;
            RenderSettings.ambientProbe = probe;
            if (HasLightModel(saved))
                Shader.SetGlobalVector(LightModelAmbient, Vector4.Scale(saved.LightModel, new Vector4(ratio.r, ratio.g, ratio.b, 1f)));
            Shader.SetGlobalColor(DeferredAmbient, target);

            if (!_logged)
            {
                _logged = true;
                Debug.Log($"[JRTI-Ambient]: Feed ambient {saved.Light} -> {target}, mode {RenderSettings.ambientMode}, light model {saved.LightModel}, Deferred {saved.Deferred}");
            }
            return saved;
        }

        public static void Restore(Saved saved)
        {
            RenderSettings.ambientLight = saved.Light;
            RenderSettings.ambientProbe = saved.Probe;
            if (HasLightModel(saved))
                Shader.SetGlobalVector(LightModelAmbient, saved.LightModel);
            Shader.SetGlobalColor(DeferredAmbient, saved.Deferred);
        }

        private static bool HasLightModel(Saved saved) => saved.LightModel.sqrMagnitude > 0f;

        public static Color? KspAmbient()
        {
            var light = DynamicAmbientLight.Instance;
            var body = FlightGlobals.currentMainBody;
            if (light == null || light.disableDynamicAmbient || body == null)
                return null;

            double seaLevel = body.GetPressure(0.0);
            float pressure = seaLevel > 0.0 ? Mathf.Clamp01((float)(FlightGlobals.ship_stP / seaLevel)) : 0f;
            var ambient = Color.Lerp(light.vacuumAmbientColor, body.atmosphericAmbientColor, pressure);
            return light.boostFactor >= 0f
                ? Color.Lerp(ambient, Color.white, light.boostFactor)
                : Color.Lerp(ambient, Color.black, -light.boostFactor);
        }

        private static Color Ratio(Color to, Color from)
            => new Color(Part(to.r, from.r), Part(to.g, from.g), Part(to.b, from.b), 1f);

        private static float Part(float to, float from)
            => from > 0.0001f ? to / from : 1f;
    }
}
