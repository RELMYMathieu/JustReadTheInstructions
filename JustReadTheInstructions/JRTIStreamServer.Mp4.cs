using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static void FixMp4(string path)
        {
            try { FixMp4Internal(path); }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: FixMp4 crash:\n{ex}"); }
        }

        private static void FixMp4Internal(string path)
        {
            byte[] d;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 2L * 1024 * 1024 * 1024) { Debug.LogWarning($"[JRTI-Stream]: FixMp4 skip (>2GB): {path}"); return; }
                d = File.ReadAllBytes(path);
            }
            catch (Exception ex) { Debug.LogWarning($"[JRTI-Stream]: FixMp4 read failed: {ex.Message}"); return; }

            long movieTs = 0, trackTs = 0;
            uint trackId = 1;
            int mvhdOff = -1, tkhdOff = -1, mdhdOff = -1;
            bool mvhdV0 = true, tkhdV0 = true, mdhdV0 = true;
            int moovEnd = -1;
            var frags = new List<(long size, long dur, bool rap)>();
            long totalDur = 0;

            for (int i = 0; i + 8 <= d.Length;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > d.Length) break;
                uint type = Rd32(d, i + 4);

                if (type == 0x6D6F6F76)
                {
                    moovEnd = i + (int)sz;
                    ScanMoov(d, i + 8, moovEnd, ref movieTs, ref trackTs, ref trackId,
                        ref mvhdOff, ref mvhdV0, ref tkhdOff, ref tkhdV0, ref mdhdOff, ref mdhdV0);
                }
                else if (type == 0x6D6F6F66)
                {
                    int moofEnd = i + (int)sz;
                    uint mdatSz = moofEnd + 8 <= d.Length && Rd32(d, moofEnd + 4) == 0x6D646174
                        ? Rd32(d, moofEnd) : 0;
                    long fragDur = ScanMoofDuration(d, i + 8, moofEnd);
                    totalDur += fragDur;
                    frags.Add((sz + mdatSz, fragDur, ScanMoofIsRap(d, i + 8, moofEnd)));
                }

                i += (int)sz;
            }

            if (moovEnd < 0 || movieTs == 0 || trackTs == 0)
            {
                Debug.LogWarning($"[JRTI-Stream]: FixMp4 abort — moovEnd={moovEnd}, movieTs={movieTs}, trackTs={trackTs}, frags={frags.Count}, path={path}");
                return;
            }

            if (totalDur > 0 && mvhdOff >= 0)
            {
                long movieDur = totalDur * movieTs / trackTs;
                WriteDurMvhd(d, mvhdOff, mvhdV0, movieDur);
                if (tkhdOff >= 0) WriteDurTkhd(d, tkhdOff, tkhdV0, movieDur);
                if (mdhdOff >= 0) WriteDurMvhd(d, mdhdOff, mdhdV0, totalDur);
            }

            if (frags.Count == 0)
            {
                Debug.LogWarning($"[JRTI-Stream]: FixMp4 abort — no moof fragments found. moovEnd={moovEnd}, movieTs={movieTs}, trackTs={trackTs}");
                try { File.WriteAllBytes(path, d); } catch { }
                return;
            }

            byte[] sidx = BuildSidx(trackId, trackTs, frags);
            byte[] result = new byte[d.Length + sidx.Length];
            Buffer.BlockCopy(d, 0, result, 0, moovEnd);
            Buffer.BlockCopy(sidx, 0, result, moovEnd, sidx.Length);
            Buffer.BlockCopy(d, moovEnd, result, moovEnd + sidx.Length, d.Length - moovEnd);

            try
            {
                File.WriteAllBytes(path, result);
                Debug.Log($"[JRTI-Stream]: FixMp4 OK — {frags.Count} frags, SIDX {sidx.Length}B injected at offset {moovEnd}: {path}");
            }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: FixMp4 write failed: {ex.Message}"); }
        }

        private static void ScanMoov(byte[] d, int s, int e,
            ref long mTs, ref long tTs, ref uint trackId,
            ref int mvhdOff, ref bool mvhdV0,
            ref int tkhdOff, ref bool tkhdV0,
            ref int mdhdOff, ref bool mdhdV0)
        {
            for (int i = s; i + 8 <= e;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > e) break;
                uint type = Rd32(d, i + 4);

                if (type == 0x6D766864)
                {
                    mvhdOff = i;
                    mvhdV0 = d[i + 8] == 0;
                    mTs = Rd32(d, i + 12 + (mvhdV0 ? 8 : 16));
                }
                else if (type == 0x7472616B)
                    ScanTrak(d, i + 8, i + (int)sz, ref tTs, ref trackId,
                        ref tkhdOff, ref tkhdV0, ref mdhdOff, ref mdhdV0);

                i += (int)sz;
            }
        }

        private static void ScanTrak(byte[] d, int s, int e,
            ref long tTs, ref uint trackId,
            ref int tkhdOff, ref bool tkhdV0,
            ref int mdhdOff, ref bool mdhdV0)
        {
            for (int i = s; i + 8 <= e;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > e) break;
                uint type = Rd32(d, i + 4);

                if (type == 0x746B6864)
                {
                    tkhdOff = i;
                    tkhdV0 = d[i + 8] == 0;
                    int idOff = i + (tkhdV0 ? 20 : 28);
                    if (idOff + 4 <= e) trackId = Rd32(d, idOff);
                }
                else if (type == 0x6D646961)
                    ScanMdia(d, i + 8, i + (int)sz, ref tTs, ref mdhdOff, ref mdhdV0);

                i += (int)sz;
            }
        }

        private static void ScanMdia(byte[] d, int s, int e,
            ref long tTs, ref int mdhdOff, ref bool mdhdV0)
        {
            for (int i = s; i + 8 <= e;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > e) break;

                if (Rd32(d, i + 4) == 0x6D646864)
                {
                    mdhdOff = i;
                    mdhdV0 = d[i + 8] == 0;
                    tTs = Rd32(d, i + 12 + (mdhdV0 ? 8 : 16));
                }

                i += (int)sz;
            }
        }

        private static long ScanMoofDuration(byte[] d, int s, int e)
        {
            long total = 0;
            for (int i = s; i + 8 <= e;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > e) break;

                if (Rd32(d, i + 4) == 0x74726166)
                {
                    uint defDur = 0;
                    int trafEnd = i + (int)sz;
                    for (int j = i + 8; j + 8 <= trafEnd;)
                    {
                        uint sz2 = Rd32(d, j);
                        if (sz2 < 8 || j + (int)sz2 > trafEnd) break;
                        uint t2 = Rd32(d, j + 4);

                        if (t2 == 0x74666864)
                        {
                            uint fl = ((uint)d[j + 9] << 16) | ((uint)d[j + 10] << 8) | d[j + 11];
                            int off = j + 16;
                            if ((fl & 0x000001u) != 0) off += 8;
                            if ((fl & 0x000002u) != 0) off += 4;
                            if ((fl & 0x000008u) != 0) defDur = Rd32(d, off);
                        }
                        else if (t2 == 0x7472756E)
                        {
                            uint fl = ((uint)d[j + 9] << 16) | ((uint)d[j + 10] << 8) | d[j + 11];
                            uint count = Rd32(d, j + 12);
                            int off = j + 16;
                            if ((fl & 0x000001u) != 0) off += 4;
                            if ((fl & 0x000004u) != 0) off += 4;
                            bool hasDur = (fl & 0x000100u) != 0;
                            int stride = (hasDur ? 4 : 0)
                                + ((fl & 0x000200u) != 0 ? 4 : 0)
                                + ((fl & 0x000400u) != 0 ? 4 : 0)
                                + ((fl & 0x000800u) != 0 ? 4 : 0);
                            if (stride == 0) { j += (int)sz2; continue; }
                            for (uint k = 0; k < count && off + stride <= j + (int)sz2; k++, off += stride)
                                total += hasDur ? Rd32(d, off) : defDur;
                        }

                        j += (int)sz2;
                    }
                }

                i += (int)sz;
            }
            return total;
        }

        private static bool ScanMoofIsRap(byte[] d, int s, int e)
        {
            for (int i = s; i + 8 <= e;)
            {
                uint sz = Rd32(d, i);
                if (sz < 8 || i + (int)sz > e) break;

                if (Rd32(d, i + 4) == 0x74726166)
                {
                    uint defFlags = 0;
                    int trafEnd = i + (int)sz;
                    for (int j = i + 8; j + 8 <= trafEnd;)
                    {
                        uint sz2 = Rd32(d, j);
                        if (sz2 < 8 || j + (int)sz2 > trafEnd) break;
                        uint t2 = Rd32(d, j + 4);

                        if (t2 == 0x74666864)
                        {
                            uint fl = ((uint)d[j + 9] << 16) | ((uint)d[j + 10] << 8) | d[j + 11];
                            int off = j + 16;
                            if ((fl & 0x000001u) != 0) off += 8;
                            if ((fl & 0x000002u) != 0) off += 4;
                            if ((fl & 0x000008u) != 0) off += 4;
                            if ((fl & 0x000010u) != 0) off += 4;
                            if ((fl & 0x000020u) != 0 && off + 4 <= j + (int)sz2) defFlags = Rd32(d, off);
                        }
                        else if (t2 == 0x7472756E)
                        {
                            uint fl = ((uint)d[j + 9] << 16) | ((uint)d[j + 10] << 8) | d[j + 11];
                            int off = j + 16;
                            if ((fl & 0x000001u) != 0) off += 4;

                            uint firstFlags;
                            if ((fl & 0x000004u) != 0 && off + 4 <= j + (int)sz2)
                                firstFlags = Rd32(d, off);
                            else if ((fl & 0x000400u) != 0)
                            {
                                if ((fl & 0x000100u) != 0) off += 4;
                                if ((fl & 0x000200u) != 0) off += 4;
                                firstFlags = off + 4 <= j + (int)sz2 ? Rd32(d, off) : defFlags;
                            }
                            else
                                firstFlags = defFlags;

                            return (firstFlags & 0x00010000u) == 0;
                        }

                        j += (int)sz2;
                    }
                }

                i += (int)sz;
            }
            return false;
        }

        private static byte[] BuildSidx(uint trackId, long timescale,
            List<(long size, long dur, bool rap)> frags)
        {
            int total = 32 + frags.Count * 12;
            var b = new byte[total];
            int o = 0;

            Wr32(b, o, (uint)total); o += 4;
            Wr32(b, o, 0x73696478); o += 4;
            b[o++] = 0;
            b[o++] = 0; b[o++] = 0; b[o++] = 0;
            Wr32(b, o, trackId); o += 4;
            Wr32(b, o, (uint)timescale); o += 4;
            Wr32(b, o, 0); o += 4;
            Wr32(b, o, 0); o += 4;
            b[o++] = 0; b[o++] = 0;
            Wr16(b, o, (ushort)frags.Count); o += 2;

            foreach (var (size, dur, rap) in frags)
            {
                Wr32(b, o, (uint)(size & 0x7FFFFFFF)); o += 4;
                Wr32(b, o, (uint)dur); o += 4;
                Wr32(b, o, rap ? 0x90000000u : 0u); o += 4;
            }

            return b;
        }

        private static void WriteDurMvhd(byte[] d, int o, bool v0, long dur)
        {
            int off = o + (v0 ? 24 : 32);
            if (v0) Wr32(d, off, (uint)Math.Min(dur, uint.MaxValue));
            else Wr64(d, off, (ulong)dur);
        }

        private static void WriteDurTkhd(byte[] d, int o, bool v0, long dur)
        {
            int off = o + (v0 ? 28 : 36);
            if (v0) Wr32(d, off, (uint)Math.Min(dur, uint.MaxValue));
            else Wr64(d, off, (ulong)dur);
        }

        private static uint Rd32(byte[] d, int o)
            => ((uint)d[o] << 24) | ((uint)d[o + 1] << 16) | ((uint)d[o + 2] << 8) | d[o + 3];

        private static void Wr16(byte[] d, int o, ushort v)
        { d[o] = (byte)(v >> 8); d[o + 1] = (byte)v; }

        private static void Wr32(byte[] d, int o, uint v)
        { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }

        private static void Wr64(byte[] d, int o, ulong v)
        { Wr32(d, o, (uint)(v >> 32)); Wr32(d, o + 4, (uint)v); }
    }
}