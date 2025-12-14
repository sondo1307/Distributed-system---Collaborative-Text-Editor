using EditorServer;
using System.Text;

// Tạo thư mục lưu bài nếu chưa có
string saveFolder = Path.Combine(Directory.GetCurrentDirectory(), "SavedDocs");
if (!Directory.Exists(saveFolder)) Directory.CreateDirectory(saveFolder);

// --- VÒNG LẶP CHÍNH CỦA CHƯƠNG TRÌNH ---
while (true)
{
    // 1. KHỞI TẠO TRẠNG THÁI MỚI (Mỗi lần quay lại menu là 1 state mới)
    var docState = new DocumentState();
    bool exitProgram = false; // Cờ để kiểm tra xem user có muốn thoát hẳn không

    // --- MENU CHỌN FILE ---
    while (true)
    {
        Console.Clear();
        Console.WriteLine("=== SERVER QUAN LY VAN BAN ===");
        Console.WriteLine($"Thu muc luu: {saveFolder}");
        Console.WriteLine("1. Tao van ban moi");
        Console.WriteLine("2. Load bai cu");
        Console.WriteLine("3. Xoa bai cu");
        Console.WriteLine("4. THOAT CHUONG TRINH (Exit)");
        Console.WriteLine("------------------------------");
        Console.Write("Chon: ");
        var choice = Console.ReadLine();

        if (choice == "1")
        {
            docState.Content.Clear();
            Console.Write("Dat ten file (vd: tailieu1.txt): ");
            string name = Console.ReadLine();
            docState.CurrentFileName = string.IsNullOrWhiteSpace(name) ? "tailieu.txt" : name;
            if (!docState.CurrentFileName.EndsWith(".txt")) docState.CurrentFileName += ".txt";

            Console.WriteLine("Da tao moi! Nhan Enter de bat dau Server...");
            Console.ReadLine();
            break; // Thoát menu để vào bước chạy server
        }
        else if (choice == "2" || choice == "3")
        {
            var files = Directory.GetFiles(saveFolder, "*.txt");
            if (files.Length == 0)
            {
                Console.WriteLine("Khong co file nao. Nhan Enter...");
                Console.ReadLine();
                continue;
            }

            Console.WriteLine("--- Danh sach file ---");
            for (int i = 0; i < files.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {Path.GetFileName(files[i])}");
            }

            Console.Write("Chon so thu tu file (hoac gõ 0 de quay lai): ");
            if (int.TryParse(Console.ReadLine(), out int idx) && idx > 0 && idx <= files.Length)
            {
                string selectedPath = files[idx - 1];
                if (choice == "2") // Load
                {
                    string content = File.ReadAllText(selectedPath);
                    docState.Content = new StringBuilder(content);
                    docState.CurrentFileName = Path.GetFileName(selectedPath);
                    Console.WriteLine($"Da load file: {docState.CurrentFileName}");
                    Console.WriteLine("Nhan Enter de bat dau Server...");
                    Console.ReadLine();
                    break; // Thoát menu
                }
                else // Xoa
                {
                    File.Delete(selectedPath);
                    Console.WriteLine("Da xoa file!");
                    Console.ReadLine();
                }
            }
        }
        else if (choice == "4")
        {
            exitProgram = true;
            break;
        }
    }

    if (exitProgram) break; // Thoát vòng lặp chính -> Tắt chương trình

    // --- CẤU HÌNH & CHẠY SERVER ---
    // --- CẤU HÌNH IP CHO SERVER (Sửa đoạn này) ---
    Console.Clear();
    Console.WriteLine("--- CAU HINH MANG ---");

    var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
    string selectedIP = ""; // Khởi tạo biến để lưu IPv4

    foreach (var ip in host.AddressList)
    {
        // Kiểm tra xem địa chỉ IP này có phải là loại InterNetwork (IPv4) hay không
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            Console.WriteLine($"Tìm thấy IPv4: {ip}");

            // Nếu đây là lần đầu tiên tìm thấy IPv4, lưu nó lại.
            if (string.IsNullOrEmpty(selectedIP))
            {
                selectedIP = ip.ToString();
            }
        }
    }

    // 1. Hiển thị gợi ý các IP đang có trên máy để bạn dễ nhập
    //Console.WriteLine("Cac IP hien co tren may nay:");
    //var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
    //foreach (var ip in host.AddressList)
    //{
    //    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    //    {
    //        Console.WriteLine($" - {ip}");
    //    }
    //}

    // 2. Cho phép nhập tay
    //Console.Write("\nNhap IP ban muon Server chay (Enter de dung mac dinh 0.0.0.0): ");
    //string selectedIP = host.AddressList[0].ToString();

    // Nếu không nhập gì thì dùng 0.0.0.0 (an toàn nhất)
    //if (string.IsNullOrWhiteSpace(selectedIP)) selectedIP = "0.0.0.0";

    // ---------------------------------------------

    // --- CHẠY SERVER ---
    var builder = WebApplication.CreateBuilder(args);

    // ÁP DỤNG IP VỪA NHẬP VÀO ĐÂY
    builder.WebHost.UseUrls($"http://{selectedIP}:5186");

    builder.Services.AddSingleton(docState);
    builder.Services.AddSignalR();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    var app = builder.Build();
    app.MapHub<EditorHub>("/editorhub");

    try
    {
        await app.StartAsync();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[LOI] Khong the khoi dong Server tren IP {selectedIP}:5186");
        Console.WriteLine($"Loi chi tiet: {ex.Message}");
        Console.WriteLine("Nhan Enter de quay lai menu...");
        Console.ReadLine();
        continue; // Quay lại vòng lặp menu để chọn lại
    }

    Console.Clear();
    Console.WriteLine($">>> SERVER DANG CHAY TAI: http://{selectedIP}:5186 <<<");

    // ... (Phần code hiển thị hướng dẫn và vòng lặp lệnh bên dưới giữ nguyên)
    Console.WriteLine("Cac lenh quan tri:");
    Console.WriteLine(" - save : Luu file");
    Console.WriteLine(" - saveas : Luu vao file khac");
    Console.WriteLine(" - back : Dung Server va quay lai Menu chon file");
    Console.WriteLine(" - exit : Thoat chuong trinh ngay lap tuc");
    Console.WriteLine("---------------------------------------------");

    // --- VÒNG LẶP XỬ LÝ LỆNH KHI SERVER ĐANG CHẠY ---
    bool backToMenu = false;
    while (true)
    {
        Console.Write("Command> ");
        string cmd = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(cmd)) continue;

        string[] parts = cmd.Split(' ');
        string action = parts[0].ToLower();

        if (action == "save")
        {
            string path = Path.Combine(saveFolder, docState.CurrentFileName);
            lock (docState.Content)
            {
                File.WriteAllText(path, docState.Content.ToString());
            }
            Console.WriteLine($"[OK] Da luu vao {path}");
        }
        else if (action == "saveas")
        {
            if (parts.Length < 2)
            {
                Console.WriteLine("Loi: Thieu ten file. Cach dung: saveas <ten_file_moi>");
            }
            else
            {
                // 1. Lấy tên file mới từ tham số nhập vào
                string newName = parts[1];
                if (!newName.EndsWith(".txt")) newName += ".txt";

                // 2. Cập nhật tên file hiện tại của hệ thống sang file mới
                docState.CurrentFileName = newName;
                string newPath = Path.Combine(saveFolder, newName);

                // 3. Ghi nội dung sang file mới
                lock (docState.Content)
                {
                    File.WriteAllText(newPath, docState.Content.ToString());
                }

                Console.WriteLine($"[OK] Da luu sang file moi: {newPath}");
                Console.WriteLine($"[INFO] He thong da chuyen sang lam viec tren file: {newName}");
            }
        }
        // -----------------------------------
        else if (action == "back")
        {
            // ... (Code back cũ giữ nguyên) ...
            Console.WriteLine("Dang dung Server...");
            backToMenu = true;
            break;
        }
        else if (action == "exit")
        {
            // ... (Code exit cũ giữ nguyên) ...
            Console.WriteLine("Dang tat chuong trinh...");
            exitProgram = true;
            break;
        }
        else
        {
            Console.WriteLine("Lenh khong hop le.");
        }
    }

    // --- DỌN DẸP SERVER ---
    await app.StopAsync();
    await app.DisposeAsync(); // Giải phóng cổng 5186 để lần sau chạy lại không bị lỗi "Address in use"

    if (exitProgram) break; // Thoát hẳn chương trình

    // Nếu không exitProgram thì vòng lặp while(true) ở ngoài cùng sẽ chạy lại -> Hiện Menu
}