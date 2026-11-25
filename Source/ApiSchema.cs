using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JustReadTheInstructions
{
    public sealed class ApiField
    {
        public string Name { get; }
        public ApiSchema Schema { get; }
        public bool Required { get; }

        public ApiField(string name, ApiSchema schema, bool required = true, string description = null)
        {
            Name = name;
            Schema = description == null ? schema : schema.WithDescription(description);
            Required = required;
        }
    }

    public sealed class ApiSchema
    {
        private readonly string _json;

        private ApiSchema(string json) => _json = json;

        public static readonly ApiSchema String = new ApiSchema("{\"type\":\"string\"}");
        public static readonly ApiSchema Integer = new ApiSchema("{\"type\":\"integer\"}");
        public static readonly ApiSchema Number = new ApiSchema("{\"type\":\"number\"}");
        public static readonly ApiSchema Boolean = new ApiSchema("{\"type\":\"boolean\"}");
        public static readonly ApiSchema DateTime = new ApiSchema("{\"type\":\"string\",\"format\":\"date-time\"}");
        public static readonly ApiSchema AnyObject = new ApiSchema("{\"type\":\"object\",\"additionalProperties\":true}");

        public ApiSchema WithDescription(string description)
            => new ApiSchema($"{{\"description\":{Quote(description)},{_json.Substring(1)}");

        public static ApiSchema Ref(string component) => new ApiSchema($"{{\"$ref\":\"#/components/schemas/{component}\"}}");

        public static ApiSchema ArrayOf(ApiSchema items) => new ApiSchema($"{{\"type\":\"array\",\"items\":{items._json}}}");

        public static ApiSchema Nullable(ApiSchema schema) => new ApiSchema($"{{\"oneOf\":[{schema._json},{{\"type\":\"null\"}}]}}");

        public static ApiSchema Enum(params string[] values)
            => new ApiSchema($"{{\"type\":\"string\",\"enum\":[{string.Join(",", values.Select(Quote))}]}}");

        public static ApiSchema Object(params ApiField[] fields)
        {
            var properties = string.Join(",", fields.Select(f => $"{Quote(f.Name)}:{f.Schema._json}"));
            var required = fields.Where(f => f.Required).Select(f => Quote(f.Name)).ToList();
            var json = new StringBuilder("{\"type\":\"object\",\"properties\":{").Append(properties).Append('}');
            if (required.Count > 0) json.Append(",\"required\":[").Append(string.Join(",", required)).Append(']');
            return new ApiSchema(json.Append('}').ToString());
        }

        public static string Quote(string text)
        {
            var json = new StringBuilder("\"");
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    default:
                        if (c < 0x20) json.Append($"\\u{(int)c:x4}");
                        else json.Append(c);
                        break;
                }
            }
            return json.Append('"').ToString();
        }

        public override string ToString() => _json;
    }

    public static class ApiSchemaExtensions
    {
        public static IEnumerable<ApiField> Optional(this IEnumerable<ApiField> fields)
            => fields.Select(f => new ApiField(f.Name, f.Schema, required: false));
    }
}
