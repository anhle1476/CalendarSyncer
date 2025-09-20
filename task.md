# Task Implementation Plan - CalendarSyncService

## Phase 1: Thiết lập cơ bản Windows Service (Tuần 1) - **HOÀN THÀNH**

### Tuần 1, Ngày 1-2:
- [ ] Tạo project Worker Service trong .NET
  - [ ] Sử dụng Visual Studio hoặc CLI: `dotnet new worker -n CalendarSyncService`
  - [ ] Cấu trúc thư mục và project files
  - [ ] Xem xét sử dụng .NET 6+ cho tính năng hiện đại
- [ ] Cấu hình Windows Service để chạy dưới nền
  - [ ] Thêm package Microsoft.Extensions.Hosting.WindowsServices
  - [ ] Cấu hình HostBuilder để chạy như Windows Service
  - [ ] Kiểm tra khả năng chạy dưới nền

### Tuần 1, Ngày 3-4:
- [ ] Thiết lập cơ chế logging cơ bản
  - [ ] Cấu hình logging trong Program.cs
  - [ ] Thêm file logging provider
  - [ ] Kiểm tra logging hoạt động đúng
- [ ] Thêm cấu hình qua appsettings.json
  - [ ] Tạo cấu trúc appsettings.json cơ bản
  - [ ] Đọc cấu hình trong Program.cs
  - [ ] Kiểm tra binding cấu hình đúng

### Tuần 1, Ngày 5:
- [ ] Kiểm thử service chạy dưới nền
  - [ ] Build project và kiểm tra output
  - [ ] Chạy thử service ở chế độ console
  - [ ] Debug và fix lỗi nếu có
- [ ] Document kết quả giai đoạn 1
  - [ ] Ghi chú các vấn đề gặp phải
  - [ ] Cập nhật tài liệu cho các bước tiếp theo

## Phase 2: Tích hợp Google Calendar API (Tuần 2)

### Tuần 2, Ngày 1:
- [ ] Tạo Service Account trong Google Cloud Console
  - [ ] Truy cập Google Cloud Console
  - [ ] Tạo project mới hoặc sử dụng project hiện có
  - [ ] Enable Google Calendar API
  - [ ] Tạo Service Account và tải JSON key file
- [ ] Cấp quyền truy cập Google Calendar
  - [ ] Nếu dùng Google Workspace: Cấu hình Domain-wide Delegation
  - [ ] Nếu không: Chia sẻ calendar với service account email

### Tuần 2, Ngày 2-3:
- [ ] Tích hợp thư viện Google.Apis.Calendar.v3
  - [ ] Thêm NuGet packages cần thiết:
    - [ ] Google.Apis.Calendar.v3
    - [ ] Google.Apis.Auth
  - [ ] Kiểm tra phiên bản mới nhất và tương thích
- [ ] Xây dựng lớp truy cập Google Calendar API
  - [ ] Tạo GoogleCalendarService.cs
  - [ ] Hiện thực phương thức xác thực
  - [ ] Tạo phương thức lấy danh sách sự kiện

### Tuần 2, Ngày 4-5:
- [ ] Xử lý xác thực Service Account
  - [ ] Hiện thực logic đọc JSON key file
  - [ ] Tạo credential với scope phù hợp
  - [ ] Khởi tạo CalendarService
- [ ] Kiểm thử truy vấn dữ liệu từ Google Calendar
  - [ ] Viết unit test cho GoogleCalendarService
  - [ ] Kiểm tra kết nối và truy vấn sự kiện
  - [ ] Xử lý lỗi xác thực nếu có

## Phase 3: Thiết kế cơ sở dữ liệu và đồng bộ (Tuần 3)

### Tuần 3, Ngày 1:
- [ ] Thiết kế schema SQL Server
  - [ ] Xác định các cột cần thiết cho CalendarEvents
  - [ ] Thiết kế bảng CalendarSyncState
  - [ ] Xem xét các kiểu dữ liệu phù hợp (DATETIME, NVARCHAR, JSON)
- [ ] Tạo bảng CalendarEvents và CalendarSyncState
  - [ ] Viết script CREATE TABLE
  - [ ] Tạo database (nếu chưa có)
  - [ ] Thực thi script tạo bảng

### Tuần 3, Ngày 2:
- [ ] Xây dựng lớp truy cập dữ liệu (Data Access Layer)
  - [ ] Tạo DatabaseService.cs
  - [ ] Hiện thực kết nối database
  - [ ] Tạo các phương thức CRUD cơ bản

### Tuần 3, Ngày 3:
- [ ] Hiện thực cơ chế full sync ban đầu
  - [ ] Tạo phương thức sync full events
  - [ ] Xử lý chèn dữ liệu mới vào DB
  - [ ] Kiểm tra dữ liệu được lưu đúng

