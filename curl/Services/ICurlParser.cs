using curl.Models;

namespace curl.Services
{
    public interface ICurlParser
    {
        ParsedCurlCommand Parse(string curlText);
    }
}


