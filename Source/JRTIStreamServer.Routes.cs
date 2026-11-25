using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

namespace JustReadTheInstructions
{
    public partial class JRTIStreamServer
    {
        private const string ControlKeyQuery = "key";
        private const int PreflightMaxAgeSeconds = 600;

        private ApiRoute[] _apiRoutes;
        private ApiRoute[] _pageRoutes;
        private string _openApiJson;

        private IReadOnlyList<ApiRoute> ApiRoutes => _apiRoutes ?? (_apiRoutes = BuildApiRoutes());
        private IEnumerable<ApiRoute> UnversionedApiRoutes => ApiRoutes.Where(route => !route.Operation.VersionedOnly);
        private IReadOnlyList<ApiRoute> PageRoutes => _pageRoutes ?? (_pageRoutes = BuildPageRoutes());
        private string OpenApiJson => _openApiJson ?? (_openApiJson = OpenApiDocument.Build(ApiCatalog.Operations, ApiCatalog.Components));

        private Dictionary<string, Action<HttpListenerContext, RouteValues>> ApiHandlers() => new Dictionary<string, Action<HttpListenerContext, RouteValues>>
        {
            ["getApi"] = (ctx, _) => ServeApiDiscovery(ctx),
            ["getOpenApi"] = (ctx, _) => ServeText(ctx, OpenApiJson, "application/json"),
            ["getSession"] = (ctx, _) => ServeSession(ctx),
            ["listCameras"] = (ctx, _) => ServeCameraList(ctx),
            ["getStreams"] = (ctx, _) => ServeMultiStream(ctx),
            ["getEvents"] = (ctx, _) => ServeEvents(ctx),
            ["getDebugStats"] = (ctx, _) => ServeDebugStats(ctx),
            ["getProgram"] = (ctx, _) => ServeText(ctx, ProgramJson(), "application/json"),
            ["takeProgram"] = (ctx, v) => TakeProgram(ctx, LayoutName(v)),
            ["clearProgram"] = (ctx, _) => { SetProgramLayout(null); ServeText(ctx, ProgramJson(), "application/json"); },
            ["listLayouts"] = (ctx, _) => ServeLayoutList(ctx),
            ["getLayout"] = ForLayout(ServeLayout),
            ["saveLayout"] = ForLayout(SaveLayout),
            ["deleteLayout"] = ForLayout(DeleteLayout),
            ["listRecordings"] = (ctx, _) => ServeRecordingList(ctx),
            ["getRecording"] = (ctx, v) => ServeRecordingFile(ctx, v["file"]),
            ["getSnapshot"] = ForCamera((ctx, id, state, _) => ServeSnapshot(ctx, state)),
            ["getStream"] = ForCamera((ctx, id, state, _) => ServeMjpeg(ctx, state)),
            ["getPreview"] = ForCamera((ctx, id, state, _) => ServePreviewMjpeg(ctx, state)),
            ["getAudio"] = ForCamera((ctx, id, state, _) => ServeCameraAudio(ctx, id, state)),
            ["getCameraStatus"] = ForCamera((ctx, id, state, _) => ServeText(ctx, "ok", "text/plain")),
            ["getCameraSettings"] = ForCamera((ctx, id, state, _) => ServeCameraSettings(ctx, state)),
            ["changeCameraSettings"] = ForCamera((ctx, id, state, _) => UpdateCameraSettings(ctx, state)),
            ["stopRecording"] = ForCamera((ctx, id, state, _) => StopGameRecording(ctx, state, discard: false)),
            ["discardRecording"] = ForCamera((ctx, id, state, _) => StopGameRecording(ctx, state, discard: true)),
            ["controlRecording"] = ForCamera((ctx, id, state, v) => HandleGameRecording(ctx, id, state, v["action"])),
        };

        private ApiRoute[] BuildApiRoutes()
        {
            var handlers = ApiHandlers();
            foreach (var missing in ApiCatalog.Operations.Where(op => !handlers.ContainsKey(op.Id)))
                Debug.LogError($"[JRTI-Stream]: API route {missing.Id} has no handler");
            return ApiCatalog.Operations
                .Where(op => handlers.ContainsKey(op.Id))
                .Select(op => new ApiRoute(op, handlers[op.Id]))
                .ToArray();
        }

        private ApiRoute[] BuildPageRoutes() => new[]
        {
            new ApiRoute(new ApiOperation("mainPage", "GET", "/", "Main page"), (ctx, _) => ServeWebFile(ctx, "index.html")),
            new ApiRoute(new ApiOperation("mainPageFile", "GET", "/index.html", "Main page"), (ctx, _) => ServeWebFile(ctx, "index.html")),
            new ApiRoute(new ApiOperation("cameraPage", "GET", "/camera/{id}", "Opens the viewer page"), (ctx, v) => RedirectToViewer(ctx, v["id"])),
        };

