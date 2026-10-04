using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class OneShotHook
    {
        private const int MaxPending = 64;
        private const string HarmonyId = "JustReadTheInstructions.CameraAudio";

        internal readonly struct Fired
        {
            public readonly AudioSource Source;
            public readonly AudioClip Clip;
            public readonly float VolumeScale;
            public readonly int Frame;

            public Fired(AudioSource source, AudioClip clip, float volumeScale, int frame)
            {
                Source = source;
                Clip = clip;
                VolumeScale = volumeScale;
                Frame = frame;
            }
        }

        private static readonly List<Fired> Pending = new List<Fired>();
        private static bool _attempted;

        public static bool Listening { get; set; }

        public static void TryInstall()
        {
            if (_attempted) return;
            _attempted = true;

            try
            {
                var harmonyType = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("HarmonyLib.Harmony", false))
                    .FirstOrDefault(t => t != null);
                if (harmonyType == null)
                {
                    Debug.Log("[JRTI-Audio]: Harmony not installed - sounds started with PlayOneShot (Rocket Sound Enhancement ignition, flameout, decouplers) are not heard on cameras");
                    return;
                }

                var harmony = Activator.CreateInstance(harmonyType, HarmonyId);
                var postfix = Activator.CreateInstance(harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod"),
                    typeof(OneShotHook).GetMethod(nameof(AfterPlayOneShot), BindingFlags.Static | BindingFlags.NonPublic));
                var patch = harmonyType.GetMethods().First(m => m.Name == "Patch" && m.GetParameters().Length > 2
                                                                && m.GetParameters()[0].ParameterType == typeof(MethodBase));
                int postfixSlot = Array.FindIndex(patch.GetParameters(), p => p.Name == "postfix");

                foreach (var original in typeof(AudioSource).GetMethods().Where(m => m.Name == nameof(AudioSource.PlayOneShot)))
                {
                    var arguments = new object[patch.GetParameters().Length];
                    arguments[0] = original;
                    arguments[postfixSlot] = postfix;
                    patch.Invoke(harmony, arguments);
                }
                Debug.Log("[JRTI-Audio]: Listening for PlayOneShot sounds");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Could not listen for PlayOneShot sounds: {(ex.InnerException ?? ex).Message}");
            }
        }

        public static void TakePending(List<Fired> into)
        {
            into.AddRange(Pending);
            Pending.Clear();
        }

        private static void AfterPlayOneShot(AudioSource __instance, object[] __args)
        {
            if (!Listening || __args.Length == 0 || !(__args[0] is AudioClip clip) || clip == null) return;

            int frame = Time.frameCount;
            foreach (var fired in Pending)
                if (fired.Frame == frame && ReferenceEquals(fired.Source, __instance) && ReferenceEquals(fired.Clip, clip)) return;

            if (Pending.Count >= MaxPending) Pending.RemoveAt(0);
            Pending.Add(new Fired(__instance, clip, __args.Length > 1 && __args[1] is float scale ? scale : 1f, frame));
        }
    }
}
