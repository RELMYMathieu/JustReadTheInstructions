using System.Collections.Generic;
using System.Linq;
using System.Text;
using static JustReadTheInstructions.ApiSchema;

namespace JustReadTheInstructions
{
    public sealed class ApiResponse
    {
        public int Status { get; }
        public string ContentType { get; }
        public ApiSchema Schema { get; }
        public string Description { get; }

        private ApiResponse(int status, string contentType, ApiSchema schema, string description)
        {
            Status = status;
            ContentType = contentType;
            Schema = schema;
            Description = description;
        }

        public static ApiResponse Json(ApiSchema schema, string description = "JSON") => new ApiResponse(200, "application/json", schema, description);
        public static ApiResponse Binary(string contentType, string description) => new ApiResponse(200, contentType, null, description);
        public static ApiResponse Empty(int status, string description) => new ApiResponse(status, null, null, description);
    }

    public sealed class ApiParameter
    {
        public string Name { get; }
        public ApiSchema Schema { get; }
        public string Description { get; }

        public ApiParameter(string name, ApiSchema schema, string description)
        {
            Name = name;
            Schema = schema;
            Description = description;
        }
    }

    public sealed class ApiOperation
    {
        public string Id { get; }
        public string Method { get; }
        public string Template { get; }
        public string Summary { get; }
        public string[] AlsoAccepts { get; set; } = new string[0];
        public bool Writes { get; set; }
        public bool VersionedOnly { get; set; }
        public ApiParameter[] Path { get; set; } = new ApiParameter[0];
        public ApiParameter[] Query { get; set; } = new ApiParameter[0];
        public ApiSchema Body { get; set; }
        public ApiResponse Response { get; set; }

        public ApiOperation(string id, string method, string template, string summary)
        {
            Id = id;
            Method = method;
            Template = template;
            Summary = summary;
        }

        public IEnumerable<string> Methods => new[] { Method }.Concat(AlsoAccepts);

        public IEnumerable<string> PathParameterNames
            => ApiRouter.Segments(Template).Where(s => s.StartsWith("{", System.StringComparison.Ordinal)).Select(s => s.Trim('{', '}'));
    }

    public static class ApiCatalog
    {
        public const string Title = "Just Read The Instructions";

        private static readonly ApiSchema Mic = Enum("game", "external", "onboard");
        private static readonly ApiSchema Track = Enum("off", "vessel", "target");
        private static readonly ApiSchema Codec = Enum("h264", "av1");

        private static readonly ApiParameter CameraId = new ApiParameter("id", Integer, "Camera ID, as listed by /cameras");
        private static readonly ApiParameter LayoutName = new ApiParameter("layout", String, "Layout name: letters, digits, spaces, - and _ (64 at most)");

