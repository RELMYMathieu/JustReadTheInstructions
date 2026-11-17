using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace JustReadTheInstructions
{
    public sealed class RouteValues
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);

        internal void Set(string name, string value) => _values[name] = value;

        public string this[string name] => _values.TryGetValue(name, out var value) ? value : null;
    }

    public sealed class ApiRoute
    {
        private readonly string[] _segments;

        public ApiOperation Operation { get; }
        public IReadOnlyList<string> Methods { get; }
        public Action<HttpListenerContext, RouteValues> Handler { get; }

        public ApiRoute(ApiOperation operation, Action<HttpListenerContext, RouteValues> handler)
        {
            Operation = operation;
            Methods = operation.Methods.ToArray();
            Handler = handler;
            _segments = ApiRouter.Segments(operation.Template);
        }

        public bool Allows(string method)
            => Methods.Contains(method) || (method == "HEAD" && Methods.Contains("GET"));

        public RouteValues Match(string[] path)
        {
            if (path.Length != _segments.Length) return null;
            var values = new RouteValues();
            for (int i = 0; i < path.Length; i++)
            {
                var segment = _segments[i];
                if (IsParameter(segment)) values.Set(segment.Substring(1, segment.Length - 2), path[i]);
                else if (!string.Equals(segment, path[i], StringComparison.Ordinal)) return null;
            }
            return values;
        }

        private static bool IsParameter(string segment)
            => segment.Length > 2 && segment[0] == '{' && segment[segment.Length - 1] == '}';
    }

    public static class ApiRouter
    {
        public const int Version = 1;
        public const string VersionPrefix = "/api/v1";

        public enum Outcome { Matched, WrongMethod, NotFound }

        public static string[] Segments(string path)
            => path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

        public static bool TryStripVersion(string path, out string rest)
        {
            if (path == VersionPrefix) rest = "/";
            else rest = path.StartsWith(VersionPrefix + "/", StringComparison.Ordinal) ? path.Substring(VersionPrefix.Length) : null;
            return rest != null;
        }

        public static Outcome Resolve(IEnumerable<ApiRoute> routes, string method, string path,
                                      out ApiRoute route, out RouteValues values, out IReadOnlyList<string> allowed)
        {
            var segments = Segments(path);
            var allowedMethods = new List<string>();
            route = null;
            values = null;
            allowed = allowedMethods;

            foreach (var candidate in routes)
            {
                var match = candidate.Match(segments);
                if (match == null) continue;
                if (candidate.Allows(method))
                {
                    route = candidate;
                    values = match;
                    return Outcome.Matched;
                }
                allowedMethods.AddRange(candidate.Methods.Where(m => !allowedMethods.Contains(m)));
            }

            return allowedMethods.Count > 0 ? Outcome.WrongMethod : Outcome.NotFound;
        }
    }
}
