using System.Text;
using System.Text.RegularExpressions;
using curl.Models;

namespace curl.Services
{
    public class CurlParser : ICurlParser
    {
        private static readonly HashSet<string> SupportedMethods = new(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"
        };

        public ParsedCurlCommand Parse(string curlText)
        {
            if (string.IsNullOrWhiteSpace(curlText))
            {
                throw new ArgumentException("CURL trống");
            }

            var tokens = Tokenize(curlText);

            // Remove leading 'curl'
            if (tokens.Count > 0 && string.Equals(tokens[0], "curl", StringComparison.OrdinalIgnoreCase))
            {
                tokens.RemoveAt(0);
            }

            string? url = null;
            string? method = null;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dataParts = new List<string>();
            bool useHttp2 = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (token == "-X" || token == "--request")
                {
                    i++;
                    method = RequireValue(tokens, i, "Thiếu method sau -X/--request");
                    continue;
                }

                if (token == "-H" || token == "--header")
                {
                    i++;
                    var header = RequireValue(tokens, i, "Thiếu header sau -H/--header");
                    var sepIndex = header.IndexOf(':');
                    if (sepIndex <= 0)
                    {
                        throw new ArgumentException($"Header không hợp lệ: {header}");
                    }
                    var name = header.Substring(0, sepIndex).Trim();
                    var value = header.Substring(sepIndex + 1).Trim();
                    headers[name] = value;
                    continue;
                }

                if (token is "-d" or "--data" or "--data-raw" or "--data-binary")
                {
                    i++;
                    var data = RequireValue(tokens, i, "Thiếu dữ liệu sau -d/--data*");
                    dataParts.Add(UnescapeIfQuoted(data));
                    continue;
                }

                if (token == "--data-urlencode")
                {
                    i++;
                    var data = RequireValue(tokens, i, "Thiếu dữ liệu sau --data-urlencode");
                    dataParts.Add(EncodeDataUrlencode(data));
                    continue;
                }

                if (token is "--url")
                {
                    i++;
                    url = RequireValue(tokens, i, "Thiếu URL sau --url");
                    continue;
                }

                if (!token.StartsWith("-"))
                {
                    // Assume bare token URL
                    url ??= token;
                    continue;
                }

                // Ignore harmless flags for now
                if (token is "--compressed" or "-s" or "--silent" or "-L" or "--location" or "-k" or "--insecure")
                {
                    continue;
                }

                // Not supported currently
                if (token is "-F" or "--form")
                {
                    throw new NotSupportedException("Hiện chưa hỗ trợ multipart/form-data (-F/--form)");
                }

                if (token == "--http2")
                {
                    useHttp2 = true;
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("Không tìm thấy URL trong lệnh cURL");
            }

            var body = dataParts.Count == 0 ? null : string.Join("&", dataParts);

            if (string.IsNullOrWhiteSpace(method))
            {
                method = body is null ? "GET" : "POST";
            }

            if (!SupportedMethods.Contains(method))
            {
                throw new NotSupportedException($"HTTP method chưa hỗ trợ: {method}");
            }

            return new ParsedCurlCommand
            {
                Method = method,
                Url = url!,
                Headers = headers,
                Body = body ?? string.Empty,
                UseHttp2 = useHttp2
            };
        }

        private static string RequireValue(List<string> tokens, int index, string errorIfMissing)
        {
            if (index >= tokens.Count)
            {
                throw new ArgumentException(errorIfMissing);
            }
            return tokens[index];
        }

        private static string UnescapeIfQuoted(string text)
        {
            if (text.Length >= 2 && ((text.StartsWith("\"") && text.EndsWith("\"")) || (text.StartsWith("'") && text.EndsWith("'"))))
            {
                return text.Substring(1, text.Length - 2);
            }
            return text;
        }

        private static string EncodeDataUrlencode(string text)
        {
            var unquoted = UnescapeIfQuoted(text);
            var eqIndex = unquoted.IndexOf('=');
            if (eqIndex >= 0)
            {
                var name = unquoted.Substring(0, eqIndex);
                var value = unquoted.Substring(eqIndex + 1);
                return name + "=" + System.Uri.EscapeDataString(value);
            }
            // No '=', encode the entire token
            return System.Uri.EscapeDataString(unquoted);
        }

        private static List<string> Tokenize(string input)
        {
            // Handle line continuations \\\n+            input = Regex.Replace(input, "\\\\\\r?\\n", " ");

            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '\\' && i + 1 < input.Length)
                {
                    // Preserve escapes inside quotes as-is
                    if (inSingle)
                    {
                        // In single quotes, backslash is literal
                    }
                    else if (inDouble)
                    {
                        i++;
                        current.Append(input[i]);
                        continue;
                    }
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }
                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (!inSingle && !inDouble && char.IsWhiteSpace(c))
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
            }

            return tokens;
        }
    }
}