        public static readonly IReadOnlyDictionary<string, ApiSchema> Components = new SortedDictionary<string, ApiSchema>(System.StringComparer.Ordinal)
        {
            ["Discovery"] = Object(
                new ApiField("name", String),
                new ApiField("version", String),
                new ApiField("api", Integer),
                new ApiField("launchId", String),
                new ApiField("features", Object(
                    new ApiField("inGameRecording", Boolean),
                    new ApiField("codecs", ArrayOf(Codec)),
                    new ApiField("controlKeyRequired", Boolean))),
                new ApiField("routes", ArrayOf(Ref("Route")))),
            ["Route"] = Object(
                new ApiField("id", String),
                new ApiField("methods", ArrayOf(String)),
                new ApiField("path", String),
                new ApiField("summary", String),
                new ApiField("writes", Boolean)),
            ["Session"] = Object(
                new ApiField("launchId", String),
                new ApiField("version", String),
                new ApiField("inGameRecording", Boolean),
                new ApiField("codecs", ArrayOf(Codec)),
                new ApiField("lanUrls", ArrayOf(String)),
                new ApiField("controlKeyRequired", Boolean)),
            ["Camera"] = Object(
                new ApiField("id", Integer),
                new ApiField("name", String),
                new ApiField("streaming", Boolean),
                new ApiField("viewerCount", Integer),
                new ApiField("snapshotUrl", String),
                new ApiField("streamUrl", String),
                new ApiField("mic", Mic),
                new ApiField("canPan", Boolean),
                new ApiField("track", Track),
                new ApiField("recording", Nullable(Ref("Recording")))),
            ["Recording"] = Object(
                new ApiField("file", String),
                new ApiField("paused", Boolean),
                new ApiField("elapsedMs", Integer),
                new ApiField("bytes", Integer),
                new ApiField("framesWritten", Integer),
                new ApiField("framesDropped", Integer),
                new ApiField("error", Nullable(String))),
            ["CameraSettings"] = Object(
                new ApiField("brightness", Number),
                new ApiField("contrast", Number),
                new ApiField("gamma", Number),
                new ApiField("mic", Mic),
                new ApiField("soundGain", Number),
                new ApiField("autoGain", Boolean),
                new ApiField("mastering", Boolean),
                new ApiField("fov", Number, required: false),
                new ApiField("fovMin", Number, required: false),
                new ApiField("fovMax", Number, required: false),
                new ApiField("fovLimitMin", Number, required: false),
                new ApiField("fovLimitMax", Number, required: false),
                new ApiField("pan", Ref("Pan"), required: false)),
            ["Pan"] = Object(
                new ApiField("yaw", Number),
                new ApiField("pitch", Number),
                new ApiField("pitchMin", Number),
                new ApiField("pitchMax", Number),
                new ApiField("track", Track)),
            ["CameraSettingsChange"] = Object(new[]
            {
                new ApiField("brightness", Number),
                new ApiField("contrast", Number),
                new ApiField("gamma", Number),
                new ApiField("fov", Number),
                new ApiField("mic", Mic),
                new ApiField("soundGain", Number),
                new ApiField("autoGain", Boolean),
                new ApiField("mastering", Boolean),
                new ApiField("zoomRate", Number),
                new ApiField("panYaw", Number),
                new ApiField("panPitch", Number),
                new ApiField("panYawRate", Number),
                new ApiField("panPitchRate", Number),
                new ApiField("track", Track),
            }.Optional().ToArray()),
            ["Program"] = Object(
                new ApiField("layout", Nullable(String)),
                new ApiField("version", Integer)),
            ["LayoutEntry"] = Object(
                new ApiField("name", String),
                new ApiField("updated", DateTime)),
            ["RecordingFile"] = Object(
                new ApiField("file", String),
                new ApiField("bytes", Integer),
                new ApiField("modified", DateTime),
                new ApiField("recording", Boolean)),
        };

