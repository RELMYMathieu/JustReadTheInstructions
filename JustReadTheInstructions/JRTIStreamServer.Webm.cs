using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static void FixWebm(string path)
        {
            try { FixWebmInternal(path); }
            catch (Exception ex) { Debug.LogError($"[JRTI-Stream]: FixWebm crash:\n{ex}"); }
        }

        private static void FixWebmInternal(string path)
        {
            byte[] d;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Length > 2L * 1024 * 1024 * 1024) return;
                d = File.ReadAllBytes(path);
            }
            catch { return; }

            int i = 0;
            if (!WbReadId(d, ref i, out uint ebmlId) || ebmlId != 0x1A45DFA3) return;
            if (!WbReadSize(d, ref i, out long ebmlSz)) return;
            i += (int)Math.Min(ebmlSz == WbUnknown ? 0 : ebmlSz, Math.Max(0, d.Length - i));

            if (!WbReadId(d, ref i, out uint segId) || segId != 0x18538067) return;

            int segSizeStart = i;
            if (!WbReadSize(d, ref i, out long segBodySz)) return;
            int segSizeLen = i - segSizeStart;
            int segDataStart = i;

            bool isUnknownSize = false;
            if (segSizeLen == 8)
            {
                bool allFF = true;
                for (int k = 1; k < 8; k++)
                    if (d[segSizeStart + k] != 0xFF) { allFF = false; break; }
                isUnknownSize = allFF;
            }

            int segDataEnd = (isUnknownSize || (long)segDataStart + segBodySz > d.Length)
                ? d.Length
                : segDataStart + (int)segBodySz;

            var scan = ScanSegment(d, segDataStart, segDataEnd);
            if (scan.Clusters.Count == 0 || scan.SegInfoStart < 0) return;

            long lastTime = scan.Clusters[scan.Clusters.Count - 1].Time;
            int ci = scan.Clusters[scan.Clusters.Count - 1].AbsStart;
            int lastClusterEnd = scan.Clusters[scan.Clusters.Count - 1].AbsEnd;

            if (WbReadId(d, ref ci, out _) && WbReadSize(d, ref ci, out _))
            {
                while (ci < lastClusterEnd && ci < d.Length)
                {
                    if (!WbReadId(d, ref ci, out uint bId)) break;
                    if (!WbReadSize(d, ref ci, out long bSz)) break;
                    long bEndL = (long)ci + bSz;
                    int bEnd = bEndL > lastClusterEnd ? lastClusterEnd : (int)bEndL;

                    if (bId == 0xA3 || bId == 0xA1)
                    {
                        int tmp = ci;
                        if (WbReadVint(d, ref tmp, out _) && tmp + 1 < bEnd && tmp + 1 < d.Length)
                        {
                            int rel = (d[tmp] << 8) | d[tmp + 1];
                            long abs = scan.Clusters[scan.Clusters.Count - 1].Time + (short)rel;
                            if (abs > lastTime) lastTime = abs;
                        }
                    }
                    ci = bEnd;
                }
            }

            long durationTick = lastTime + 33;
            byte[] newInfoEl = PatchSegInfo(d, scan.SegInfoStart, scan.SegInfoEnd, durationTick);

            bool hasCues = scan.CuesStart >= 0;
            int clusterAbsStart = scan.Clusters[0].AbsStart;
            int seekHeadSize = scan.SeekHeadStart >= 0 ? scan.SeekHeadEnd - scan.SeekHeadStart : 0;
            int infoSizeDiff = newInfoEl != null ? newInfoEl.Length - (scan.SegInfoEnd - scan.SegInfoStart) : 0;
            int cuesBeforeClusterSize = hasCues && scan.CuesStart < clusterAbsStart ? scan.CuesEnd - scan.CuesStart : 0;

            long defaultDurationNs = scan.FrameCount > 1
                ? (long)Math.Round((double)durationTick * scan.TimecodeScale / scan.FrameCount)
                : 0;

            byte[] newTracksEl = scan.TracksStart >= 0 && defaultDurationNs > 0
                ? PatchTracksDefaultDuration(d, scan.TracksStart, scan.TracksEnd, defaultDurationNs)
                : null;

            int tracksSizeDiff = newTracksEl != null ? newTracksEl.Length - (scan.TracksEnd - scan.TracksStart) : 0;
            long clusterShift = infoSizeDiff + tracksSizeDiff - seekHeadSize - cuesBeforeClusterSize;

            byte[] cuesEl = BuildCues(scan.Clusters, segDataStart, clusterShift);

            var parts = new List<byte[]>();

            if (scan.SeekHeadStart >= 0)
            {
                parts.Add(Slice(d, segDataStart, scan.SeekHeadStart));
                parts.Add(Slice(d, scan.SeekHeadEnd, scan.SegInfoStart));
            }
            else
            {
                parts.Add(Slice(d, segDataStart, scan.SegInfoStart));
            }

            parts.Add(newInfoEl ?? Slice(d, scan.SegInfoStart, scan.SegInfoEnd));

            if (hasCues && scan.CuesStart < clusterAbsStart)
            {
                parts.Add(Slice(d, scan.SegInfoEnd, scan.CuesStart));
                parts.Add(Slice(d, scan.CuesEnd, clusterAbsStart));
            }
            else if (newTracksEl != null && scan.TracksStart >= 0)
            {
                parts.Add(Slice(d, scan.SegInfoEnd, scan.TracksStart));
                parts.Add(newTracksEl);
                parts.Add(Slice(d, scan.TracksEnd, clusterAbsStart));
            }
            else
            {
                parts.Add(Slice(d, scan.SegInfoEnd, clusterAbsStart));
            }

            if (hasCues && scan.CuesStart >= clusterAbsStart)
            {
                parts.Add(Slice(d, clusterAbsStart, scan.CuesStart));
                parts.Add(Slice(d, scan.CuesEnd, d.Length));
            }
            else
            {
                parts.Add(Slice(d, clusterAbsStart, d.Length));
            }

            parts.Add(cuesEl);

            long totalPayload = 0;
            foreach (var p in parts) totalPayload += p.Length;

            byte[] newSegSize = EncodeVintFixed(totalPayload, 8);

            var final = new List<byte[]> { Slice(d, 0, segSizeStart), newSegSize };
            final.AddRange(parts);

            try { File.WriteAllBytes(path, Concat(final.ToArray())); }
            catch { }
        }

        private class WbScan
        {
            public List<WbCluster> Clusters = new List<WbCluster>();
            public int SegInfoStart = -1, SegInfoEnd = -1;
            public int TracksStart = -1, TracksEnd = -1;
            public int CuesStart = -1, CuesEnd = -1;
            public int SeekHeadStart = -1, SeekHeadEnd = -1;
            public int FrameCount;
            public long TimecodeScale = 1_000_000;
        }

        private class WbCluster
        {
            public int AbsStart, AbsEnd;
            public long Time, Pos;
        }

        private static WbScan ScanSegment(byte[] d, int segDataStart, int segDataEnd)
        {
            var r = new WbScan();
            int i = segDataStart;

            while (i < segDataEnd && i < d.Length)
            {
                int eStart = i;
                if (!WbReadId(d, ref i, out uint elId)) break;
                if (!WbReadSize(d, ref i, out long elSz)) break;
                int dataStart = i;
                int eEnd = elSz == WbUnknown || (long)dataStart + elSz > segDataEnd
                    ? segDataEnd
                    : dataStart + (int)elSz;

                if (elId == 0x1549A966)
                {
                    r.SegInfoStart = eStart;
                    r.SegInfoEnd = Math.Min(eEnd, d.Length);
                    int j = dataStart;
                    while (j < r.SegInfoEnd)
                    {
                        if (!WbReadId(d, ref j, out uint cId)) break;
                        if (!WbReadSize(d, ref j, out long cSz)) break;
                        if (cId == 0x2AD7B1)
                        {
                            long tv = 0;
                            for (int k = 0; k < (int)cSz; k++) tv = tv * 256 + d[j + k];
                            r.TimecodeScale = tv > 0 ? tv : 1_000_000;
                        }
                        j += (int)cSz;
                    }
                }
                else if (elId == 0x1654AE6B)
                {
                    r.TracksStart = eStart;
                    r.TracksEnd = Math.Min(eEnd, d.Length);
                }
                else if (elId == 0x1F43B675)
                {
                    long clusterTime = 0;
                    int k = dataStart;
                    while (k < Math.Min(eEnd, d.Length))
                    {
                        if (!WbReadId(d, ref k, out uint bId)) break;
                        if (!WbReadSize(d, ref k, out long bSz)) break;
                        if (bId == 0xA3 || bId == 0xA1) r.FrameCount++;
                        if (bId == 0xE7)
                        {
                            long tv = 0;
                            for (int m = 0; m < (int)bSz; m++) tv = tv * 256 + d[k + m];
                            clusterTime = tv;
                        }
                        k += (int)bSz;
                    }
                    r.Clusters.Add(new WbCluster
                    {
                        AbsStart = eStart,
                        AbsEnd = Math.Min(eEnd, d.Length),
                        Time = clusterTime,
                        Pos = eStart - segDataStart
                    });
                }
                else if (elId == 0x1C53BB6B)
                {
                    r.CuesStart = eStart;
                    r.CuesEnd = Math.Min(eEnd, d.Length);
                }
                else if (elId == 0x114D9B74)
                {
                    r.SeekHeadStart = eStart;
                    r.SeekHeadEnd = Math.Min(eEnd, d.Length);
                }

                i = eEnd;
            }

            return r;
        }

        private static byte[] PatchSegInfo(byte[] d, int segInfoStart, int segInfoEnd, double duration)
        {
            byte[] durationEl = BuildElement(0x4489, EncodeFloat64(duration));

            int j = segInfoStart;
            if (!WbReadId(d, ref j, out _) || !WbReadSize(d, ref j, out _)) return null;
            int dataStart = j;

            int existingDurStart = -1, existingDurEnd = -1;
            int k = dataStart;
            while (k < segInfoEnd)
            {
                int childStart = k;
                if (!WbReadId(d, ref k, out uint cId)) break;
                if (!WbReadSize(d, ref k, out long cSz)) break;
                if (cId == 0x4489)
                {
                    existingDurStart = childStart;
                    existingDurEnd = k + (int)cSz;
                    break;
                }
                k += (int)cSz;
            }

            byte[] oldPayload = existingDurStart >= 0
                ? Concat(Slice(d, dataStart, existingDurStart), Slice(d, existingDurEnd, segInfoEnd))
                : Slice(d, dataStart, segInfoEnd);

            return BuildElement(0x1549A966, Concat(oldPayload, durationEl));
        }

        private static byte[] PatchTracksDefaultDuration(byte[] d, int tracksStart, int tracksEnd, long defaultDurationNs)
        {
            int j = tracksStart;
            if (!WbReadId(d, ref j, out _) || !WbReadSize(d, ref j, out _)) return null;
            int dataStart = j;

            byte[] durEl = BuildElement(0x23E383, WriteUint(defaultDurationNs));
            var parts = new List<byte[]>();
            int i = dataStart;

            while (i < tracksEnd)
            {
                int eStart = i;
                if (!WbReadId(d, ref i, out uint eId)) break;
                if (!WbReadSize(d, ref i, out long eSz)) break;
                int eDataStart = i;
                int eEnd = (long)eDataStart + eSz > tracksEnd ? tracksEnd : eDataStart + (int)eSz;
                int eEndClamped = Math.Min(eEnd, tracksEnd);

                if (eId == 0xAE)
                {
                    bool isVideo = false, hasDur = false;
                    int k = eDataStart;
                    while (k < eEndClamped)
                    {
                        if (!WbReadId(d, ref k, out uint cId)) break;
                        if (!WbReadSize(d, ref k, out long cSz)) break;
                        if (cId == 0x83 && k < d.Length && d[k] == 1) isVideo = true;
                        if (cId == 0x23E383) hasDur = true;
                        k += (int)cSz;
                    }

                    if (isVideo && !hasDur)
                    {
                        parts.Add(BuildElement(0xAE, Concat(Slice(d, eDataStart, eEndClamped), durEl)));
                        i = eEndClamped;
                        continue;
                    }
                }

                parts.Add(Slice(d, eStart, eEndClamped));
                i = eEndClamped;
            }

            return BuildElement(0x1654AE6B, Concat(parts.ToArray()));
        }

        private static byte[] BuildCues(List<WbCluster> clusters, int segDataStart, long shift)
        {
            var points = new List<byte[]>();
            foreach (var c in clusters)
            {
                long pos = Math.Max(0, c.Pos + shift);
                byte[] trackPos = BuildElement(0xB7, Concat(
                    BuildElement(0xF7, WriteUint(1)),
                    BuildElement(0xF1, WriteUint(pos))
                ));
                points.Add(BuildElement(0xBB, Concat(
                    BuildElement(0xB3, WriteUint(c.Time)),
                    trackPos
                )));
            }
            return BuildElement(0x1C53BB6B, Concat(points.ToArray()));
        }

        private static byte[] BuildElement(uint id, byte[] payload)
        {
            byte[] idBytes = EncodeId(id);
            byte[] sizeBytes = EncodeVintFixed(payload.Length, VintWidth(payload.Length));
            var result = new byte[idBytes.Length + sizeBytes.Length + payload.Length];
            Buffer.BlockCopy(idBytes, 0, result, 0, idBytes.Length);
            Buffer.BlockCopy(sizeBytes, 0, result, idBytes.Length, sizeBytes.Length);
            Buffer.BlockCopy(payload, 0, result, idBytes.Length + sizeBytes.Length, payload.Length);
            return result;
        }

        private static byte[] WriteUint(long val)
        {
            if (val <= 0) return new byte[] { 0 };
            int len = 1;
            long tmp = val;
            while (tmp > 255) { len++; tmp >>= 8; }
            var b = new byte[len];
            long v = val;
            for (int i = len - 1; i >= 0; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
            return b;
        }

        private static byte[] EncodeFloat64(double val)
        {
            long bits = BitConverter.DoubleToInt64Bits(val);
            var b = new byte[8];
            for (int i = 7; i >= 0; i--) { b[i] = (byte)(bits & 0xFF); bits >>= 8; }
            return b;
        }

        private static byte[] EncodeVintFixed(long val, int width)
        {
            int marker = 0x80 >> (width - 1);
            var b = new byte[width];
            long v = val;
            for (int i = width - 1; i > 0; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
            b[0] = (byte)((v & (marker - 1)) | marker);
            return b;
        }

        private static int VintWidth(long val)
        {
            if (val < 0x7F) return 1;
            if (val < 0x3FFF) return 2;
            if (val < 0x1FFFFF) return 3;
            if (val < 0x0FFFFFFF) return 4;
            return 8;
        }

        private static byte[] EncodeId(uint id)
        {
            if (id <= 0xFF) return new byte[] { (byte)id };
            if (id <= 0xFFFF) return new byte[] { (byte)(id >> 8), (byte)id };
            if (id <= 0xFFFFFF) return new byte[] { (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            return new byte[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
        }

        private static bool WbReadId(byte[] d, ref int i, out uint id)
        {
            id = 0;
            if (i >= d.Length) return false;
            byte b = d[i];
            if (b == 0) return false;
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 4) { width++; mask >>= 1; }
            if (i + width > d.Length) return false;
            uint val = b;
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            id = val;
            i += width;
            return true;
        }

        private static bool WbReadSize(byte[] d, ref int i, out long size)
        {
            size = 0;
            if (i >= d.Length) return false;
            byte b = d[i];
            if (b == 0) return false;
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 8) { width++; mask >>= 1; }
            if (i + width > d.Length) return false;
            long val = b & (mask - 1);
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            i += width;
            size = val;
            return true;
        }

        private static bool WbReadVint(byte[] d, ref int i, out long val)
        {
            val = 0;
            if (i >= d.Length) return false;
            byte b = d[i];
            if (b == 0) return false;
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 8) { width++; mask >>= 1; }
            if (i + width > d.Length) return false;
            val = b & (mask - 1);
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            i += width;
            return true;
        }

        private static readonly long WbUnknown = unchecked((long)0x00FFFFFFFFFFFFFFL);

        private static byte[] Slice(byte[] d, int start, int end)
        {
            int len = Math.Max(0, Math.Min(end, d.Length) - Math.Max(0, start));
            var result = new byte[len];
            if (len > 0) Buffer.BlockCopy(d, Math.Max(0, start), result, 0, len);
            return result;
        }

        private static byte[] Concat(params byte[][] arrays)
        {
            int total = 0;
            foreach (var a in arrays) total += a.Length;
            var result = new byte[total];
            int off = 0;
            foreach (var a in arrays) { Buffer.BlockCopy(a, 0, result, off, a.Length); off += a.Length; }
            return result;
        }
    }
}