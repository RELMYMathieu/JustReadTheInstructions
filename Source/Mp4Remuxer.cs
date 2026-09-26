using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace JustReadTheInstructions
{
    internal static class Mp4Remuxer
    {
        private const uint NonSyncSampleFlag = 0x00010000;
        private const int CopyBufferSize = 1 << 20;
        private const int MaxMetadataBoxBytes = 64 << 20;

        private static readonly uint Ftyp = Fourcc("ftyp");
        private static readonly uint Moov = Fourcc("moov");
        private static readonly uint Moof = Fourcc("moof");
        private static readonly uint Mvhd = Fourcc("mvhd");
        private static readonly uint Trak = Fourcc("trak");
        private static readonly uint Tkhd = Fourcc("tkhd");
        private static readonly uint Mdia = Fourcc("mdia");
        private static readonly uint Mdhd = Fourcc("mdhd");
        private static readonly uint Minf = Fourcc("minf");
        private static readonly uint Stbl = Fourcc("stbl");
        private static readonly uint Stsd = Fourcc("stsd");
        private static readonly uint Mvex = Fourcc("mvex");
        private static readonly uint Trex = Fourcc("trex");
        private static readonly uint Traf = Fourcc("traf");
        private static readonly uint Tfhd = Fourcc("tfhd");
        private static readonly uint Trun = Fourcc("trun");

        private sealed class Track
        {
            public readonly List<uint> Sizes = new List<uint>();
            public readonly List<uint> Durations = new List<uint>();
            public readonly List<long> CompositionOffsets = new List<long>();
            public readonly List<int> SyncSamples = new List<int>();
            public readonly List<long> ChunkStarts = new List<long>();
            public readonly List<long> ChunkLengths = new List<long>();
            public readonly List<int> ChunkSampleCounts = new List<int>();
            public uint DefaultDuration;
            public uint DefaultSize;
            public uint DefaultFlags;
            public long MovieTimescale;
            public long TrackTimescale;
            public long TrackDuration;
            public long DataBytes;

            public long MovieDuration => TrackTimescale == 0 ? 0 : TrackDuration * MovieTimescale / TrackTimescale;
        }

        public static void MakeProgressive(string path)
        {
            string temp = path + ".part";
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize))
                {
                    var track = new Track();
                    ReadFragmentedFile(input, track, out var ftyp, out var moov);
                    using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize))
                        WriteProgressiveFile(input, output, ftyp, moov, track);
                }
                File.Replace(temp, path, null);
            }
            catch
            {
                try { File.Delete(temp); } catch { }
                throw;
            }
        }

        private static void ReadFragmentedFile(Stream input, Track track, out byte[] ftyp, out byte[] moov)
        {
            ftyp = null;
            moov = null;
            long position = 0;
            long length = input.Length;

            while (position + 8 <= length)
            {
                input.Position = position;
                ReadHeader(input, out long size, out uint type);
                if (size == 0) size = length - position;
                if (size < 8 || position + size > length) break;

                if (type == Ftyp) ftyp = ReadBox(input, position, size);
                else if (type == Moov)
                {
                    moov = ReadBox(input, position, size);
                    ReadTrackInfo(moov, track);
                }
                else if (type == Moof) ReadFragment(ReadBox(input, position, size), position, track);

                position += size;
            }

            if (ftyp == null || moov == null || track.Sizes.Count == 0)
                throw new InvalidDataException("Not a fragmented MP4 with video samples");
        }

        private static void ReadTrackInfo(byte[] moov, Track track)
        {
            if (CountChildren(moov, 8, moov.Length, Trak) != 1)
                throw new InvalidDataException("Expected exactly one track");

            int mvhd = FindChild(moov, 8, moov.Length, Mvhd);
            int trak = FindChild(moov, 8, moov.Length, Trak);
            int mdia = FindChild(moov, trak + 8, End(moov, trak), Mdia);
            int mdhd = FindChild(moov, mdia + 8, End(moov, mdia), Mdhd);
            track.MovieTimescale = ReadTimescale(moov, mvhd);
            track.TrackTimescale = ReadTimescale(moov, mdhd);

            int mvex = FindChild(moov, 8, moov.Length, Mvex);
            if (mvex < 0) throw new InvalidDataException("Missing mvex: the file is not fragmented");
            int trex = FindChild(moov, mvex + 8, End(moov, mvex), Trex);
            if (trex < 0) return;
            track.DefaultDuration = U32(moov, trex + 20);
            track.DefaultSize = U32(moov, trex + 24);
            track.DefaultFlags = U32(moov, trex + 28);
        }

        private static void ReadFragment(byte[] moof, long moofStart, Track track)
        {
            for (int i = 8; i + 8 <= moof.Length; i += (int)U32(moof, i))
            {
                if (U32(moof, i) < 8) break;
                if (U32(moof, i + 4) == Traf) ReadTrackFragment(moof, i + 8, End(moof, i), moofStart, track);
            }
        }

        private static void ReadTrackFragment(byte[] b, int start, int end, long moofStart, Track track)
        {
            uint defaultDuration = track.DefaultDuration;
            uint defaultSize = track.DefaultSize;
            uint defaultFlags = track.DefaultFlags;
            long baseOffset = moofStart;
            long nextData = moofStart;

            for (int i = start; i + 8 <= end; i += (int)U32(b, i))
            {
                if (U32(b, i) < 8) break;
                uint type = U32(b, i + 4);
                uint flags = U32(b, i + 8) & 0xFFFFFF;

                if (type == Tfhd)
                {
                    int p = i + 16;
                    if ((flags & 0x01) != 0) { baseOffset = (long)U64(b, p); p += 8; }
                    if ((flags & 0x02) != 0) p += 4;
                    if ((flags & 0x08) != 0) { defaultDuration = U32(b, p); p += 4; }
                    if ((flags & 0x10) != 0) { defaultSize = U32(b, p); p += 4; }
                    if ((flags & 0x20) != 0) defaultFlags = U32(b, p);
                    nextData = baseOffset;
                }
                else if (type == Trun)
                {
                    bool signedOffsets = b[i + 8] != 0;
                    int count = (int)U32(b, i + 12);
                    int p = i + 16;
                    long dataStart = nextData;
                    if ((flags & 0x001) != 0) { dataStart = baseOffset + (int)U32(b, p); p += 4; }
                    bool hasFirstFlags = (flags & 0x004) != 0;
                    uint firstFlags = hasFirstFlags ? U32(b, p) : defaultFlags;
                    if (hasFirstFlags) p += 4;

                    long chunkLength = 0;
                    for (int k = 0; k < count; k++)
                    {
                        uint duration = defaultDuration, size = defaultSize;
                        uint sampleFlags = k == 0 ? firstFlags : defaultFlags;
                        long compositionOffset = 0;
                        if ((flags & 0x100) != 0) { duration = U32(b, p); p += 4; }
                        if ((flags & 0x200) != 0) { size = U32(b, p); p += 4; }
                        if ((flags & 0x400) != 0) { sampleFlags = U32(b, p); p += 4; }
                        if ((flags & 0x800) != 0)
                        {
                            compositionOffset = signedOffsets ? (long)(int)U32(b, p) : (long)U32(b, p);
                            p += 4;
                        }

                        if ((sampleFlags & NonSyncSampleFlag) == 0) track.SyncSamples.Add(track.Sizes.Count + 1);
                        track.Sizes.Add(size);
                        track.Durations.Add(duration);
                        track.CompositionOffsets.Add(compositionOffset);
                        track.TrackDuration += duration;
                        chunkLength += size;
                    }

                    track.ChunkStarts.Add(dataStart);
                    track.ChunkLengths.Add(chunkLength);
                    track.ChunkSampleCounts.Add(count);
                    track.DataBytes += chunkLength;
                    nextData = dataStart + chunkLength;
                }
            }
        }

        private static void WriteProgressiveFile(Stream input, Stream output, byte[] ftyp, byte[] moov, Track track)
        {
            int mdatHeader = track.DataBytes + 8 > uint.MaxValue ? 16 : 8;
            bool co64 = false;
            long dataStart = ftyp.Length + BuildMoov(moov, track, false, 0).Length + mdatHeader;
            if (dataStart + track.DataBytes > uint.MaxValue)
            {
                co64 = true;
                dataStart = ftyp.Length + BuildMoov(moov, track, true, 0).Length + mdatHeader;
            }

            var newMoov = BuildMoov(moov, track, co64, dataStart);
            output.Write(ftyp, 0, ftyp.Length);
            output.Write(newMoov, 0, newMoov.Length);
            WriteMdatHeader(output, track.DataBytes, mdatHeader);

            var buffer = new byte[CopyBufferSize];
            for (int c = 0; c < track.ChunkStarts.Count; c++)
                CopyRange(input, output, track.ChunkStarts[c], track.ChunkLengths[c], buffer);
        }

        private static byte[] BuildMoov(byte[] moov, Track track, bool co64, long dataStart)
        {
            var writer = new BoxWriter();
            RewriteBoxes(moov, 0, moov.Length, writer, track, co64, dataStart);
            return writer.ToArray();
        }

        private static void RewriteBoxes(byte[] src, int start, int end, BoxWriter w, Track track, bool co64, long dataStart)
        {
            for (int i = start; i + 8 <= end; i += (int)U32(src, i))
            {
                int size = (int)U32(src, i);
                if (size < 8) break;
                uint type = U32(src, i + 4);

                if (type == Mvex) continue;
                if (type == Moov || type == Trak || type == Mdia || type == Minf)
                {
                    int box = w.Begin(type);
                    RewriteBoxes(src, i + 8, i + size, w, track, co64, dataStart);
                    w.End(box);
                }
                else if (type == Stbl)
                {
                    int box = w.Begin(Stbl);
                    int stsd = FindChild(src, i + 8, i + size, Stsd);
                    w.WriteBytes(src, stsd, (int)U32(src, stsd));
                    WriteSampleTables(w, track, co64, dataStart);
                    w.End(box);
                }
                else if (type == Mvhd || type == Mdhd)
                    w.WriteBoxWithDuration(src, i, size, src[i + 8] == 0 ? 24 : 32, type == Mvhd ? track.MovieDuration : track.TrackDuration);
                else if (type == Tkhd)
                    w.WriteBoxWithDuration(src, i, size, src[i + 8] == 0 ? 28 : 36, track.MovieDuration);
                else
                    w.WriteBytes(src, i, size);
            }
        }

        private static void WriteSampleTables(BoxWriter w, Track track, bool co64, long dataStart)
        {
            int count = track.Sizes.Count;

            int stts = w.BeginFull("stts", 0);
            int sttsEntries = w.Placeholder();
            uint sttsCount = 0;
            for (int i = 0; i < count;)
            {
                int run = RunLength(track.Durations, i);
                w.WriteU32((uint)run);
                w.WriteU32(track.Durations[i]);
                sttsCount++;
                i += run;
            }
            w.Patch(sttsEntries, sttsCount);
            w.End(stts);

            if (track.CompositionOffsets.Exists(o => o != 0))
            {
                bool negative = track.CompositionOffsets.Exists(o => o < 0);
                int ctts = w.BeginFull("ctts", negative ? (byte)1 : (byte)0);
                int cttsEntries = w.Placeholder();
                uint cttsCount = 0;
                for (int i = 0; i < count;)
                {
                    int run = RunLength(track.CompositionOffsets, i);
                    w.WriteU32((uint)run);
                    w.WriteU32(unchecked((uint)track.CompositionOffsets[i]));
                    cttsCount++;
                    i += run;
                }
                w.Patch(cttsEntries, cttsCount);
                w.End(ctts);
            }

            if (track.SyncSamples.Count < count)
            {
                int stss = w.BeginFull("stss", 0);
                w.WriteU32((uint)track.SyncSamples.Count);
                foreach (int sample in track.SyncSamples) w.WriteU32((uint)sample);
                w.End(stss);
            }

            int stsc = w.BeginFull("stsc", 0);
            int stscEntries = w.Placeholder();
            uint stscCount = 0;
            for (int c = 0; c < track.ChunkSampleCounts.Count; c++)
            {
                if (c > 0 && track.ChunkSampleCounts[c] == track.ChunkSampleCounts[c - 1]) continue;
                w.WriteU32((uint)(c + 1));
                w.WriteU32((uint)track.ChunkSampleCounts[c]);
                w.WriteU32(1);
                stscCount++;
            }
            w.Patch(stscEntries, stscCount);
            w.End(stsc);

            int stsz = w.BeginFull("stsz", 0);
            w.WriteU32(0);
            w.WriteU32((uint)count);
            foreach (uint size in track.Sizes) w.WriteU32(size);
            w.End(stsz);

            int stco = w.BeginFull(co64 ? "co64" : "stco", 0);
            w.WriteU32((uint)track.ChunkLengths.Count);
            long offset = dataStart;
            foreach (long chunkLength in track.ChunkLengths)
            {
                if (co64) w.WriteU64((ulong)offset);
                else w.WriteU32((uint)offset);
                offset += chunkLength;
            }
            w.End(stco);
        }

        private static int RunLength<T>(List<T> values, int start) where T : IEquatable<T>
        {
            int end = start + 1;
            while (end < values.Count && values[end].Equals(values[start])) end++;
            return end - start;
        }

        private static void WriteMdatHeader(Stream output, long dataBytes, int headerSize)
        {
            var header = new byte[headerSize];
            if (headerSize == 16)
            {
                WriteU32(header, 0, 1);
                WriteU32(header, 4, Fourcc("mdat"));
                WriteU64(header, 8, (ulong)(dataBytes + 16));
            }
            else
            {
                WriteU32(header, 0, (uint)(dataBytes + 8));
                WriteU32(header, 4, Fourcc("mdat"));
            }
            output.Write(header, 0, header.Length);
        }

        private static void CopyRange(Stream input, Stream output, long start, long length, byte[] buffer)
        {
            input.Position = start;
            while (length > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, length));
                if (read <= 0) throw new EndOfStreamException("Sample data ends early");
                output.Write(buffer, 0, read);
                length -= read;
            }
        }

        private static void ReadHeader(Stream input, out long size, out uint type)
        {
            var header = new byte[16];
            ReadExactly(input, header, 8);
            size = U32(header, 0);
            type = U32(header, 4);
            if (size != 1) return;
            ReadExactly(input, header, 8);
            size = (long)U64(header, 0);
        }

        private static byte[] ReadBox(Stream input, long position, long size)
        {
            if (size > MaxMetadataBoxBytes) throw new InvalidDataException("Metadata box too large");
            var box = new byte[size];
            input.Position = position;
            ReadExactly(input, box, box.Length);
            return box;
        }

        private static void ReadExactly(Stream input, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        private static long ReadTimescale(byte[] b, int box) => U32(b, box + (b[box + 8] == 0 ? 20 : 28));

        private static int End(byte[] b, int box) => box + (int)U32(b, box);

        private static int FindChild(byte[] b, int start, int end, uint type)
        {
            for (int i = start; i + 8 <= end; i += (int)U32(b, i))
            {
                if (U32(b, i) < 8) break;
                if (U32(b, i + 4) == type) return i;
            }
            return -1;
        }

        private static int CountChildren(byte[] b, int start, int end, uint type)
        {
            int count = 0;
            for (int i = start; i + 8 <= end; i += (int)U32(b, i))
            {
                if (U32(b, i) < 8) break;
                if (U32(b, i + 4) == type) count++;
            }
            return count;
        }

        private static uint Fourcc(string name) => U32(Encoding.ASCII.GetBytes(name), 0);

        private static uint U32(byte[] b, int o)
            => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

        private static ulong U64(byte[] b, int o) => ((ulong)U32(b, o) << 32) | U32(b, o + 4);

        private static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v >> 24);
            b[o + 1] = (byte)(v >> 16);
            b[o + 2] = (byte)(v >> 8);
            b[o + 3] = (byte)v;
        }

        private static void WriteU64(byte[] b, int o, ulong v)
        {
            WriteU32(b, o, (uint)(v >> 32));
            WriteU32(b, o + 4, (uint)v);
        }

        private sealed class BoxWriter
        {
            private readonly MemoryStream _stream = new MemoryStream();
            private readonly byte[] _scratch = new byte[8];

            public int Begin(uint type)
            {
                int start = (int)_stream.Position;
                WriteU32(0);
                WriteU32(type);
                return start;
            }

            public int BeginFull(string type, byte version)
            {
                int start = Begin(Fourcc(type));
                WriteU32((uint)version << 24);
                return start;
            }

            public void End(int start) => Patch(start, (uint)(_stream.Position - start));

            public int Placeholder()
            {
                int position = (int)_stream.Position;
                WriteU32(0);
                return position;
            }

            public void Patch(int position, uint value)
            {
                long current = _stream.Position;
                _stream.Position = position;
                WriteU32(value);
                _stream.Position = current;
            }

            public void WriteU32(uint value)
            {
                Mp4Remuxer.WriteU32(_scratch, 0, value);
                _stream.Write(_scratch, 0, 4);
            }

            public void WriteU64(ulong value)
            {
                Mp4Remuxer.WriteU64(_scratch, 0, value);
                _stream.Write(_scratch, 0, 8);
            }

            public void WriteBytes(byte[] source, int offset, int count) => _stream.Write(source, offset, count);

            public void WriteBoxWithDuration(byte[] source, int box, int size, int durationOffset, long duration)
            {
                var copy = new byte[size];
                Buffer.BlockCopy(source, box, copy, 0, size);
                if (source[box + 8] == 0) Mp4Remuxer.WriteU32(copy, durationOffset, (uint)Math.Min(duration, uint.MaxValue));
                else Mp4Remuxer.WriteU64(copy, durationOffset, (ulong)duration);
                _stream.Write(copy, 0, size);
            }

            public byte[] ToArray() => _stream.ToArray();
        }
    }
}
