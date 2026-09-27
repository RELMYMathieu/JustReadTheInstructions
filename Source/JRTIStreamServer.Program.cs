using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly string ProgramFile =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/PluginData/program.txt";
        private static readonly TimeSpan EventHeartbeat = TimeSpan.FromSeconds(15);
        private const int EventQueueLimit = 64;
        private const int EventRetryMs = 2000;

        private readonly ConcurrentDictionary<Guid, BlockingCollection<string>> _eventClients
            = new ConcurrentDictionary<Guid, BlockingCollection<string>>();
        private readonly object _programLock = new object();
        private volatile string _programLayout;
        private long _programVersion;

        private string ProgramLayout => _programLayout;

        private void LoadProgram()
        {
            try
            {
                if (!File.Exists(ProgramFile)) return;
                var name = File.ReadAllText(ProgramFile, Encoding.UTF8).Trim();
                if (LayoutNamePattern.IsMatch(name)) _programLayout = name;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Could not read the on-air layout: {ex.Message}");
            }
        }

        private string ProgramJson()
        {
            var layout = _programLayout;
            var name = layout == null ? "null" : $"\"{EscapeJson(layout)}\"";
            return $"{{\"layout\":{name},\"version\":{Interlocked.Read(ref _programVersion)}}}";
        }

        private void SetProgramLayout(string name)
        {
            lock (_programLock)
            {
                _programLayout = name;
                Interlocked.Increment(ref _programVersion);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ProgramFile));
                    if (name == null) File.Delete(ProgramFile);
                    else File.WriteAllText(ProgramFile, name, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[JRTI-Stream]: Could not save the on-air layout: {ex.Message}");
                }
            }
            BroadcastEvent("program", ProgramJson());
        }

        private void HandleProgram(HttpListenerContext ctx, string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1 && ctx.Request.HttpMethod == "GET")
            {
                ServeText(ctx, ProgramJson(), "application/json");
                return;
            }
            if (parts.Length == 2 && parts[1] == "clear")
            {
                SetProgramLayout(null);
                ServeText(ctx, ProgramJson(), "application/json");
                return;
            }
            if (parts.Length == 3 && parts[1] == "take")
            {
                TakeProgram(ctx, Uri.UnescapeDataString(parts[2]).Trim());
                return;
            }
            ServeError(ctx, 404, "Use GET /program, /program/take/<layout> or /program/clear");
        }

        private void TakeProgram(HttpListenerContext ctx, string name)
        {
            if (!LayoutNamePattern.IsMatch(name) || !LayoutExists(name))
            {
                ServeError(ctx, 404, $"No saved layout named {name}");
                return;
            }
            SetProgramLayout(name);
            ServeText(ctx, ProgramJson(), "application/json");
        }

        private void ServeEvents(HttpListenerContext ctx)
        {
            var id = Guid.NewGuid();
            var queue = new BlockingCollection<string>(new ConcurrentQueue<string>(), EventQueueLimit);
            _eventClients[id] = queue;

            ctx.Response.ContentType = "text/event-stream; charset=utf-8";
            ctx.Response.SendChunked = true;
            ctx.Response.Headers.Add("Cache-Control", "no-cache");

            try
            {
                var output = ctx.Response.OutputStream;
                WriteEvent(output, $"retry: {EventRetryMs}\n\n");
                WriteEvent(output, EventMessage("program", ProgramJson()));
                while (_running && !queue.IsCompleted)
                    WriteEvent(output, queue.TryTake(out var message, EventHeartbeat) ? message : ": ping\n\n");
            }
            catch { }
            finally
            {
                _eventClients.TryRemove(id, out _);
                queue.Dispose();
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static string EventMessage(string eventName, string json) => $"event: {eventName}\ndata: {json}\n\n";

        private static void WriteEvent(Stream output, string message)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }

        private void BroadcastEvent(string eventName, string json)
        {
            var message = EventMessage(eventName, json);
            foreach (var client in _eventClients.Values)
            {
                try { client.TryAdd(message); }
                catch (InvalidOperationException) { }
            }
        }

        private void CloseEventClients()
        {
            foreach (var client in _eventClients.Values)
            {
                try { client.CompleteAdding(); }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
