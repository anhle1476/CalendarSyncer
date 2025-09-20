# Kế hoạch chi tiết phát triển Windows Service đồng bộ Google Calendar và SQL Server

## 1. Tổng quan kiến trúc

Windows Service sẽ thực hiện các chức năng chính:
- Đồng bộ sự kiện từ Google Calendar vào SQL Server
- Gửi thông báo thay đổi qua UDP
- Hỗ trợ cả polling định kỳ và push notifications (nếu có thể)

## 2. Các thành phần chính

### 2.1. Windows Service (.NET Core/5+ Worker Service)
- Sử dụng `WorkerService` template trong .NET
- Chạy dưới nền như một dịch vụ Windows
- Thực hiện đồng bộ định kỳ hoặc nhận push notifications

### 2.2. Xác thực Google Calendar API
- Sử dụng Service Account với JSON key file
- Cấp quyền truy cập qua Domain-wide Delegation (nếu dùng Google Workspace)
- Thư viện: `Google.Apis.Calendar.v3`

### 2.3. Đồng bộ dữ liệu
- Full sync ban đầu
- Incremental sync với `syncToken`
- Xử lý sự kiện thêm/sửa/xóa
- Lưu trữ trong SQL Server

### 2.4. Cơ sở dữ liệu SQL Server
- Bảng `CalendarEvents` lưu sự kiện
- Bảng `CalendarSyncState` lưu trạng thái đồng bộ

### 2.5. Thông báo UDP
- Gửi ID sự kiện qua UDP khi có thay đổi
- Sử dụng `UdpClient` trong .NET

### 2.6. Cấu hình
- File `appsettings.json`
- Cấu hình: kết nối DB, thông tin Google API, tần suất sync, UDP endpoint

## 3. Kế hoạch triển khai chi tiết

### 3.1. Giai đoạn 1: Thiết lập cơ bản Windows Service

**Tuần 1:**
1. Tạo project Worker Service trong .NET
2. Cấu hình Windows Service để chạy dưới nền
3. Thiết lập cơ chế logging cơ bản
4. Thêm cấu hình qua appsettings.json
5. Kiểm thử service chạy dưới nền

**Công nghệ sử dụng:**
- .NET 6+ Worker Service
- Microsoft.Extensions.Hosting
- System.Text.Json

### 3.2. Giai đoạn 2: Tích hợp Google Calendar API

**Tuần 2:**
1. Tạo Service Account trong Google Cloud Console
2. Cấp quyền truy cập Google Calendar
3. Tích hợp thư viện Google.Apis.Calendar.v3
4. Xây dựng lớp truy cập Google Calendar API
5. Xử lý xác thực Service Account
6. Kiểm thử truy vấn dữ liệu từ Google Calendar

**Công nghệ sử dụng:**
- Google.Apis.Calendar.v3
- Google.Apis.Auth
- Service Account JSON key

### 3.3. Giai đoạn 3: Thiết kế cơ sở dữ liệu và đồng bộ

**Tuần 3:**
1. Thiết kế schema SQL Server
2. Tạo bảng CalendarEvents và CalendarSyncState
3. Xây dựng lớp truy cập dữ liệu (Data Access Layer)
4. Hiện thực cơ chế full sync ban đầu
5. Hiện thực cơ chế incremental sync với syncToken
6. Xử lý các trường hợp thêm/sửa/xóa sự kiện
7. Kiểm thử đồng bộ dữ liệu

**Công nghệ sử dụng:**
- SQL Server
- ADO.NET hoặc Entity Framework Core
- System.Data.SqlClient hoặc Microsoft.Data.SqlClient

### 3.4. Giai đoạn 4: Thông báo UDP

**Tuần 4:**
1. Xây dựng module gửi thông báo UDP
2. Tích hợp gửi thông báo khi có thay đổi sự kiện
3. Cấu hình UDP endpoint trong appsettings.json
4. Kiểm thử cơ chế thông báo

**Công nghệ sử dụng:**
- System.Net.Sockets.UdpClient

### 3.5. Giai đoạn 5: Tối ưu hóa và push notifications

**Tuần 5:**
1. Hiện thực cơ chế push notifications từ Google Calendar (nếu khả thi)
2. Tối ưu hóa hiệu năng (batch processing, multi-threading)
3. Xử lý lỗi và retry mechanism
4. Kiểm thử toàn bộ flow

**Công nghệ sử dụng:**
- Google Calendar API push notifications
- Hệ thống HTTPS public endpoint (nếu cần)

### 3.6. Giai đoạn 6: Bảo mật và đóng gói

**Tuần 6:**
1. Bảo mật file cấu hình và key
2. Xử lý lỗi và logging chi tiết
3. Đóng gói thành file thực thi đơn
4. Tạo installer (MSI) hoặc script cài đặt
5. Viết tài liệu hướng dẫn

## 4. Chi tiết kỹ thuật từng thành phần

### 4.1. Windows Service

