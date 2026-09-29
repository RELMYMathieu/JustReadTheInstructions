using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private static readonly string LayoutsRoot =
            KSPUtil.ApplicationRootPath + "GameData/JustReadTheInstructions/PluginData/Layouts/";
        private static readonly Regex LayoutNamePattern = new Regex(@"^[\p{L}\p{N} _\-]{1,64}$");
        private const int MaxLayoutBytes = 64 * 1024;
        private static readonly object LayoutsLock = new object();

        private static string LayoutFile(string name) => LayoutsRoot + name + ".json";

        private static bool LayoutExists(string name)
        {
            lock (LayoutsLock)
                return File.Exists(LayoutFile(name));
        }

        private static string LayoutEventJson(string name) => $"{{\"name\":\"{EscapeJson(name)}\"}}";

        private void HandleLayouts(HttpListenerContext ctx, string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                ServeLayoutList(ctx);
                return;
            }

            var name = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]).Trim() : "";
            if (!LayoutNamePattern.IsMatch(name))
            {
                ServeError(ctx, 400, "Layout names use letters, digits, spaces, - and _ (64 at most)");
                return;
            }

            switch (ctx.Request.HttpMethod)
            {
                case "GET": ServeLayout(ctx, name); break;
                case "PUT":
                case "POST": SaveLayout(ctx, name); break;
                case "DELETE": DeleteLayout(ctx, name); break;
                default: ServeError(ctx, 405, "Use GET, PUT or DELETE"); break;
            }
        }

        private static void ServeLayoutList(HttpListenerContext ctx)
        {
            var entries = new List<string>();
            lock (LayoutsLock)
            {
                if (Directory.Exists(LayoutsRoot))
                {
                    var files = Directory.GetFiles(LayoutsRoot, "*.json");
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (var file in files)
                    {
                        var updated = File.GetLastWriteTimeUtc(file).ToString("o", CultureInfo.InvariantCulture);
                        entries.Add($"{{\"name\":\"{EscapeJson(Path.GetFileNameWithoutExtension(file))}\",\"updated\":\"{updated}\"}}");
                    }
                }
            }
            ServeText(ctx, $"[{string.Join(",", entries)}]", "application/json");
        }

        private static void ServeLayout(HttpListenerContext ctx, string name)
        {
            string json;
            lock (LayoutsLock)
                json = File.Exists(LayoutFile(name)) ? File.ReadAllText(LayoutFile(name), Encoding.UTF8) : null;

            if (json == null) ServeError(ctx, 404, "Layout not found");
            else ServeText(ctx, json, "application/json");
        }

        private void SaveLayout(HttpListenerContext ctx, string name)
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd().Trim();

            if (body.Length == 0 || body.Length > MaxLayoutBytes || body[0] != '{' || body[body.Length - 1] != '}')
            {
                ServeError(ctx, 400, "Expected a JSON object of 64 KB at most");
                return;
            }

            var file = LayoutFile(name);
            try
            {
                lock (LayoutsLock)
                {
                    Directory.CreateDirectory(LayoutsRoot);
                    var temp = file + ".tmp";
                    File.WriteAllText(temp, body, new UTF8Encoding(false));
                    if (File.Exists(file)) File.Delete(file);
                    File.Move(temp, file);
                }
                ServeStatus(ctx, 204);
                BroadcastEvent("layout", LayoutEventJson(name));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Could not save layout {name}: {ex.Message}");
                ServeError(ctx, 500, "Could not save the layout");
            }
        }

        private void DeleteLayout(HttpListenerContext ctx, string name)
        {
            try
            {
                lock (LayoutsLock)
                    if (File.Exists(LayoutFile(name))) File.Delete(LayoutFile(name));
                ServeStatus(ctx, 204);
                BroadcastEvent("layout", LayoutEventJson(name));
                if (string.Equals(ProgramLayout, name, StringComparison.OrdinalIgnoreCase))
                    SetProgramLayout(null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[JRTI-Stream]: Could not delete layout {name}: {ex.Message}");
                ServeError(ctx, 500, "Could not delete the layout");
            }
        }

        private static void ServeStatus(HttpListenerContext ctx, int code)
        {
            ctx.Response.StatusCode = code;
            ctx.Response.Close();
        }
    }
}
