using System.Net.Http.Headers;
using System.Text;
using curl.Models;
using curl.Services;
using Microsoft.AspNetCore.Mvc;
using System.IO.Compression;

namespace curl.Controllers
{
    public class CurlController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ICurlParser _curlParser;
        private const int ResponseBodyDisplayLimitBytes = 1024 * 1024; // 1MB

        public CurlController(IHttpClientFactory httpClientFactory, ICurlParser curlParser)
        {
            _httpClientFactory = httpClientFactory;
            _curlParser = curlParser;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var model = new CurlExecuteViewModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(CurlExecuteViewModel model, CancellationToken cancellationToken)
        {
            model ??= new CurlExecuteViewModel();

            if (string.IsNullOrWhiteSpace(model.CurlText))
            {
                ModelState.AddModelError(nameof(model.CurlText), "Vui lòng nhập lệnh cURL");
                return View(model);
            }

            try
            {
                var parsed = _curlParser.Parse(model.CurlText);
                model.ParsedCommand = parsed;

                using var httpClient = _httpClientFactory.CreateClient();
                using var httpRequest = new HttpRequestMessage(new HttpMethod(parsed.Method), parsed.Url);
                if (parsed.UseHttp2)
                {
                    httpRequest.Version = new Version(2, 0);
                    httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
                }

                foreach (var header in parsed.Headers)
                {
                    // Skip content-specific headers here; they belong to HttpContent
                    if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                if (!string.IsNullOrEmpty(parsed.Body))
                {
                    httpRequest.Content = new StringContent(parsed.Body, Encoding.UTF8);
                }

                // If there are Content-* headers but no content yet, create empty content to host them
                if (httpRequest.Content == null && parsed.Headers.Keys.Any(k => k.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)))
                {
                    httpRequest.Content = new ByteArrayContent(Array.Empty<byte>());
                }

                if (httpRequest.Content != null)
                {
                    // Apply Content-Type if provided
                    if (parsed.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        httpRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    }
                    else if (!string.IsNullOrEmpty(parsed.Body))
                    {
                        // Mimic curl default when using -d/--data without explicit Content-Type
                        httpRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=utf-8");
                    }

                    // Apply other Content-* headers
                    foreach (var header in parsed.Headers)
                    {
                        var headerName = header.Key;
                        if (headerName.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                        {
                            if (headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            httpRequest.Content.Headers.TryAddWithoutValidation(headerName, header.Value);
                        }
                    }
                }

                using var httpResponse = await httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                model.ResponseStatus = (int)httpResponse.StatusCode + " " + httpResponse.StatusCode;

                var headerLines = new List<string>();
                foreach (var h in httpResponse.Headers)
                {
                    headerLines.Add($"{h.Key}: {string.Join(", ", h.Value)}");
                }
                foreach (var h in httpResponse.Content.Headers)
                {
                    headerLines.Add($"{h.Key}: {string.Join(", ", h.Value)}");
                }
                model.ResponseHeaders = string.Join("\n", headerLines);

                await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
                Stream toRead = responseStream;
                var encodings = httpResponse.Content.Headers.ContentEncoding?.ToList() ?? new List<string>();
                if (encodings.Count > 0)
                {
                    // Apply decoders in reverse order of encodings
                    for (int ei = encodings.Count - 1; ei >= 0; ei--)
                    {
                        var enc = encodings[ei].Trim().ToLowerInvariant();
                        if (enc == "gzip")
                        {
                            toRead = new GZipStream(toRead, CompressionMode.Decompress, leaveOpen: true);
                        }
                        else if (enc == "deflate")
                        {
                            toRead = new DeflateStream(toRead, CompressionMode.Decompress, leaveOpen: true);
                        }
                        else if (enc == "br")
                        {
                            toRead = new BrotliStream(toRead, CompressionMode.Decompress, leaveOpen: true);
                        }
                        else
                        {
                            // Unknown encoding; stop decoding further
                            break;
                        }
                    }
                }

                using var memory = new MemoryStream();
                await toRead.CopyToAsync(memory, cancellationToken);
                memory.Position = 0;

                var bytes = memory.ToArray();
                var toDisplay = bytes.LongLength > ResponseBodyDisplayLimitBytes
                    ? ResponseBodyDisplayLimitBytes
                    : bytes.LongLength;

                var rawText = Encoding.UTF8.GetString(bytes, 0, (int)toDisplay);
                // Try pretty print JSON
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(rawText);
                    model.ResponseBody = System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
                catch
                {
                    model.ResponseBody = rawText;
                }

                if (bytes.LongLength > ResponseBodyDisplayLimitBytes)
                {
                    model.ResponseBody += $"\n\n--- truncated at {ResponseBodyDisplayLimitBytes} bytes ---";
                }
            }
            catch (Exception ex)
            {
                model.ErrorMessage = ex.Message;
            }

            return View(model);
        }
    }
}


