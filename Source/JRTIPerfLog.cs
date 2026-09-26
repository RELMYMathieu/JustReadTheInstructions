using System;
using System.IO;

namespace JustReadTheInstructions
{
    internal sealed class JRTIPerfLog : IDisposable
    {
        private static readonly string LogDirectory =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/PluginData/PerfLogs/";

        private readonly StreamWriter _global;
        private readonly StreamWriter _cameras;
        private readonly DateTime _startUtc;

        public string BaseName { get; }
        public int Rows { get; private set; }

        private JRTIPerfLog(string baseName)
        {
            BaseName = baseName;
            _startUtc = DateTime.UtcNow;
            _global = CreateWriter(baseName + "-global.csv", PerfFormat.CsvHeader(PerfFormat.GlobalColumns));
            _cameras = CreateWriter(baseName + "-cameras.csv", PerfFormat.CsvHeader(PerfFormat.CameraColumns));
        }

        public static JRTIPerfLog Start()
        {
            Directory.CreateDirectory(LogDirectory);
            return new JRTIPerfLog("perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }

        public void Write(PerfSnapshot snapshot)
        {
            double elapsed = (snapshot.Utc - _startUtc).TotalSeconds;
            _global.WriteLine(PerfFormat.CsvRow(elapsed, PerfFormat.GlobalColumns, snapshot));
            foreach (var camera in snapshot.Cameras)
                _cameras.WriteLine(PerfFormat.CsvRow(elapsed, PerfFormat.CameraColumns, camera));
            _global.Flush();
            _cameras.Flush();
            Rows++;
        }

        public void Dispose()
        {
            _global.Dispose();
            _cameras.Dispose();
        }

        private static StreamWriter CreateWriter(string fileName, string header)
        {
            var writer = new StreamWriter(Path.Combine(LogDirectory, fileName));
            writer.WriteLine(header);
            return writer;
        }
    }
}
