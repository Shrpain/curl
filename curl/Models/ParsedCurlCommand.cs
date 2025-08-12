namespace curl.Models
{
    public class ParsedCurlCommand
    {
        public string Method { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string Body { get; set; } = string.Empty;
        public bool UseHttp2 { get; set; }
    }
}


