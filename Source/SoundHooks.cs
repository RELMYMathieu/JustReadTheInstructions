using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace JustReadTheInstructions
{
    internal static class SoundHooks
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
        private static readonly List<AudioSource> Created = new List<AudioSource>();
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
                var patch = harmonyType.GetMethods().First(m => m.Name == "Patch" && m.GetParameters().Length > 2
                                                                && m.GetParameters()[0].ParameterType == typeof(MethodBase));

                void Postfix(MethodBase original, string hook)
                {
                    var postfix = Activator.CreateInstance(harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod"),
                        typeof(SoundHooks).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic));
                    var arguments = new object[patch.GetParameters().Length];
                    arguments[0] = original;
                    arguments[Array.FindIndex(patch.GetParameters(), p => p.Name == "postfix")] = postfix;
                    patch.Invoke(harmony, arguments);
                }

                foreach (var original in typeof(AudioSource).GetMethods().Where(m => m.Name == nameof(AudioSource.PlayOneShot)))
                    Postfix(original, nameof(AfterPlayOneShot));
                Postfix(typeof(AudioFX).GetMethod("CreateSource", BindingFlags.Instance | BindingFlags.NonPublic), nameof(AfterCreateSource));
                Debug.Log("[JRTI-Audio]: Listening for PlayOneShot sounds and new effect sounds");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Audio]: Could not listen for PlayOneShot and effect sounds: {(ex.InnerException ?? ex).Message}");
            }
        }

        public static void TakePending(List<Fired> into)
        {
            into.AddRange(Pending);
            Pending.Clear();
        }

        public static void TakeCreated(List<AudioSource> into)
        {
            into.AddRange(Created);
            Created.Clear();
        }

        private static void AfterCreateSource(AudioSource __result)
        {
            if (Listening && __result != null && Created.Count < MaxPending) Created.Add(__result);
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
