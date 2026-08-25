# Mon - Total Volume & Delta Footprint (ATAS Custom Indicator)

Chỉ báo Custom Footprint cao cấp dành cho nền tảng giao dịch **ATAS Platform**, tối ưu hóa việc phân tích khối lượng vào lệnh theo từng mức giá (Order Flow / Volume Footprint) với các tính năng vượt trội:

---

## 🌟 Tính Năng Nổi Bật

1. **Hiển thị Footprint Kép (Total Volume & Delta):**
   - Bên trái ô giá: **Total Volume** (tổng khối lượng giao dịch tại mức giá).
   - Bên phải ô giá: **Delta** (sự chênh lệch Ask Volume - Bid Volume).
   - Phân cấp màu sắc cảnh báo Volume đột biến (**Cam / Tím**).
   - Làm nổi bật đường viền mức giá có khối lượng lớn nhất (**POC - Point of Control**).

2. **Nến Mỏng ở Giữa (Middle Candle):**
   - Hiển thị thân nến (Body) và bóng nến (Wicks) màu sắc trực quan (Bullish / Bearish) ở vạch ngăn giữa hai ô footprint.
   - Hỗ trợ bật/tắt (`Show Candle in Middle`) hoàn toàn ở mọi mức độ zoom.

3. **Ticks Grouping (Gom Mức Giá):**
   - Cho phép gom nhiều tick giá vào 1 ô (`Ticks Grouping = 1, 2, 4...`) để biểu đồ tinh gọn và dễ nhìn hơn trên các thị trường biến động mạnh như NQ (Nasdaq), ES (S&P 500).

4. **Right-side Volume & Delta Profile (Histogram Phải):**
   - Histogram đứng ở mép phải biểu đồ, tổng hợp toàn bộ Volume (độ dài thanh) và Delta (màu sắc Xanh/Đỏ) của các nến đang nhìn thấy trên màn hình.

5. **Bảng Thống Kê 3 Hàng Dưới Đáy (Bottom Stats Table):**
   - Hàng 1: **Delta** của từng cây nến.
   - Hàng 2: **CD Day** (Cumulative Delta lũy kế trong ngày, tự động reset theo phiên giao dịch mới).
   - Hàng 3: **Candle Vol** (Tổng Volume của cây nến).
   - Thẻ nhãn cố định ở góc phải (`Sticky Labels Card`).
   - Tự động ẩn chữ thông minh (`Text Auto-Hide`) khi zoom out để chống đè số.

6. **Tối Ưu Zoom Out (`Min Candle Width for Footprint Text`):**
   - Tự động chuyển sang chế độ nến mỏng khi thu nhỏ biểu đồ (độ rộng nến `< 35px`), chống rối mắt và đè chữ.

7. **Stacked Imbalances (Mất Cân Bằng Khối Lượng):**
   - Quét mất cân bằng mua/bán theo đường chéo với các bộ lọc `Imbalance Ratio (%)`, `Imbalance Volume`, `Imbalance Range`.
   - Tùy chọn **`Line till touch`**: Tự động triệt tiêu (biến mất) đường mất cân bằng khi giá quay lại kiểm tra (mitigate).

8. **Delta Divergence (Phân Kỳ Delta - Mũi Tên Tín Hiệu & Tự Động Làm Mờ Xám Khi Bị Phủ Nhận):**
   - **Bullish Delta Divergence:** Nến tăng (Close > Open) nhưng Delta âm $\rightarrow$ Mũi tên hướng lên `▲` dưới chân nến (Hấp thụ mua).
   - **Bearish Delta Divergence:** Nến giảm (Close < Open) nhưng Delta dương $\rightarrow$ Mũi tên hướng xuống `▼` trên đầu nến (Hấp thụ bán).
   - **Cơ Chế Đánh Dấu Invalidation (Failed Divergence):**
     - Kiểm tra tức thời trong phạm vi **1 đến 2 cây nến tiếp theo** (`Invalidation Check Window`).
     - Nếu trong 1-2 nến kế tiếp, giá đục thủng **High** của nến Bearish (hoặc thủng **Low** của nến Bullish) $\rightarrow$ Mũi tên sẽ **tự động chuyển sang màu xám mờ (`InvalidatedArrowColor`)**.
     - Giúp nhận biết ngay các tín hiệu phân kỳ bị thất bại tức thì mà không cần rối mắt bởi các ký hiệu vẽ thêm, chart luôn giữ được sự tinh gọn tối đa.
   - Bộ lọc tỷ lệ % Delta co giãn thông minh: **Major Divergence** (mặc định $\ge 10\%$) và **Minor Divergence** (mặc định $\ge 2.5\%$).
   - Tùy chỉnh riêng biệt kích thước và màu sắc cho mũi tên Lớn, Nhỏ, màu xám mờ và số nến kiểm tra.

