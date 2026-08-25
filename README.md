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
      | NQ RTH 09:30-16:00 ET | 12 | 150 / 300 | 280% / 2 / 20 | 10% / 2% |
      | NQ Overnight 18:00-09:30 ET | 8 | 60 / 120 | 300% / 2 / 8 | 12% / 3% |
      | ES RTH 09:30-16:00 ET | 4 | 300 / 600 | 300% / 3 / 40 | 8% / 2% |
      | ES Overnight 18:00-09:30 ET | 4 | 100 / 200 | 320% / 2 / 12 | 10% / 2.5% |

    - Hai slot còn lại là `Custom 1` và `Custom 2` để tự tinh chỉnh.
    - Các khung giờ trên dùng múi giờ **US Eastern (ET)**; hãy chọn chart/session template tương ứng trong ATAS.
    - Đổi tên trực tiếp tại ô `Profile Rename / Label`, tự động cập nhật ngay trên danh sách chọn `Active Profile` theo thời gian thực.
    - Lưu trữ vĩnh viễn cấu hình vào file `.cfg` trên máy.
    - Settings được gom thành 9 nhóm đánh số (Quick Setup, Theme, Footprint, POC, Middle Candle, Right Profile, Bottom Statistics, Stacked Imbalance, Delta Divergence) để tìm tham số nhanh hơn.

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