**Các lớp chính:**
- `Program.cs`: Entry point, cấu hình Dependency Injection
- `Worker.cs`: Lớp xử lý chính, thực hiện đồng bộ định kỳ
- `CalendarSyncService.cs`: Dịch vụ đồng bộ chính
- `GoogleCalendarService.cs`: Truy cập Google Calendar API
- `DatabaseService.cs`: Truy cập cơ sở dữ liệu
- `UdpNotificationService.cs`: Gửi thông báo UDP

**Cơ chế hoạt động:**
```
Worker (Timer) 
    → CalendarSyncService (Điều phối)
        → GoogleCalendarService (Lấy dữ liệu)
        → DatabaseService (Lưu trữ)
        → UdpNotificationService (Thông báo)
```

### 4.2. Google Calendar API Integration

**Xác thực:**
```csharp
// Load service account key
using var stream = new FileStream("service-account.json", FileMode.Open, FileAccess.Read);
GoogleCredential credential = GoogleCredential.FromStream(stream)
                             .CreateScoped(new[] { 
                                 CalendarService.Scope.Calendar, 
                                 CalendarService.Scope.CalendarEvents 
                             });

// Create Calendar service
var service = new CalendarService(new BaseClientService.Initializer() {
    HttpClientInitializer = credential,
    ApplicationName = "CalendarSyncService"
});
```

**Đồng bộ:**
```csharp
// Full sync
var request = service.Events.List(calendarId);
request.TimeMin = DateTime.Now.AddDays(-30);
var events = request.Execute();

// Incremental sync
var request = service.Events.List(calendarId);
request.SyncToken = lastSyncToken;
var events = request.Execute();
```

### 4.3. Cơ sở dữ liệu

**Schema:**
```sql
-- Bảng sự kiện
CREATE TABLE CalendarEvents (
    EventID NVARCHAR(100) PRIMARY KEY,
    CalendarID NVARCHAR(100),
    Summary NVARCHAR(500),
    Description NVARCHAR(MAX),
    StartTime DATETIME,
    EndTime DATETIME,
    CreatedTime DATETIME,
    UpdatedTime DATETIME,
    Location NVARCHAR(200),
    Status NVARCHAR(50),
    OrganizerEmail NVARCHAR(100),
    Attendees NVARCHAR(MAX),
    Recurrence NVARCHAR(MAX),
);

-- Bảng trạng thái đồng bộ
CREATE TABLE CalendarSyncState (
    CalendarID NVARCHAR(100) PRIMARY KEY,
    LastSyncToken NVARCHAR(200),
    LastSyncTime DATETIME
);
```

### 4.4. Thông báo UDP

**Gửi thông báo:**
```csharp
UdpClient udpClient = new UdpClient();
IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 11004);
byte[] sendBytes = Encoding.UTF8.GetBytes("eventId123");
udpClient.Send(sendBytes, sendBytes.Length, ipEndPoint);
```

### 4.5. Cấu hình

**appsettings.json:**
```json
{
  "Google": {
    "ServiceAccountKeyPath": "C:\\keys\\calendar-sa.json",
    "CalendarId": "primary"
  },
  "Database": {
    "ConnectionString": "Server=myserver;Database=CalendarDB;Trusted_Connection=True;"
  },
  "Sync": {
    "IntervalMinutes": 5
  },
  "Notification": {
    "UdpHost": "192.168.1.100",
    "UdpPort": 11004
  }
}
```

## 5. Xử lý lỗi và trường hợp đặc biệt

### 5.1. Token hết hạn (HTTP 410)
- Khi nhận HTTP 410 "Sync token expired"
- Xóa token cũ, thực hiện full sync lại

### 5.2. Lỗi kết nối
- Retry mechanism với backoff exponential
- Logging chi tiết lỗi

### 5.3. Quota limit
- Theo dõi quota usage
- Điều chỉnh tần suất sync phù hợp

### 5.4. Sự kiện lặp lại (Recurring events)
- Lưu nguyên cấu trúc recurrence
- Xử lý theo nhu cầu thực tế

## 6. Bảo mật

### 6.1. Service Account Key
- Lưu trữ key file ở vị trí an toàn
- Không commit lên source control

### 6.2. Kết nối cơ sở dữ liệu
- Sử dụng connection string an toàn
- Tránh SQL injection bằng parameterized queries

### 6.3. UDP
- Chỉ gửi ID sự kiện, không gửi dữ liệu nhạy cảm
- Có thể lọc port/host trong firewall

## 7. Triển khai và đóng gói

### 7.1. Đóng gói
```bash
dotnet publish -r win-x64 -c Release /p:PublishSingleFile=true
```

### 7.2. Cài đặt service
```bash
sc create CalendarSyncService binpath= "C:\Services\CalendarSyncService.exe"
```

### 7.3. Quản lý service
- Start/Stop qua Services.msc
- Logging vào Windows Event Log hoặc file riêng

## 8. Mở rộng trong tương lai

1. **Hỗ trợ nhiều calendar**: Cho phép cấu hình nhiều Calendar ID
2. **Đồng bộ hai chiều**: Từ SQL Server → Google Calendar
3. **UI đơn giản**: Ứng dụng MFC C++ để giám sát service
4. **Webhook receiver**: Nhận push notifications từ Google
5. **Monitoring**: Tích hợp Application Insights hoặc Prometheus