9. **Chế Độ Màu Preset (Dark Mode & Light Mode):**
   - **DarkMode Preset:** Thiết kế tối ưu cho nền đen/xám tối.
   - **LightMode Preset:** Tương phản cao, chữ Volume xám than đậm và màu sắc nét cho nền trắng sáng.
   - **Custom:** Tự do tùy biến từng mã màu.

10. **Hệ Thống Quản Lý Profiles Đa Năng:**
    - Dropdown `1) Trading Profile` nằm ngay đầu Settings để chuyển nhanh giữa 4 preset theo thanh khoản thị trường/phiên:

      | Profile | Ticks grouping | Volume tím / cam | Imbalance (ratio / range / min vol) | Delta major / minor |
      |---|---:|---:|---:|---:|
      | NQ RTH 09:30-16:00 ET | 12 | 150 / 300 | 300% / 3 / 20 | 10% / 5% |
      | NQ Overnight 18:00-09:30 ET | 8 | 60 / 120 | 300% / 3 / 8 | 10% / 5% |
      | ES RTH 09:30-16:00 ET | 4 | 300 / 600 | 300% / 3 / 40 | 10% / 5% |
      | ES Overnight 18:00-09:30 ET | 4 | 100 / 200 | 300% / 3 / 12 | 10% / 5% |

    - Hai slot còn lại là `Custom 1` và `Custom 2` để tự tinh chỉnh.
    - Các khung giờ trên dùng múi giờ **US Eastern (ET)**; hãy chọn chart/session template tương ứng trong ATAS.
    - `CD Day` reset theo đầu phiên của profile (09:30 ET cho RTH, 18:00 ET cho Overnight), thay vì reset cứng lúc 00:00.
    - Đổi tên trực tiếp tại ô `Profile Rename / Label`, tự động cập nhật ngay trên danh sách chọn `Active Profile` theo thời gian thực.
    - Lưu trữ vĩnh viễn cấu hình vào file `.cfg` trên máy.
    - Settings được gom thành 9 nhóm đánh số (Quick Setup, Theme, Footprint, POC, Middle Candle, Right Profile, Bottom Statistics, Stacked Imbalance, Delta Divergence) để tìm tham số nhanh hơn.

## Cơ sở hiệu chỉnh và giới hạn

- Stacked Imbalance luôn được tính giữa **hai mức giá raw liền kề (1 tick)**, độc lập với `Ticks Grouping` dùng để hiển thị footprint. Cách này khớp với định nghĩa diagonal Bid/Ask của ATAS.
- `300%` và 3 mức liên tiếp là baseline bảo thủ để lọc stacked imbalance mạnh. ATAS mặc định imbalance đơn là 150%, đồng thời dùng 300% trong ví dụ thực hành; đây không phải ngưỡng tối ưu cho mọi data feed.
- `|Delta| / Volume` dùng 10% (Major) và 5% (Minor) để tránh tín hiệu sign mismatch quá nhỏ. Đây là heuristic thực dụng, không phải ngưỡng đã được chứng minh phổ quát.
- Các ngưỡng Volume tím/cam và Minimum Volume là số hợp đồng tuyệt đối nên phụ thuộc bar type, timeframe, kỳ hạn hợp đồng, session và data feed. Cần walk-forward/backtest trên dữ liệu tick ATAS của chính chart đang dùng trước khi xem là thông số tối ưu.
- Tài liệu tham khảo:
  - ATAS Footprint Settings: https://help.atas.net/en/support/solutions/articles/72000606631
  - ATAS Imbalance guide: https://atas.net/blog/imbalance-trade-on-the-side-of-superior-forces/
  - Cont, Kukanov & Stoikov, *The Price Impact of Order Book Events*: https://arxiv.org/abs/1011.6402
  - Takahashi, *Interaction Between Asset Returns and Order Flow Imbalances* (E-mini S&P 500): https://doi.org/10.24677/riim.19.0_23

---

## 🛠️ Hướng Dẫn Cài Đặt & Biên Dịch

### Yêu cầu
* [.NET 8.0 SDK](https://dotnet.microsoft.com/) hoặc [.NET 9.0/10.0 SDK](https://dotnet.microsoft.com/)
* [ATAS Platform](https://atas.net/) đã cài đặt trên máy tính.

### Biên dịch dự án
Mở terminal trong thư mục dự án và chạy:
```bash
dotnet build -c Release
```

### Triển khai file DLL vào ATAS
Sao chép file `bin/Release/net10.0-windows/TotalVolDeltaFootprint.dll` vào thư mục indicators của ATAS:
```powershell
Copy-Item -Path "bin\Release\net10.0-windows\TotalVolDeltaFootprint.dll" -Destination "$env:APPDATA\ATAS\Indicators\" -Force
Copy-Item -Path "bin\Release\net10.0-windows\TotalVolDeltaFootprint.dll" -Destination "$env:APPDATA\ATAS X\Indicators\" -Force
```

Khởi động lại ATAS, nhấn `Ctrl + I`, tìm kiếm **`Mon - Total Volume & Delta Footprint`** trong nhóm **Custom** và thêm vào biểu đồ.
