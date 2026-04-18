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
            if (!WbReadSize(d, ref i, out long ebmlBodySize)) return;
            i += (int)Math.Min(ebmlBodySize == WbUnknownSize ? 0 : ebmlBodySize, Math.Max(0, d.Length - i));

            if (!WbReadId(d, ref i, out uint segId) || segId != 0x18538067) return;

            int segSizeStart = i;
            if (!WbReadSize(d, ref i, out long segBodySize)) return;

            int segDataStart = i;
            int segDataEnd = (segBodySize == WbUnknownSize || (long)segDataStart + segBodySize > d.Length)
                ? d.Length
                : segDataStart + (int)segBodySize;

            int segInfoStart = -1, segInfoEnd = -1;
            int existingDurationStart = -1, existingDurationEnd = -1;
            int seekHeadStart = -1, seekHeadEnd = -1;
            int cuesStart = -1, cuesEnd = -1;

            bool hasValidDuration = false;
            bool hasCues = false;
            var clusters = new List<(int absStart, long time, int absEnd)>();

            int j = segDataStart;
            while (j < segDataEnd && j < d.Length)
            {
                int elStart = j;
                if (!WbReadId(d, ref j, out uint elId)) break;
                if (!WbReadSize(d, ref j, out long elSize)) break;
                int dataOff = j;

                int elEnd;
                if (elSize == WbUnknownSize || (long)dataOff + elSize > segDataEnd)
                    elEnd = segDataEnd;
                else
                    elEnd = dataOff + (int)elSize;

                if (elId == 0x1549A966)
                {
                    segInfoStart = elStart;
                    segInfoEnd = elEnd;
                    int k = dataOff;
                    while (k < elEnd && k < d.Length)
                    {
                        int childStart = k;

                        if (!WbReadId(d, ref k, out uint subId)) break;
                        if (!WbReadSize(d, ref k, out long subSize)) break;
                        int subEnd = (subSize == WbUnknownSize || k + subSize > elEnd) ? elEnd : k + (int)subSize;

                        if (subId == 0x4489)
                        {
                            existingDurationStart = childStart;
                            existingDurationEnd = subEnd;

                            double val = WbReadFloat(d, k, (int)subSize);
                            if (val > 0) hasValidDuration = true;
                        }

                        k = subEnd;
                    }
                }
                else if (elId == 0x114D9B74)
                {
                    seekHeadStart = elStart;
                    seekHeadEnd = elEnd;
                }
                else if (elId == 0x1C53BB6B)
                {
                    hasCues = true;
                    cuesStart = elStart;
                    cuesEnd = elEnd;
                }
                else if (elId == 0x1F43B675)
                {
                    long clusterTime = 0;
                    int k = dataOff;
                    while (k < elEnd && k < d.Length)
                    {
                        if (!WbReadId(d, ref k, out uint subId)) break;
                        if (!WbReadSize(d, ref k, out long subSize)) break;
                        int subEnd = (subSize == WbUnknownSize || k + subSize > elEnd) ? elEnd : k + (int)subSize;
                        if (subId == 0xE7 && subSize <= 8 && subSize > 0)
                        {
                            long t = 0;
                            for (int x = 0; x < (int)subSize; x++) t = t * 256 + d[k + x];
                            clusterTime = t;
                            break;
                        }
                        k = subEnd;
                    }
                    clusters.Add((elStart, clusterTime, elEnd));
                }

                j = elEnd;
            }

            if (segInfoStart < 0 || clusters.Count == 0) return;
            if (hasValidDuration && hasCues) return;

            int firstClusterAbsPos = clusters[0].absStart;

            long lastTime = clusters[clusters.Count - 1].time;
            int lastClusterAbsStart = clusters[clusters.Count - 1].absStart;
            int lastClusterAbsEnd = clusters[clusters.Count - 1].absEnd;

            int ci = lastClusterAbsStart;

            if (WbReadId(d, ref ci, out _) && WbReadSize(d, ref ci, out _))
            {
                while (ci < lastClusterAbsEnd && ci < d.Length)
                {
                    if (!WbReadId(d, ref ci, out uint iR2)) break;
                    if (!WbReadSize(d, ref ci, out long sR2)) break;
                    long blockEndL = (long)ci + sR2;
                    int blockEnd = blockEndL > lastClusterAbsEnd ? lastClusterAbsEnd : (int)blockEndL;

                    if (iR2 == 0xA3 || iR2 == 0xA1)
                    {
                        int tempCi = ci;
                        if (WbReadVint(d, ref tempCi, out _))
                        {
                            if (tempCi + 1 < blockEnd && tempCi + 1 < d.Length)
                            {
                                int relTime = (d[tempCi] << 8) | d[tempCi + 1];
                                short signedTime = (short)relTime;
                                long absTime = clusters[clusters.Count - 1].time + signedTime;
                                if (absTime > lastTime) lastTime = absTime;
                            }
                        }
                    }
                    ci = blockEnd;
                }
            }

            long durationTicks = lastTime + 33;

            byte[] durationEl = WbBuildFloat64(0x4489, durationTicks);

            int infoIdLen = WbIdLen(d, segInfoStart);
            int infoSzLen = WbSizeLen(d, segInfoStart + infoIdLen);
            int infoDataStart = segInfoStart + infoIdLen + infoSzLen;

            byte[] oldInfoPayload;
            if (existingDurationStart != -1)
            {
                oldInfoPayload = Concat(
                    Slice(d, infoDataStart, existingDurationStart),
                    Slice(d, existingDurationEnd, segInfoEnd)
                );
            }
            else
            {
                oldInfoPayload = Slice(d, infoDataStart, segInfoEnd);
            }

            byte[] newInfoEl = WbBuildContainerRaw(0x1549A966, Concat(oldInfoPayload, durationEl));

            int seekHeadSize = seekHeadStart != -1 ? (seekHeadEnd - seekHeadStart) : 0;
            int infoSizeDelta = newInfoEl.Length - (segInfoEnd - segInfoStart);
            int cuesBeforeClustersSize = (hasCues && cuesStart < firstClusterAbsPos) ? (cuesEnd - cuesStart) : 0;

            long clusterShift = infoSizeDelta - seekHeadSize - cuesBeforeClustersSize;
            byte[] cuesEl = WbBuildCues(clusters, segDataStart, clusterShift);

            var parts = new List<byte[]>();

            if (seekHeadStart != -1)
            {
                parts.Add(Slice(d, segDataStart, seekHeadStart));
                parts.Add(Slice(d, seekHeadEnd, segInfoStart));
            }
            else
            {
                parts.Add(Slice(d, segDataStart, segInfoStart));
            }

            parts.Add(newInfoEl);

            if (hasCues && cuesStart < firstClusterAbsPos)
            {
                parts.Add(Slice(d, segInfoEnd, cuesStart));
                parts.Add(Slice(d, cuesEnd, firstClusterAbsPos));
            }
            else
            {
                parts.Add(Slice(d, segInfoEnd, firstClusterAbsPos));
            }

            if (hasCues && cuesStart >= firstClusterAbsPos)
            {
                parts.Add(Slice(d, firstClusterAbsPos, cuesStart));
                parts.Add(Slice(d, cuesEnd, d.Length));
            }
            else
            {
                parts.Add(Slice(d, firstClusterAbsPos, d.Length));
            }

            parts.Add(cuesEl);

            long totalBodySize = 0;
            foreach (var p in parts) totalBodySize += p.Length;

            byte[] newSegSizeBytes = WbEncodeVint(totalBodySize, 8);

            parts.Insert(0, newSegSizeBytes);
            parts.Insert(0, Slice(d, 0, segSizeStart));

            byte[] final = Concat(parts.ToArray());

            try { File.WriteAllBytes(path, final); }
            catch { }
        }

        private static readonly long WbUnknownSize = unchecked((long)0x00FFFFFFFFFFFFFFL);

        private static byte[] WbBuildCues(List<(int absStart, long time, int absEnd)> clusters, int segDataStart, long shift = 0)
        {
            var points = new List<byte[]>();

            foreach (var (absStart, time, _) in clusters)
            {
                long clusterPos = Math.Max(0, absStart - segDataStart + shift);
                byte[] trackPos = WbBuildContainerRaw(0xB7, Concat(
                    WbBuildUintFixed(0xF7, 1, 1),
                    WbBuildUintFixed(0xF1, clusterPos, 8)
                ));
                byte[] cuePoint = WbBuildContainerRaw(0xBB, Concat(
                    WbBuildUint(0xB3, time),
                    trackPos
                ));
                points.Add(cuePoint);
            }
            return WbBuildContainerRaw(0x1C53BB6B, Concat(points.ToArray()));
        }

        private static byte[] WbBuildFloat64(uint id, double val)
        {
            byte[] idBytes = WbEncodeId(id);
            byte[] payload = new byte[8];
            long bits = BitConverter.DoubleToInt64Bits(val);
            for (int i = 7; i >= 0; i--) { payload[i] = (byte)(bits & 0xFF); bits >>= 8; }
            byte[] sizeBytes = WbEncodeVint(8, 1);
            return Concat(idBytes, sizeBytes, payload);
        }

        private static byte[] WbBuildUint(uint id, long val)
        {
            if (val < 0) val = 0;
            int byteLen = 1;
            long tmp = val;
            while (tmp > 0xFF) { byteLen++; tmp >>= 8; }
            byte[] payload = new byte[byteLen];
            long v = val;
            for (int i = byteLen - 1; i >= 0; i--) { payload[i] = (byte)(v & 0xFF); v >>= 8; }
            return Concat(WbEncodeId(id), WbEncodeVint(byteLen, 1), payload);
        }

        private static byte[] WbBuildUintFixed(uint id, long val, int byteLen)
        {
            if (val < 0) val = 0;
            byte[] payload = new byte[byteLen];
            long v = val;
            for (int i = byteLen - 1; i >= 0; i--) { payload[i] = (byte)(v & 0xFF); v >>= 8; }
            return Concat(WbEncodeId(id), WbEncodeVint(byteLen, 1), payload);
        }

        private static byte[] WbBuildContainerRaw(uint id, byte[] payload)
        {
            return Concat(WbEncodeId(id), WbEncodeVint(payload.Length, WbVintWidth(payload.Length)), payload);
        }

        private static byte[] WbEncodeId(uint id)
        {
            if (id <= 0xFF) return new byte[] { (byte)id };
            if (id <= 0xFFFF) return new byte[] { (byte)(id >> 8), (byte)id };
            if (id <= 0xFFFFFF) return new byte[] { (byte)(id >> 16), (byte)(id >> 8), (byte)id };
            return new byte[] { (byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id };
        }

        private static byte[] WbEncodeVint(long val, int width)
        {
            var b = new byte[width];
            int marker = 0x80 >> (width - 1);
            long v = val;
            for (int i = width - 1; i > 0; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
            b[0] = (byte)(((byte)(v & 0x7F) & (byte)(marker - 1)) | (byte)marker);
            return b;
        }

        private static int WbVintWidth(long val)
        {
            if (val < 0x7F) return 1;
            if (val < 0x3FFF) return 2;
            if (val < 0x1FFFFF) return 3;
            if (val < 0x0FFFFFFF) return 4;
            return 8;
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

        private static int WbIdLen(byte[] d, int offset)
        {
            if (offset >= d.Length) return 1;
            byte b = d[offset];
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 4) { width++; mask >>= 1; }
            return width;
        }

        private static int WbSizeLen(byte[] d, int offset)
        {
            if (offset >= d.Length) return 1;
            byte b = d[offset];
            int width = 1;
            byte mask = 0x80;
            while ((b & mask) == 0 && width <= 8) { width++; mask >>= 1; }
            return width;
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
            if (width > 8 || i + width > d.Length) return false;
            val = b & (mask - 1);
            for (int x = 1; x < width; x++) val = (val << 8) | d[i + x];
            i += width;
            return true;
        }

        private static double WbReadFloat(byte[] d, int i, int size)
        {
            if (size != 4 && size != 8) return 0;
            byte[] temp = new byte[size];
            Buffer.BlockCopy(d, i, temp, 0, size);

            if (BitConverter.IsLittleEndian) Array.Reverse(temp);

            if (size == 4) return BitConverter.ToSingle(temp, 0);
            return BitConverter.ToDouble(temp, 0);
        }

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