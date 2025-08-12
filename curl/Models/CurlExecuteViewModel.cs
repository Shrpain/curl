namespace curl.Models
{
    public class CurlExecuteViewModel
    {
        public string? CurlText { get; set; }

        public ParsedCurlCommand? ParsedCommand { get; set; }

        public string? ResponseStatus { get; set; }
        public string? ResponseHeaders { get; set; }
        public string? ResponseBody { get; set; }

        public string? ErrorMessage { get; set; }
    }
}