        public static readonly IReadOnlyList<ApiOperation> Operations = new[]
        {
            new ApiOperation("getApi", "GET", "/", "Mod version, features and every route")
                { VersionedOnly = true, Response = ApiResponse.Json(Ref("Discovery")) },
            new ApiOperation("getOpenApi", "GET", "/openapi.json", "This API as an OpenAPI 3.1 document, to generate clients")
                { VersionedOnly = true, Response = ApiResponse.Json(AnyObject) },
            new ApiOperation("getSession", "GET", "/session", "Game session: launch id, mod version, recording support, codecs, network addresses")
                { Response = ApiResponse.Json(Ref("Session")) },
            new ApiOperation("listCameras", "GET", "/cameras", "Cameras with their name, viewers, mic, pan and recording state")
                { Response = ApiResponse.Json(ArrayOf(Ref("Camera"))) },
            new ApiOperation("getStreams", "GET", "/streams", "Several cameras in one multipart stream; each part names its camera in an X-Camera-Id header")
            {
                Query = new[]
                {
                    new ApiParameter("ids", String, "Camera IDs separated by commas, such as 10,11"),
                    new ApiParameter("preview", Enum("1"), "1 for preview-size frames"),
                },
                Response = ApiResponse.Binary("multipart/x-mixed-replace", "JPEG frames"),
            },
            new ApiOperation("getEvents", "GET", "/events", "Server-Sent Events: program (the layout on air) and layout (a saved layout changed)")
                { Response = ApiResponse.Binary("text/event-stream", "Events, with a ping comment every 15 seconds") },
            new ApiOperation("getDebugStats", "GET", "/debug/stats", "Latest performance sample")
                { Response = ApiResponse.Json(AnyObject) },
            new ApiOperation("getProgram", "GET", "/program", "The layout on air")
                { Response = ApiResponse.Json(Ref("Program")) },
            new ApiOperation("takeProgram", "POST", "/program/take/{layout}", "Puts a saved layout on air. Also answers GET, for Stream Deck and browser links")
                { AlsoAccepts = new[] { "GET" }, Writes = true, Path = new[] { LayoutName }, Response = ApiResponse.Json(Ref("Program")) },
            new ApiOperation("clearProgram", "POST", "/program/clear", "Takes the program off air. Also answers GET, for links")
                { AlsoAccepts = new[] { "GET" }, Writes = true, Response = ApiResponse.Json(Ref("Program")) },
            new ApiOperation("listLayouts", "GET", "/layouts", "Saved layouts")
                { Response = ApiResponse.Json(ArrayOf(Ref("LayoutEntry"))) },
            new ApiOperation("getLayout", "GET", "/layouts/{layout}", "One saved layout, as saved by the layout page")
                { Path = new[] { LayoutName }, Response = ApiResponse.Json(AnyObject) },
            new ApiOperation("saveLayout", "PUT", "/layouts/{layout}", "Saves a layout: a JSON object of 64 KB at most. Also answers POST")
                { AlsoAccepts = new[] { "POST" }, Writes = true, Path = new[] { LayoutName }, Body = AnyObject, Response = ApiResponse.Empty(204, "Saved") },
            new ApiOperation("deleteLayout", "DELETE", "/layouts/{layout}", "Deletes a layout")
                { Writes = true, Path = new[] { LayoutName }, Response = ApiResponse.Empty(204, "Deleted") },
            new ApiOperation("listRecordings", "GET", "/recordings", "Recordings on disk, newest first")
                { Response = ApiResponse.Json(ArrayOf(Ref("RecordingFile"))) },
            new ApiOperation("getRecording", "GET", "/recordings/{file}", "One recording, with byte ranges for seeking")
            {
                Path = new[] { new ApiParameter("file", String, "File name, as listed by /recordings") },
                Query = new[] { new ApiParameter("download", Enum("1"), "1 to save it instead of playing it") },
                Response = ApiResponse.Binary("video/mp4", "The video file"),
            },
            new ApiOperation("getSnapshot", "GET", "/camera/{id}/snapshot", "Latest frame")
                { Path = new[] { CameraId }, Response = ApiResponse.Binary("image/jpeg", "JPEG image") },
            new ApiOperation("getStream", "GET", "/camera/{id}/stream", "MJPEG stream at render size, for OBS, VLC or an img tag")
                { Path = new[] { CameraId }, Response = ApiResponse.Binary("multipart/x-mixed-replace", "JPEG frames") },
            new ApiOperation("getPreview", "GET", "/camera/{id}/preview", "MJPEG stream at preview size")
                { Path = new[] { CameraId }, Response = ApiResponse.Binary("multipart/x-mixed-replace", "JPEG frames") },
            new ApiOperation("getAudio", "GET", "/camera/{id}/audio", "Camera sound as an endless WAV stream")
                { Path = new[] { CameraId }, Response = ApiResponse.Binary("audio/wav", "PCM audio") },
            new ApiOperation("getCameraStatus", "GET", "/camera/{id}/status", "Answers ok while the camera exists")
                { Path = new[] { CameraId }, Response = ApiResponse.Binary("text/plain", "ok") },
            new ApiOperation("getCameraSettings", "GET", "/camera/{id}/settings", "Image, sound, field of view and pan settings")
                { Path = new[] { CameraId }, Response = ApiResponse.Json(Ref("CameraSettings")) },
            new ApiOperation("changeCameraSettings", "POST", "/camera/{id}/settings", "Changes any of the listed fields and leaves the others alone")
                { Writes = true, Path = new[] { CameraId }, Body = Ref("CameraSettingsChange"), Response = ApiResponse.Empty(200, "Changed") },
            new ApiOperation("controlRecording", "POST", "/camera/{id}/recording/{action}", "Starts, stops, discards, pauses or resumes the in-game recording")
            {
                Writes = true,
                Path = new[] { CameraId, new ApiParameter("action", Enum("start", "stop", "discard", "pause", "resume"), "What to do") },
                Query = new[] { new ApiParameter("codec", Codec, "Codec for start, h264 by default") },
                Response = ApiResponse.Json(Nullable(Ref("Recording")), "The recording, or null once stopped"),
            },
        };

