using System.Text;
using System.Text.Json;
using curl.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using curl.Services;
using System.IO.Compression;

namespace curl.Controllers
{
    public class SicboController : Controller
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ICurlParser _curlParser;

        public SicboController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ICurlParser curlParser)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _curlParser = curlParser;
        }

        [HttpGet]
        public IActionResult Index()
        {
            var rows = new List<SicboRowViewModel>();
            return View(rows);
        }

        /// <summary>
        /// Lấy JSON nguồn và chuyển đổi thành bảng SICBO theo quy tắc
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Fetch(CancellationToken cancellationToken)
        {
            var sourceUrl = _configuration["Sicbo:SourceUrl"]; // GET JSON nếu có URL
            var curlText = _configuration["Sicbo:CurlText"];   // Hoặc chạy lệnh cURL cấu hình

            string json;
            using var client = _httpClientFactory.CreateClient();

            if (!string.IsNullOrWhiteSpace(sourceUrl) && !sourceUrl.Contains("example.com", StringComparison.OrdinalIgnoreCase))
            {
                using var resp = await client.GetAsync(sourceUrl, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    return StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(cancellationToken));
                }
                json = await ReadStringHandlingEncoding(resp, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(curlText))
            {
                // Thực thi lệnh cURL đã cấu hình
                var parsed = _curlParser.Parse(curlText);
                using var httpRequest = new HttpRequestMessage(new HttpMethod(parsed.Method), parsed.Url);
                if (parsed.UseHttp2)
                {
                    httpRequest.Version = new Version(2, 0);
                    httpRequest.VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
                }
                foreach (var header in parsed.Headers)
                {
                    if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                if (!string.IsNullOrEmpty(parsed.Body))
                {
                    httpRequest.Content = new StringContent(parsed.Body, System.Text.Encoding.UTF8);
                }
                if (httpRequest.Content == null && parsed.Headers.Keys.Any(k => k.StartsWith("Content-", StringComparison.OrdinalIgnoreCase)))
                {
                    httpRequest.Content = new ByteArrayContent(Array.Empty<byte>());
                }
                if (httpRequest.Content != null)
                {
                    if (parsed.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        httpRequest.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                    }
                    else if (!string.IsNullOrEmpty(parsed.Body))
                    {
                        httpRequest.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded; charset=utf-8");
                    }
                    foreach (var header in parsed.Headers)
                    {
                        var headerName = header.Key;
                        if (headerName.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && !headerName.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            httpRequest.Content.Headers.TryAddWithoutValidation(headerName, header.Value);
                        }
                    }
                }
                using var resp = await client.SendAsync(httpRequest, cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    return StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(cancellationToken));
                }
                json = await ReadStringHandlingEncoding(resp, cancellationToken);
            }
            else
            {
                return BadRequest("Chưa cấu hình Sicbo:SourceUrl hoặc Sicbo:CurlText trong appsettings.json");
            }

            var items = ExtractSicboItems(json);

            // Đếm Bão theo chuỗi, reset khi gặp triple (tính lại từ đầu mỗi lần fetch)
            var result = new List<SicboRowViewModel>();
            int baoCounter = 0;

            foreach (var it in items)
            {
                // Lấy đúng 3 chữ số (bỏ mọi ký tự không phải số)
                var onlyDigits = new string((it.WinNumber ?? string.Empty).Where(char.IsDigit).ToArray());
                int d1 = onlyDigits.Length >= 1 ? onlyDigits[0] - '0' : 0;
                int d2 = onlyDigits.Length >= 2 ? onlyDigits[1] - '0' : 0;
                int d3 = onlyDigits.Length >= 3 ? onlyDigits[2] - '0' : 0;
                int sum = d1 + d2 + d3;

                string taiXiu = sum >= 11 && sum <= 18 ? "Tài" : "Xỉu"; // 3-10 Xỉu, 11-18 Tài
                string chanLe = (sum % 2 == 0) ? "Chẵn" : "Lẻ";

                string soBao;
                if (onlyDigits.Length == 3 && d1 == d2 && d2 == d3 && d1 != 0)
                {
                    soBao = "Bão";
                    baoCounter = 0; // reset sau khi ghi nhận Bão
                }
                else
                {
                    baoCounter += 1;
                    soBao = baoCounter.ToString();
                }

                result.Add(new SicboRowViewModel
                {
                    Guess = string.Empty,
                    Correctness = string.Empty,
                    History = string.IsNullOrWhiteSpace(onlyDigits) ? it.WinNumber ?? string.Empty : onlyDigits,
                    Points = sum,
                    TaiXiu = taiXiu,
                    ChanLe = chanLe,
                    SoBao = soBao,
                    MaVan = it.Issue ?? string.Empty
                });
            }

            // Sắp xếp từ cũ nhất đến mới nhất theo Mã ván (ưu tiên số nếu parse được)
            var ordered = result
                .OrderBy(r => long.TryParse(r.MaVan, out var n) ? n : long.MaxValue)
                .ThenBy(r => r.MaVan, StringComparer.Ordinal)
                .ToList();

            return Json(ordered);
        }

        private static List<SicboSourceItem> ExtractSicboItems(string json)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            try
            {
                // Trường hợp JSON đã đúng định dạng List<SicboSourceItem>
                var direct = JsonSerializer.Deserialize<List<SicboSourceItem>>(json, options);
                if (direct != null && direct.Count > 0)
                {
                    return direct;
                }
            }
            catch { /* ignore and try generic parse */ }

            var results = new List<SicboSourceItem>();
            using var doc = JsonDocument.Parse(json);

            // Tìm mảng đối tượng có field issue & winNumber ở bất cứ cấp nào
            void ScanElement(JsonElement el)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.Array:
                        foreach (var child in el.EnumerateArray())
                        {
                            if (child.ValueKind == JsonValueKind.Object)
                            {
                                string? issue = null;
                                string? win = null;
                                foreach (var p in child.EnumerateObject())
                                {
                                    var name = p.Name.ToLowerInvariant();
                                    if (name == "issue") issue = p.Value.ToString();
                                    else if (name == "winnumber" || name == "win_number") win = p.Value.ToString();
                                }
                                if (!string.IsNullOrEmpty(issue) || !string.IsNullOrEmpty(win))
                                {
                                    results.Add(new SicboSourceItem { Issue = issue, WinNumber = win });
                                }
                            }
                        }
                        break;
                    case JsonValueKind.Object:
                        foreach (var p in el.EnumerateObject())
                        {
                            ScanElement(p.Value);
                        }
                        break;
                }
            }

            ScanElement(doc.RootElement);
            return results;
        }

        private static async Task<string> ReadStringHandlingEncoding(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            Stream toRead = responseStream;
            var encodings = response.Content.Headers.ContentEncoding?.ToList() ?? new List<string>();
            if (encodings.Count > 0)
            {
                for (int i = encodings.Count - 1; i >= 0; i--)
                {
                    var enc = encodings[i].Trim().ToLowerInvariant();
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
                        break;
                    }
                }
            }

            using var memory = new MemoryStream();
            await toRead.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            var bytes = memory.ToArray();
            return Encoding.UTF8.GetString(bytes);
        }
    }
}


