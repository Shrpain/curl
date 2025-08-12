namespace curl.Models
{
    public class SicboRowViewModel
    {
        public string Guess { get; set; } = string.Empty;           // Đoán
        public string Correctness { get; set; } = string.Empty;     // Đ/S
        public string History { get; set; } = string.Empty;         // Lịch Sử
        public int Points { get; set; }                              // Điểm
        public string TaiXiu { get; set; } = string.Empty;          // Tài/Xỉu
        public string ChanLe { get; set; } = string.Empty;          // Chẵn/Lẻ
        public string SoBao { get; set; } = string.Empty;           // Số Bão
        public string MaVan { get; set; } = string.Empty;           // Mã ván
    }

    public class SicboSourceItem
    {
        public string? Issue { get; set; }      // Mã ván
        public string? WinNumber { get; set; }  // ví dụ "1,4,4"
    }
}