        public static ApiOperation Find(string id) => Operations.First(op => op.Id == id);
    }

    public static class OpenApiDocument
    {
        public static string Build(IEnumerable<ApiOperation> operations, IReadOnlyDictionary<string, ApiSchema> components)
        {
            var paths = operations
                .GroupBy(op => op.Template)
                .Select(group => $"{Quote(group.Key)}:{{{string.Join(",", group.Select(OperationJson))}}}");

            var schemas = components.Select(kv => $"{Quote(kv.Key)}:{kv.Value}");

            var json = new StringBuilder("{");
            json.Append("\"openapi\":\"3.1.0\",");
            json.Append($"\"info\":{{\"title\":{Quote(ApiCatalog.Title)},\"version\":\"{ApiRouter.Version}\"}},");
            json.Append($"\"servers\":[{{\"url\":{Quote(ApiRouter.VersionPrefix)}}}],");
            json.Append($"\"paths\":{{{string.Join(",", paths)}}},");
            json.Append("\"components\":{");
            json.Append($"\"schemas\":{{{string.Join(",", schemas)}}},");
            json.Append("\"securitySchemes\":{\"controlKey\":{\"type\":\"http\",\"scheme\":\"bearer\",\"description\":\"Only when the player turns on Require a key. The key can also go in a key query parameter\"}}");
            json.Append("}}");
            return Json.Pretty(json.ToString());
        }

        private static string OperationJson(ApiOperation op)
        {
            var parameters = op.Path.Select(p => ParameterJson(p, "path", true))
                .Concat(op.Query.Select(p => ParameterJson(p, "query", false)))
                .ToList();

            var json = new StringBuilder($"{Quote(op.Method.ToLowerInvariant())}:{{");
            json.Append($"\"operationId\":{Quote(op.Id)},\"summary\":{Quote(op.Summary)}");
            if (parameters.Count > 0) json.Append($",\"parameters\":[{string.Join(",", parameters)}]");
            if (op.Body != null) json.Append($",\"requestBody\":{{\"required\":true,\"content\":{{\"application/json\":{{\"schema\":{op.Body}}}}}}}");
            json.Append($",\"responses\":{{{ResponseJson(op.Response)}");
            if (op.Writes)
                json.Append(",\"401\":{\"description\":\"The control key is required and missing or wrong\"},\"403\":{\"description\":\"Sent by a web page from another site\"}");
            json.Append('}');
            if (op.Writes) json.Append(",\"security\":[{},{\"controlKey\":[]}]");
            return json.Append('}').ToString();
        }

        private static string ParameterJson(ApiParameter parameter, string location, bool required)
            => $"{{\"name\":{Quote(parameter.Name)},\"in\":\"{location}\",\"required\":{(required ? "true" : "false")}," +
               $"\"description\":{Quote(parameter.Description)},\"schema\":{parameter.Schema}}}";

        private static string ResponseJson(ApiResponse response)
        {
            var json = new StringBuilder($"\"{response.Status}\":{{\"description\":{Quote(response.Description)}");
            if (response.ContentType != null)
            {
                json.Append($",\"content\":{{{Quote(response.ContentType)}:{{");
                if (response.Schema != null) json.Append($"\"schema\":{response.Schema}");
                json.Append("}}");
            }
            return json.Append('}').ToString();
        }
    }

        public static class Json
    {
        public static string Pretty(string json)
        {
            var output = new StringBuilder();
            int depth = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    output.Append(c);
                    if (c == '\\') output.Append(json[++i]);
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; output.Append(c); break;
                    case '{':
                    case '[':
                        output.Append(c);
                        if (IsEmptyContainer(json, i)) { output.Append(json[++i]); break; }
                        NewLine(output, ++depth);
                        break;
                    case '}':
                    case ']': NewLine(output, --depth); output.Append(c); break;
                    case ',': output.Append(c); NewLine(output, depth); break;
                    case ':': output.Append(": "); break;
                    default: output.Append(c); break;
                }
            }
            return output.Append('\n').ToString();
        }

        private static bool IsEmptyContainer(string json, int i)
            => i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']');

        private static void NewLine(StringBuilder output, int depth) => output.Append('\n').Append(' ', depth * 2);
    }
}