### Tuần 3, Ngày 4:
- [ ] Hiện thực cơ chế incremental sync với syncToken
  - [ ] Thêm logic lưu và đọc syncToken
  - [ ] Tạo phương thức incremental sync
  - [ ] Kiểm tra cơ chế chỉ lấy sự kiện thay đổi

### Tuần 3, Ngày 5:
- [ ] Xử lý các trường hợp thêm/sửa/xóa sự kiện
  - [ ] Hiện thực logic xử lý event status cancelled
  - [ ] Cập nhật sự kiện đã tồn tại
  - [ ] Chèn sự kiện mới
- [ ] Kiểm thử đồng bộ dữ liệu
  - [ ] Test các trường hợp thêm/sửa/xóa
  - [ ] Kiểm tra tính toàn vẹn dữ liệu

## Phase 4: Thông báo UDP (Tuần 4)

### Tuần 4, Ngày 1-2:
- [ ] Xây dựng module gửi thông báo UDP
  - [ ] Tạo UdpNotificationService.cs
  - [ ] Hiện thực phương thức gửi UDP packet
  - [ ] Xử lý serialization dữ liệu gửi đi

### Tuần 4, Ngày 3:
- [ ] Tích hợp gửi thông báo khi có thay đổi sự kiện
  - [ ] Gọi UdpNotificationService từ DatabaseService
  - [ ] Gửi thông báo khi thêm/sửa/xóa sự kiện
  - [ ] Xử lý lỗi khi gửi UDP

### Tuần 4, Ngày 4:
- [ ] Cấu hình UDP endpoint trong appsettings.json
  - [ ] Thêm cấu hình UdpHost và UdpPort
  - [ ] Đọc cấu hình trong UdpNotificationService
- [ ] Kiểm thử cơ chế thông báo
  - [ ] Tạo ứng dụng UDP listener để test
  - [ ] Kiểm tra thông báo được gửi đúng
  - [ ] Xử lý lỗi mạng nếu có

## Phase 5: Tối ưu hóa và push notifications (Tuần 5)

### Tuần 5, Ngày 1-2:
- [ ] Hiện thực cơ chế push notifications từ Google Calendar (nếu khả thi)
  - [ ] Nghiên cứu Events.Watch API
  - [ ] Xem xét yêu cầu HTTPS public endpoint
  - [ ] Hiện thực nếu khả thi, nếu không thì dùng polling

### Tuần 5, Ngày 3:
- [ ] Tối ưu hóa hiệu năng (batch processing, multi-threading)
  - [ ] Xem xét sử dụng Parallel.ForEach cho xử lý nhiều sự kiện
  - [ ] Tối ưu hóa truy vấn database
  - [ ] Xem xét bulk insert/update

### Tuần 5, Ngày 4:
- [ ] Xử lý lỗi và retry mechanism
  - [ ] Thêm cơ chế retry với exponential backoff
  - [ ] Xử lý các loại lỗi thường gặp
  - [ ] Logging chi tiết lỗi

### Tuần 5, Ngày 5:
- [ ] Kiểm thử toàn bộ flow
  - [ ] Test integration giữa các thành phần
  - [ ] Kiểm tra hiệu năng
  - [ ] Xử lý lỗi phát sinh

## Phase 6: Bảo mật và đóng gói (Tuần 6)

### Tuần 6, Ngày 1:
- [ ] Bảo mật file cấu hình và key
  - [ ] Xem xét mã hóa key file
  - [ ] Sử dụng User Secrets cho môi trường dev
  - [ ] Hạn chế quyền truy cập file cấu hình

### Tuần 6, Ngày 2:
- [ ] Xử lý lỗi và logging chi tiết
  - [ ] Thêm structured logging
  - [ ] Ghi log vào Windows Event Log
  - [ ] Xử lý exception handling đầy đủ

### Tuần 6, Ngày 3:
- [ ] Đóng gói thành file thực thi đơn
  - [ ] Cấu hình publish single file
  - [ ] Build bản release
  - [ ] Kiểm tra file chạy độc lập

### Tuần 6, Ngày 4:
- [ ] Tạo installer (MSI) hoặc script cài đặt
  - [ ] Tạo script cài đặt service
  - [ ] Hoặc tạo MSI installer
  - [ ] Kiểm tra cài đặt và chạy service

### Tuần 6, Ngày 5:
- [ ] Viết tài liệu hướng dẫn
  - [ ] Hướng dẫn cài đặt
  - [ ] Hướng dẫn cấu hình
  - [ ] Hướng dẫn troubleshooting
- [ ] Tổng kết và bàn giao
  - [ ] Kiểm tra lại toàn bộ chức năng
  - [ ] Đảm bảo code quality
  - [ ] Chuẩn bị cho phase tiếp theo (UI nếu cần)