        private bool TryServeRoute(HttpListenerContext ctx, IEnumerable<ApiRoute> routes, string path)
        {
            var request = ctx.Request;
            var origin = request.Headers["Origin"];
            bool corsAllowed = origin != null && ApiAccess.IsLocalhostOrigin(origin) && !ApiAccess.IsSameOrigin(origin, request.UserHostName);

            var outcome = ApiRouter.Resolve(routes, request.HttpMethod, path, out var route, out var values, out var allowed);
            if (outcome == ApiRouter.Outcome.NotFound) return false;

            if (corsAllowed)
            {
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", origin);
                ctx.Response.Headers.Add("Vary", "Origin");
            }

            if (outcome == ApiRouter.Outcome.WrongMethod)
            {
                if (request.HttpMethod == "OPTIONS" && corsAllowed) ServePreflight(ctx, allowed);
                else
                {
                    ctx.Response.Headers.Add("Allow", string.Join(", ", allowed));
                    ServeError(ctx, 405, $"Use {string.Join(" or ", allowed)}");
                }
                return true;
            }

            if (route.Operation.Writes && !MayWrite(ctx)) return true;
            route.Handler(ctx, values);
            return true;
        }

        private static void ServePreflight(HttpListenerContext ctx, IReadOnlyList<string> allowed)
        {
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", string.Join(", ", allowed));
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Authorization, Content-Type");
            ctx.Response.Headers.Add("Access-Control-Allow-Private-Network", "true");
            ctx.Response.Headers.Add("Access-Control-Max-Age", PreflightMaxAgeSeconds.ToString());
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
        }

        private static bool MayWrite(HttpListenerContext ctx)
        {
            var request = ctx.Request;
            var verdict = ApiAccess.CheckWrite(JRTISettings.RequireControlKey, JRTISettings.ControlKey, request.IsLocal,
                request.UserHostName, request.QueryString[ControlKeyQuery], name => request.Headers[name]);

            switch (verdict)
            {
                case ApiAccess.WriteVerdict.OtherWebsite:
                    ServeError(ctx, 403, "Pages from other websites cannot change anything here");
                    return false;
                case ApiAccess.WriteVerdict.NeedsKey:
                    ctx.Response.Headers.Add("WWW-Authenticate", "Bearer realm=\"JRTI\"");
                    ServeError(ctx, 401, "This needs the control key shown in the game's JRTI settings");
                    return false;
                default:
                    return true;
            }
        }

        private Action<HttpListenerContext, RouteValues> ForCamera(Action<HttpListenerContext, int, CameraStreamState, RouteValues> handler)
            => (ctx, values) =>
            {
                if (!int.TryParse(values["id"], out int cameraId))
                {
                    ServeError(ctx, 400, "Invalid camera ID");
                    return;
                }
                if (!_states.TryGetValue(cameraId, out var state))
                {
                    ServeError(ctx, 404, "Camera not found");
                    return;
                }
                handler(ctx, cameraId, state, values);
            };

        private static Action<HttpListenerContext, RouteValues> ForLayout(Action<HttpListenerContext, string> handler)
            => (ctx, values) =>
            {
                var name = LayoutName(values);
                if (!LayoutNamePattern.IsMatch(name))
                {
                    ServeError(ctx, 400, "Layout names use letters, digits, spaces, - and _ (64 at most)");
                    return;
                }
                handler(ctx, name);
            };

        private static string LayoutName(RouteValues values) => Uri.UnescapeDataString(values["layout"]).Trim();

        private static void RedirectToViewer(HttpListenerContext ctx, string id)
        {
            if (!int.TryParse(id, out int cameraId))
            {
                ServeError(ctx, 400, "Invalid camera ID");
                return;
            }
            ctx.Response.Redirect($"/viewer.html?id={cameraId}");
            ctx.Response.Close();
        }

        private void ServeApiDiscovery(HttpListenerContext ctx)
        {
            var routes = ApiRoutes.Select(route =>
                $"{{\"id\":{ApiSchema.Quote(route.Operation.Id)}," +
                $"\"methods\":[{string.Join(",", route.Methods.Select(ApiSchema.Quote))}]," +
                $"\"path\":{ApiSchema.Quote(ApiRouter.VersionPrefix + route.Operation.Template.TrimEnd('/'))}," +
                $"\"summary\":{ApiSchema.Quote(route.Operation.Summary)}," +
                $"\"writes\":{Bool(route.Operation.Writes)}}}");

            var json = new StringBuilder("{");
            json.Append($"\"name\":{ApiSchema.Quote(ApiCatalog.Title)},");
            json.Append($"\"version\":\"{JRTISettingsGUI.ModVersion}\",");
            json.Append($"\"api\":{ApiRouter.Version},");
            json.Append($"\"launchId\":\"{LaunchId}\",");
            json.Append($"\"features\":{{\"inGameRecording\":{Bool(InGameRecordingAvailable)},\"codecs\":{CodecsJson()},\"controlKeyRequired\":{Bool(JRTISettings.RequireControlKey)}}},");
            json.Append($"\"routes\":[{string.Join(",", routes)}]");
            json.Append('}');
            ServeText(ctx, json.ToString(), "application/json");
        }

        private static string Bool(bool value) => value ? "true" : "false";

        internal string LinkWithControlKey()
        {
            var baseUrl = LanUrls().FirstOrDefault();
            return baseUrl == null ? null : $"{baseUrl}?{ControlKeyQuery}={JRTISettings.ControlKey}";
        }
    }
}
