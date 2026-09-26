using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace JustReadTheInstructions
{
    internal sealed class PerfSnapshot
    {
        public DateTime Utc;
        public double Seconds;
        public int Frames;
        public PerfStat FrameMs;
        public PerfStat JrtiMs;
        public int GcCollections;
        public double GcFrameMaxMs;
        public double HeapMb;
        public int PoolBusy;
        public int PoolMin;
        public int PoolIoBusy;
        public int StreamClients;
        public int PreviewClients;
        public int Recordings;
        public double RecordingKbps;
        public bool SpreadCaptures;
        public int MaxFps;
        public List<CameraPerfSample> Cameras;

        public double PerSecond(double count) => Seconds > 0.0 ? count / Seconds : 0.0;
    }

    internal sealed class PerfColumn<T>
    {
        public readonly string Key;
        public readonly bool IsText;
        private readonly Func<T, string> _format;

        public PerfColumn(string key, Func<T, string> format, bool isText = false)
        {
            Key = key;
            _format = format;
            IsText = isText;
        }

        public string Format(T row) => _format(row);
    }

    internal static class PerfFormat
    {
        public static readonly PerfColumn<PerfSnapshot>[] GlobalColumns =
        {
            new PerfColumn<PerfSnapshot>("utc", s => s.Utc.ToString("o", CultureInfo.InvariantCulture), isText: true),
            Global("fps", s => s.PerSecond(s.Frames)),
            Global("frame_ms_avg", s => s.FrameMs.Average),
            Global("frame_ms_max", s => s.FrameMs.Max),
            Global("jrti_ms_avg", s => s.JrtiMs.Average),
            Global("jrti_ms_max", s => s.JrtiMs.Max),
            Global("gc_per_s", s => s.PerSecond(s.GcCollections)),
            Global("gc_frame_ms_max", s => s.GcFrameMaxMs),
            Global("heap_mb", s => s.HeapMb),
            Global("pool_busy", s => s.PoolBusy),
            Global("pool_min", s => s.PoolMin),
            Global("pool_io_busy", s => s.PoolIoBusy),
            Global("stream_clients", s => s.StreamClients),
            Global("preview_clients", s => s.PreviewClients),
            Global("recordings", s => s.Recordings),
            Global("recording_kbps", s => s.RecordingKbps),
            Global("spread", s => s.SpreadCaptures ? 1 : 0),
            Global("max_fps", s => s.MaxFps),
            Global("camera_count", s => s.Cameras.Count),
        };

        public static readonly PerfColumn<CameraPerfSample>[] CameraColumns =
        {
            Camera("camera_id", c => c.Id),
            new PerfColumn<CameraPerfSample>("camera", c => c.Name, isText: true),
            new PerfColumn<CameraPerfSample>("mode", c => c.HasWindow ? "window" : "stream", isText: true),
            Camera("renders_per_s", c => c.PerSecond(c[CameraMetric.Render].Count)),
            Camera("render_ms_avg", c => c[CameraMetric.Render].Average),
            Camera("render_ms_max", c => c[CameraMetric.Render].Max),
            Camera("stream_fps", c => c.PerSecond(c[CameraMetric.Encode].Count)),
            Camera("capture_ms_avg", c => c[CameraMetric.CaptureIssue].Average),
            Camera("capture_ms_max", c => c[CameraMetric.CaptureIssue].Max),
            Camera("readback_ms_avg", c => c[CameraMetric.Readback].Average),
            Camera("readback_ms_max", c => c[CameraMetric.Readback].Max),
            Camera("readback_copy_ms_avg", c => c[CameraMetric.ReadbackCopy].Average),
            Camera("encode_wait_ms_avg", c => c[CameraMetric.EncodeWait].Average),
            Camera("encode_wait_ms_max", c => c[CameraMetric.EncodeWait].Max),
            Camera("encode_ms_avg", c => c[CameraMetric.Encode].Average),
            Camera("encode_ms_max", c => c[CameraMetric.Encode].Max),
            Camera("jpeg_kb_avg", c => c[CameraMetric.JpegKb].Average),
            Camera("deferred_per_s", c => c.PerSecond(c.Deferred)),
            Camera("stream_clients", c => c.StreamClients),
            Camera("preview_clients", c => c.PreviewClients),
        };

        public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        public static string CsvHeader<T>(PerfColumn<T>[] columns)
            => "t_s," + string.Join(",", columns.Select(c => c.Key));

        public static string CsvRow<T>(double elapsedSeconds, PerfColumn<T>[] columns, T row)
            => Number(elapsedSeconds) + "," + string.Join(",", columns.Select(c => c.IsText ? CsvQuote(c.Format(row)) : c.Format(row)));

        public static string ToJson(PerfSnapshot snapshot)
        {
            var sb = new StringBuilder("{");
            AppendJsonFields(sb, GlobalColumns, snapshot);
            sb.Append(",\"cameras\":[");
            for (int i = 0; i < snapshot.Cameras.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                AppendJsonFields(sb, CameraColumns, snapshot.Cameras[i]);
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static PerfColumn<PerfSnapshot> Global(string key, Func<PerfSnapshot, double> value)
            => new PerfColumn<PerfSnapshot>(key, s => Number(value(s)));

        private static PerfColumn<CameraPerfSample> Camera(string key, Func<CameraPerfSample, double> value)
            => new PerfColumn<CameraPerfSample>(key, c => Number(value(c)));

        private static string CsvQuote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";

        private static void AppendJsonFields<T>(StringBuilder sb, PerfColumn<T>[] columns, T row)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var column = columns[i];
                sb.Append('"').Append(column.Key).Append("\":");
                string value = column.Format(row);
                if (column.IsText)
                    sb.Append('"').Append(JRTIStreamServer.EscapeJson(value)).Append('"');
                else
                    sb.Append(value);
            }
        }
    }
}
