# HƯỚNG DẪN CHẠY DỰ ÁN HOTJOB

**Dự án:** HotJob - Hệ thống tìm kiếm và quản lý việc làm  
**Framework:** ASP.NET Core Blazor Server (.NET 10.0)  
**Database:** MySQL  
**ORM:** Entity Framework Core 9.0  
**Cập nhật:** 2026-10-12

---

## 📋 MỤC LỤC

1. [Yêu cầu hệ thống](#1-yêu-cầu-hệ-thống)
2. [Cài đặt môi trường](#2-cài-đặt-môi-trường)
3. [Clone và cấu hình dự án](#3-clone-và-cấu-hình-dự-án)
4. [Thiết lập Database](#4-thiết-lập-database)
5. [Chạy ứng dụng](#5-chạy-ứng-dụng)
6. [Kiểm tra ứng dụng](#6-kiểm-tra-ứng-dụng)
7. [Troubleshooting](#7-troubleshooting)

---

## 1. YÊU CẦU HỆ THỐNG

### 1.1. Phần mềm bắt buộc

| Phần mềm | Version yêu cầu | Download |
|----------|-----------------|----------|
| **.NET SDK** | 10.0 hoặc mới hơn | https://dotnet.microsoft.com/download |
| **MySQL Server** | 8.0+ | https://dev.mysql.com/downloads/mysql/ |
| **Git** | Latest | https://git-scm.com/downloads |

### 1.2. IDE/Editor (Khuyến nghị)

- **Visual Studio 2022** (v17.12+) - Community/Professional/Enterprise
- **Visual Studio Code** với C# Dev Kit extension
- **JetBrains Rider** 2024.3+

### 1.3. Hệ điều hành

- ✅ Windows 10/11 (x64)
- ✅ macOS 12+ (Intel hoặc Apple Silicon)
- ✅ Linux (Ubuntu 20.04+, Fedora 36+)

---

## 2. CÀI ĐẶT MÔI TRƯỜNG

### 2.1. Cài đặt .NET 10 SDK

**Windows:**
```powershell
# Tải và chạy installer từ:
# https://dotnet.microsoft.com/download/dotnet/10.0

# Kiểm tra cài đặt
dotnet --version
# Output: 10.0.x
```

**macOS:**
```bash
# Sử dụng Homebrew
brew install dotnet-sdk

# Kiểm tra
dotnet --version
```

**Linux (Ubuntu/Debian):**
```bash
# Thêm Microsoft package repository
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# Cài đặt .NET SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0

# Kiểm tra
dotnet --version
```

### 2.2. Cài đặt MySQL Server

**Windows:**
1. Tải **MySQL Installer** từ: https://dev.mysql.com/downloads/installer/
2. Chạy installer và chọn **Developer Default**
3. Trong quá trình cài đặt:
   - **Root password:** Đặt password mạnh (ghi nhớ để dùng sau)
   - **Port:** Giữ mặc định **3306**
   - Chọn cài đặt **MySQL Workbench** để quản lý DB

**macOS:**
```bash
# Sử dụng Homebrew
brew install mysql

# Khởi động MySQL service
brew services start mysql

# Thiết lập root password
mysql_secure_installation
```

**Linux (Ubuntu):**
```bash
# Cài đặt MySQL Server
sudo apt-get update
sudo apt-get install mysql-server

# Bảo mật MySQL
sudo mysql_secure_installation

# Khởi động service
sudo systemctl start mysql
sudo systemctl enable mysql
```

### 2.3. Kiểm tra kết nối MySQL

```bash
# Đăng nhập MySQL với root
mysql -u root -p
# Nhập password đã đặt

# Trong MySQL prompt:
mysql> SELECT VERSION();
mysql> EXIT;
```

---

## 3. CLONE VÀ CÁU HÌNH DỰ ÁN

### 3.1. Clone repository

```bash
# Clone dự án từ Git
git clone https://github.com/your-repo/hotjob.git
# Hoặc nếu bạn đang ở local:
cd D:\C\user\Desktop\hotjob

# Di chuyển vào thư mục dự án
cd hotjob/HotJob/hotjob
```

### 3.2. Kiểm tra cấu trúc dự án

```bash
# Xem cấu trúc
tree -L 2
# Hoặc trên Windows:
dir

# Cấu trúc mong đợi:
# hotjob/
# ├── Components/
# ├── Data/
# ├── Models/
# ├── Services/
# ├── wwwroot/
# ├── appsettings.json
# ├── appsettings.Development.json
# ├── Program.cs
# └── hotjob.csproj
```

### 3.3. Cấu hình Connection String

**Bước 1:** Mở file `appsettings.json`

```bash
# Windows
notepad appsettings.json

# macOS/Linux
nano appsettings.json
```

**Bước 2:** Cập nhật connection string

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=hotjob;User=root;Password=YOUR_PASSWORD_HERE;"
  }
}
```

**Lưu ý:**
- Thay `YOUR_PASSWORD_HERE` bằng password MySQL root của bạn
- Nếu dùng user khác root, thay đổi `User=root` thành user đó
- Nếu MySQL chạy port khác 3306, thêm: `Port=3307;`

**Bước 3:** (Tùy chọn) Cấu hình Development environment

Tạo/chỉnh sửa `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=hotjob_dev;User=root;Password=YOUR_PASSWORD_HERE;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

### 3.4. Restore packages

```bash
# Di chuyển về thư mục chứa .csproj
cd D:\C\user\Desktop\hotjob\HotJob\hotjob

# Restore NuGet packages
dotnet restore

# Output mong đợi:
# Determining projects to restore...
# Restored D:\C\user\Desktop\hotjob\HotJob\hotjob\hotjob.csproj (in X ms).
```

---

## 4. THIẾT LẬP DATABASE

### 4.1. Cài đặt EF Core Tools

```bash
# Cài đặt global tool
dotnet tool install --global dotnet-ef

# Hoặc update nếu đã có
dotnet tool update --global dotnet-ef

# Kiểm tra
dotnet ef --version
# Output: Entity Framework Core .NET Command-line Tools 9.0.0
```

### 4.2. Tạo Database

**Cách 1: Tự động bằng Migration (Khuyến nghị)**

```bash
# Bước 1: Tạo migration đầu tiên
dotnet ef migrations add InitialCreate

# Output:
# Build started...
# Build succeeded.
# Done. To undo this action, use 'dotnet ef migrations remove'

# Bước 2: Apply migration để tạo database
dotnet ef database update

# Output:
# Build started...
# Build succeeded.
# Applying migration '20261012_InitialCreate'.
# Done.
```

**Cách 2: Thủ công bằng MySQL Workbench**

```sql
-- Mở MySQL Workbench, kết nối đến localhost
-- Chạy lệnh SQL:

CREATE DATABASE hotjob CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Sau đó chạy migration:
-- dotnet ef database update
```

### 4.3. Kiểm tra Database

```bash
# Đăng nhập MySQL
mysql -u root -p

# Trong MySQL prompt:
mysql> USE hotjob;
mysql> SHOW TABLES;

# Output mong đợi (danh sách tables):
# +----------------------------------+
# | Tables_in_hotjob                 |
# +----------------------------------+
# | Applications                     |
# | ApplicationStatusHistories       |
# | AspNetRoles                      |
# | AspNetUsers                      |
# | Candidates                       |
# | CompanyProfiles                  |
# | Educations                       |
# | Employers                        |
# | Interviews                       |
# | JobCategories                    |
# | JobCategoryMappings              |
# | JobPostings                      |
# | JobSkills                        |
# | MatchScores                      |
# | ModelTrainingLogs                |
# | Notifications                    |
# | Resumes                          |
# | ResumeSkills                     |
# | Reviews                          |
# | SavedJobs                        |
# | Skills                           |
# | WorkExperiences                  |
# +----------------------------------+

mysql> EXIT;
```

### 4.4. Seed dữ liệu (Tùy chọn)

Nếu dự án có DatabaseSeeder, chạy:

```bash
# Kiểm tra xem có class DatabaseSeeder không
# Nếu có, seed data sẽ tự động chạy khi ứng dụng khởi động lần đầu
# Hoặc có thể trigger thủ công trong Program.cs
```

---

## 5. CHẠY ỨNG DỤNG

### 5.1. Chạy bằng .NET CLI

```bash
# Di chuyển vào thư mục dự án
cd D:\C\user\Desktop\hotjob\HotJob\hotjob

# Chạy ứng dụng
dotnet run

# Output:
# Building...
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: https://localhost:5001
# info: Microsoft.Hosting.Lifetime[14]
#       Now listening on: http://localhost:5000
# info: Microsoft.Hosting.Lifetime[0]
#       Application started. Press Ctrl+C to shut down.
```

**Truy cập ứng dụng:**
- HTTPS: https://localhost:5001
- HTTP: http://localhost:5000

### 5.2. Chạy bằng Visual Studio 2022

**Bước 1:** Mở Solution
- File → Open → Project/Solution
- Chọn file `hotjob.slnx` hoặc `hotjob.csproj`

**Bước 2:** Kiểm tra cấu hình
- Đảm bảo **Debug** configuration được chọn
- Startup project: `hotjob`

**Bước 3:** Chạy
- Nhấn **F5** (Run with Debugging)
- Hoặc **Ctrl+F5** (Run without Debugging)
- Hoặc click nút ▶️ **https** trên toolbar

**Output:**
- Visual Studio sẽ tự động mở browser đến https://localhost:5001

### 5.3. Chạy bằng Visual Studio Code

**Bước 1:** Cài đặt extensions
- **C# Dev Kit** (ms-dotnettools.csdevkit)
- **C#** (ms-dotnettools.csharp)

**Bước 2:** Mở thư mục dự án
```bash
cd D:\C\user\Desktop\hotjob\HotJob\hotjob
code .
```

**Bước 3:** Tạo launch configuration

Tạo file `.vscode/launch.json`:

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch HotJob",
      "type": "coreclr",
      "request": "launch",
      "preLaunchTask": "build",
      "program": "${workspaceFolder}/bin/Debug/net10.0/hotjob.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "stopAtEntry": false,
      "serverReadyAction": {
        "action": "openExternally",
        "pattern": "\\bNow listening on:\\s+(https?://\\S+)"
      },
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  ]
}
```

Tạo file `.vscode/tasks.json`:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "command": "dotnet",
      "type": "process",
      "args": [
        "build",
        "${workspaceFolder}/hotjob.csproj",
        "/property:GenerateFullPaths=true",
        "/consoleloggerparameters:NoSummary"
      ],
      "problemMatcher": "$msCompile"
    }
  ]
}
```

**Bước 4:** Chạy
- Nhấn **F5**
- Hoặc Run → Start Debugging

### 5.4. Chạy ở môi trường Production

```bash
# Build Release
dotnet build -c Release

# Publish
dotnet publish -c Release -o ./publish

# Chạy published app
cd publish
dotnet hotjob.dll
```

---

## 6. KIỂM TRA ỨNG DỤNG

### 6.1. Kiểm tra trang chủ

Truy cập: https://localhost:5001

**Mong đợi:**
- ✅ Trang chủ load thành công
- ✅ Navigation menu hiển thị
- ✅ Không có lỗi console

### 6.2. Kiểm tra tính năng Quản lý công ty

**Đăng ký tài khoản Employer:**
1. Click "Sign In" → "Register"
2. Điền thông tin, chọn role "Employer"
3. Đăng ký thành công

**Truy cập trang quản lý:**
- Đi tới: https://localhost:5001/company/members
- Xem danh sách nhà tuyển dụng pending

### 6.3. Kiểm tra Database connection

```bash
# Xem logs
# Nếu có lỗi kết nối DB, sẽ thấy:
# Microsoft.EntityFrameworkCore.Database.Connection: Error: An error occurred...

# Kiểm tra có data không
mysql -u root -p
mysql> USE hotjob;
mysql> SELECT COUNT(*) FROM AspNetUsers;
mysql> EXIT;
```

### 6.4. Test API endpoints (nếu có)

```bash
# Nếu dự án có API controllers
curl https://localhost:5001/api/health
```

---

## 7. TROUBLESHOOTING

### 7.1. Lỗi: "Unable to connect to MySQL"

**Triệu chứng:**
```
MySql.Data.MySqlClient.MySqlException: Unable to connect to any of the specified MySQL hosts.
```

**Giải pháp:**

**1. Kiểm tra MySQL service đang chạy**
```bash
# Windows
net start MySQL80

# macOS
brew services start mysql

# Linux
sudo systemctl status mysql
```

**2. Kiểm tra connection string**
- Đảm bảo password đúng
- Kiểm tra tên database đã tạo: `hotjob`
- Kiểm tra port (mặc định 3306)

**3. Test kết nối thủ công**
```bash
mysql -u root -p -h localhost
```

### 7.2. Lỗi: "The namespace 'hotjob' already contains a definition"

**Triệu chứng:**
```
Error CS0101: The namespace 'hotjob' already contains a definition for 'Program'
```

**Giải pháp:**
```bash
# Clean solution
dotnet clean

# Remove bin and obj folders
rm -rf bin obj

# Rebuild
dotnet build
```

### 7.3. Lỗi: "dotnet ef command not found"

**Triệu chứng:**
```bash
dotnet ef
# Could not execute because the specified command or file was not found.
```

**Giải pháp:**
```bash
# Cài đặt lại EF Core tools
dotnet tool install --global dotnet-ef --version 9.0.0

# Hoặc update
dotnet tool update --global dotnet-ef

# Restart terminal sau khi cài
```

### 7.4. Lỗi: "Port 5001 is already in use"

**Triệu chứng:**
```
System.IO.IOException: Failed to bind to address https://127.0.0.1:5001
```

**Giải pháp:**

**Option 1: Kill process đang dùng port**
```bash
# Windows
netstat -ano | findstr :5001
taskkill /PID <PID> /F

# macOS/Linux
lsof -ti:5001 | xargs kill -9
```

**Option 2: Đổi port trong appsettings.json hoặc launchSettings.json**

### 7.5. Lỗi Migration: "Table already exists"

**Triệu chứng:**
```
MySql.Data.MySqlClient.MySqlException: Table 'AspNetUsers' already exists
```

**Giải pháp:**

**Option 1: Drop database và tạo lại**
```bash
mysql -u root -p
mysql> DROP DATABASE hotjob;
mysql> CREATE DATABASE hotjob CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
mysql> EXIT;

# Chạy lại migration
dotnet ef database update
```

**Option 2: Remove và tạo lại migration**
```bash
dotnet ef database update 0
dotnet ef migrations remove
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### 7.6. Lỗi: "A network-related or instance-specific error"

**Triệu chứng:**
```
A network-related or instance-specific error occurred while establishing a connection to SQL Server.
```

**Giải pháp:**
- Lỗi này xảy ra khi connection string trỏ đến SQL Server thay vì MySQL
- Kiểm tra lại connection string phải có `Server=localhost` chứ KHÔNG phải `Data Source=...`

---

## 8. CHẠY UNIT TESTS

### 8.1. Tạo Test Project (nếu chưa có)

```bash
# Di chuyển về thư mục solution
cd D:\C\user\Desktop\hotjob\HotJob

# Tạo xUnit test project
dotnet new xunit -n hotjob.Tests

# Add reference đến project chính
cd hotjob.Tests
dotnet add reference ../hotjob/hotjob.csproj

# Add Moq package
dotnet add package Moq
dotnet add package Microsoft.EntityFrameworkCore.InMemory
```

### 8.2. Chạy Tests

```bash
# Chạy tất cả tests
dotnet test

# Chạy với verbose output
dotnet test --verbosity detailed

# Chạy tests và tạo coverage report
dotnet test /p:CollectCoverage=true
```

---

## 9. DEVELOPMENT WORKFLOW

### 9.1. Quy trình làm việc hàng ngày

```bash
# 1. Pull code mới nhất
git pull origin main

# 2. Restore packages (nếu có thay đổi)
dotnet restore

# 3. Apply migrations mới (nếu có)
dotnet ef database update

# 4. Chạy ứng dụng
dotnet run

# 5. Sau khi code xong, chạy tests
dotnet test

# 6. Commit code
git add .
git commit -m "Your message"
git push origin your-branch
```

### 9.2. Tạo Migration mới khi thay đổi Model

```bash
# 1. Thay đổi Models (ví dụ: thêm field mới)

# 2. Tạo migration
dotnet ef migrations add AddNewFieldToEmployer

# 3. Review migration code trong Migrations/

# 4. Apply migration
dotnet ef database update

# 5. Test thử với data mới
```

### 9.3. Rollback Migration

```bash
# Xem danh sách migrations
dotnet ef migrations list

# Rollback về migration trước đó
dotnet ef database update PreviousMigrationName

# Remove migration cuối cùng (chưa apply)
dotnet ef migrations remove
```

---

## 10. DEPLOYMENT

### 10.1. Build cho Production

```bash
# Clean
dotnet clean -c Release

# Publish
dotnet publish -c Release -o ./publish --self-contained false

# Output folder: ./publish/
```

### 10.2. Deploy lên IIS (Windows)

1. Cài đặt **ASP.NET Core Hosting Bundle**
2. Tạo Application Pool (No Managed Code)
3. Copy folder `publish/` lên server
4. Cấu hình IIS site trỏ đến folder đó
5. Cập nhật connection string trong `appsettings.json`

### 10.3. Deploy lên Linux Server

```bash
# 1. Copy files lên server
scp -r ./publish user@server:/var/www/hotjob

# 2. SSH vào server
ssh user@server

# 3. Cài đặt .NET Runtime
sudo apt-get install aspnetcore-runtime-10.0

# 4. Tạo systemd service
sudo nano /etc/systemd/system/hotjob.service

# 5. Reload và start
sudo systemctl daemon-reload
sudo systemctl start hotjob
sudo systemctl enable hotjob

# 6. Setup Nginx reverse proxy
```

---

## 11. USEFUL COMMANDS

```bash
# Xem version SDK
dotnet --version
dotnet --list-sdks

# Xem thông tin project
dotnet --info

# Watch mode (auto rebuild on file change)
dotnet watch run

# Build without running
dotnet build

# Clean build artifacts
dotnet clean

# List NuGet packages
dotnet list package

# Update packages
dotnet add package PackageName
```

---

## 12. RESOURCES

- **Official Docs:** https://docs.microsoft.com/aspnet/core/blazor
- **EF Core Docs:** https://docs.microsoft.com/ef/core
- **MySQL Connector:** https://dev.mysql.com/doc/connector-net/en/
- **xUnit Docs:** https://xunit.net/docs/getting-started/netcore/cmdline

---

## ✅ CHECKLIST HOÀN TẤT SETUP

- [ ] Đã cài đặt .NET 10 SDK
- [ ] Đã cài đặt MySQL Server 8.0+
- [ ] Đã clone/tải source code
- [ ] Đã cấu hình connection string trong appsettings.json
- [ ] Đã chạy `dotnet restore` thành công
- [ ] Đã chạy `dotnet ef database update` thành công
- [ ] Đã kiểm tra database có tables
- [ ] Đã chạy `dotnet run` và truy cập được https://localhost:5001
- [ ] Đã test đăng ký/đăng nhập
- [ ] Đã chạy unit tests (nếu có)

**🎉 Chúc mừng! Bạn đã setup thành công dự án HotJob!**

---

**Liên hệ hỗ trợ:**  
Nếu gặp vấn đề không có trong tài liệu này, vui lòng tạo issue trên GitHub hoặc liên hệ team.
