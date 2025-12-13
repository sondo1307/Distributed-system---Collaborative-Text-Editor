using Microsoft.AspNetCore.SignalR.Client;
using System.Text;
using System.Runtime.InteropServices;

// --- CÁC CLASS DÙNG CHUNG (Copy y hệt bên Server) ---
public class UserCursor { public string ConnectionId { get; set; } public string UserName { get; set; } public int CursorPosition { get; set; } public int ColorCode { get; set; } }
public class TextAction { public string Type { get; set; } public int Position { get; set; } public string Content { get; set; } }
// ----------------------------------------------------

public static class WindowHelper
{
    // --- KHAI BÁO CÁC HÀM CỦA WINDOWS (Win32 API) ---
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    // --- CÁC HẰNG SỐ ĐỊNH NGHĨA ---
    private const int GWL_STYLE = -16;

    // WS_MAXIMIZEBOX: Nút phóng to
    // WS_MINIMIZEBOX: Nút thu nhỏ
    // WS_THICKFRAME: Viền cửa sổ dùng để kéo giãn (Resize)
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_THICKFRAME = 0x00040000;

    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_SHOWWINDOW = 0x0040;

    // --- HÀM CHÍNH ĐỂ GỌI ---
    public static void SetupConsoleWindow(int widthPx, int heightPx)
    {
        // 1. Lấy tay cầm (Handle) của cửa sổ Console hiện tại
        IntPtr handle = GetConsoleWindow();

        // 2. Lấy kiểu (Style) hiện tại của cửa sổ
        int currentStyle = GetWindowLong(handle, GWL_STYLE);

        // 3. Loại bỏ tính năng Resize và Maximize
        // Phép toán Bitwise AND với phần bù (~) để tắt bit
        int newStyle = currentStyle & ~WS_MAXIMIZEBOX & ~WS_THICKFRAME;

        // Nếu bạn muốn khóa luôn nút Minimize (ẩn xuống thanh taskbar) thì bỏ comment dòng dưới:
        // newStyle = newStyle & ~WS_MINIMIZEBOX;

        // 4. Áp dụng Style mới
        SetWindowLong(handle, GWL_STYLE, newStyle);

        // 5. Đặt kích thước Pixel cố định
        // Tham số: Handle, 0, Vị trí X, Vị trí Y, Chiều rộng, Chiều cao, Cờ
        // Ở đây tôi để X=100, Y=100 để cửa sổ hiện ra dễ nhìn, bạn có thể chỉnh tùy ý
        SetWindowPos(handle, IntPtr.Zero, 100, 100, widthPx, heightPx, SWP_NOZORDER | SWP_SHOWWINDOW);

        // 6. Cố định Buffer size để thanh cuộn hoạt động đúng với kích thước mới
        // Lưu ý: 500px khá nhỏ, khoảng 60 cột. Cần chỉnh buffer để tránh lỗi hiển thị.
        try
        {
            // Tính toán ước lượng: Font console thường rộng 8px, cao 16px.
            int cols = (widthPx - 40) / 8;
            int rows = (heightPx - 40) / 16;
            Console.SetWindowSize(cols, rows);
            Console.SetBufferSize(cols, rows); // Tắt thanh cuộn ngang/dọc thừa thãi
        }
        catch
        {
            // Bỏ qua lỗi nếu tính toán sai kích thước buffer (Windows 11 Terminal mới tự xử lý tốt hơn)
        }
    }
}

class Program
{
    static HubConnection connection;
    static StringBuilder localDoc = new StringBuilder();
    static List<UserCursor> remoteUsers = new List<UserCursor>();
    static int myCursorIndex = 0;
    static string myName = "";
    static object _renderLock = new object();
    static string statusMessage = "San sang"; // Biến lưu trạng thái hiển thị

    static async Task Main(string[] args)
    {
        // Setup cửa sổ 500x500 pixel và khóa resize
        WindowHelper.SetupConsoleWindow(1500, 750);

        Console.OutputEncoding = Encoding.UTF8;
        Console.Write("Nhap ten cua ban: ");
        myName = Console.ReadLine();

        // Kết nối đến Server ở cổng 5000 (mặc định)
        connection = new HubConnectionBuilder()
            .WithUrl("http://localhost:5186/editorhub")
            .Build();

        RegisterEvents();

        try
        {
            Console.WriteLine("Dang ket noi...");
            await connection.StartAsync();
            await connection.InvokeAsync("JoinChat", myName);

            Console.CursorVisible = false;
            RenderUI();
            await InputLoop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nLOI KET NOI: {ex.Message}");
            Console.WriteLine("Hay chac chan ban da chay Server truoc!");
        }
    }

    static void RegisterEvents()
    {
        // 1. Nhận văn bản khi mới vào (Giữ nguyên)
        connection.On<string>("LoadDocument", (doc) => {
            localDoc = new StringBuilder(doc);
            // Lần đầu tải xong cũng nên Clear cho sạch
            isFirstRender = true;
            RenderUI();
        });

        // 2. CÓ NGƯỜI MỚI VÀO (Sửa đoạn này)
        connection.On<UserCursor>("UserJoined", (user) => {
            remoteUsers.Add(user);

            // --- THÊM DÒNG NÀY ---
            isFirstRender = true; // Bắt buộc Clear màn hình để vẽ lại danh sách mới
                                  // ---------------------

            RenderUI();
        });

        // 3. CÓ NGƯỜI THOÁT (Nên làm tương tự cho UserLeft)
        connection.On<string>("UserLeft", (id) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null) remoteUsers.Remove(user);

            // --- THÊM DÒNG NÀY ---
            isFirstRender = true; // Xóa màn hình để danh sách ngắn lại gọn gàng
                                  // ---------------------

            RenderUI();
        });

        // 4. Cập nhật danh sách (Sửa đoạn này luôn cho chắc)
        connection.On<List<UserCursor>>("UpdateUserList", (users) => {
            remoteUsers = users.Where(u => u.ConnectionId != connection.ConnectionId).ToList();

            // --- THÊM DÒNG NÀY ---
            isFirstRender = true;
            // ---------------------

            RenderUI();
        });

        // ... Các sự kiện khác (ReceiveAction, RemoteCursorMoved) giữ nguyên ...
        // Lưu ý: ReceiveAction và RemoteCursorMoved KHÔNG được set isFirstRender = true 
        // vì gõ phím thì cần mượt, không được nháy.
        connection.On<TextAction>("ReceiveAction", (action) => {
            ApplyLocalChange(action);
            RenderUI();
        });

        connection.On<string, int>("RemoteCursorMoved", (id, pos) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null) { user.CursorPosition = pos; RenderUI(); }
        });

        connection.On<string>("ReceiveNotification", (msg) => {
            statusMessage = msg; // Cập nhật trạng thái
            RenderUI(); // Vẽ lại màn hình để hiện thông báo
        });
    }

    static void ApplyLocalChange(TextAction action)
    {
        if (action.Type == "INSERT")
        {
            localDoc.Insert(action.Position, action.Content);
            if (action.Position <= myCursorIndex) myCursorIndex++;
        }
        else if (action.Type == "DELETE" && action.Position < localDoc.Length)
        {
            localDoc.Remove(action.Position, 1);
            if (action.Position < myCursorIndex) myCursorIndex--;
        }
    }

    static async Task InputLoop()
    {
        while (true)
        {
            var key = Console.ReadKey(true);
            bool docChanged = false;
            bool cursorMoved = false;

            // --- THÊM ĐOẠN NÀY ĐỂ BẮT CTRL + S ---
            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.S)
            {
                statusMessage = "Dang luu..."; // Phản hồi ngay lập tức cho user đỡ sốt ruột
                RenderUI();

                // Gọi lên Server
                await connection.InvokeAsync("RequestSave", myName);
                continue; // Bỏ qua các xử lý phía dưới
            }

            if (key.Key == ConsoleKey.LeftArrow && myCursorIndex > 0)
            {
                myCursorIndex--; cursorMoved = true;
            }
            else if (key.Key == ConsoleKey.RightArrow && myCursorIndex < localDoc.Length)
            {
                myCursorIndex++; cursorMoved = true;
            }
            // --- THÊM LOGIC LÊN / XUỐNG ---
            else if (key.Key == ConsoleKey.UpArrow)
            {
                int newIdx = CalculateVerticalMove(-1); // -1 là đi lên
                if (newIdx != myCursorIndex)
                {
                    myCursorIndex = newIdx;
                    cursorMoved = true;
                }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                int newIdx = CalculateVerticalMove(1); // 1 là đi xuống
                if (newIdx != myCursorIndex)
                {
                    myCursorIndex = newIdx;
                    cursorMoved = true;
                }
            }
            else if (key.Key == ConsoleKey.Backspace && myCursorIndex > 0)
            {
                int delPos = myCursorIndex - 1;
                localDoc.Remove(delPos, 1);
                myCursorIndex--;
                await connection.InvokeAsync("SendAction", new TextAction { Type = "DELETE", Position = delPos });
                docChanged = true;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                localDoc.Insert(myCursorIndex, key.KeyChar);
                await connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = key.KeyChar.ToString() });
                myCursorIndex++;
                docChanged = true;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                string newline = "\n"; // Dùng \n để đại diện xuống dòng

                // 1. Chèn vào local
                localDoc.Insert(myCursorIndex, newline);

                // 2. Gửi lên server
                await connection.InvokeAsync("SendAction", new TextAction
                {
                    Type = "INSERT",
                    Position = myCursorIndex,
                    Content = newline
                });

                myCursorIndex++; // Nhảy qua ký tự \n
                docChanged = true;
            }
            // -------------------------------------------------
            else if (!char.IsControl(key.KeyChar))
            {
                // ... (Code gõ chữ thường giữ nguyên) ...
                localDoc.Insert(myCursorIndex, key.KeyChar);
                await connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = key.KeyChar.ToString() });
                myCursorIndex++;
                docChanged = true;
            }

            if (docChanged || cursorMoved)
            {
                RenderUI();
                await connection.InvokeAsync("UpdateCursor", myCursorIndex);
            }
        }
    }

    // Biến lưu độ cao của phần Header (Danh sách người dùng)
    static int headerHeight = 0;
    static bool isFirstRender = true;
    static void RenderUI()
    {
        lock (_renderLock)
        {
            Console.CursorVisible = false;
            if (isFirstRender) { Console.Clear(); isFirstRender = false; }
            else { Console.SetCursorPosition(0, 0); }

            // --- SỬA PHẦN HEADER NÀY ---
            Console.WriteLine("================ DANH SÁCH THAM GIA ================");

            // In dòng trạng thái nổi bật (Ví dụ: [SYSTEM] Da luu bai...)
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"THONG BAO: {statusMessage}".PadRight(50)); // PadRight để xóa chữ cũ
            Console.ResetColor();

            // Vẽ User Info (Giống cũ)
            WriteColoredUser(myName, -1, ConsoleColor.White, myCursorIndex); // -1 là màu trắng
            foreach (var user in remoteUsers)
            {
                WriteColoredUser(user.UserName, user.ColorCode, (ConsoleColor)user.ColorCode, user.CursorPosition);
            }

            Console.WriteLine("====================================================");
            Console.WriteLine("NỘI DUNG VĂN BẢN:                                   "); // Thêm khoảng trắng để xóa rác cũ nếu header co lại
            Console.WriteLine("----------------------------------------------------");

            // Lưu lại vị trí bắt đầu của văn bản để tí nữa tính toán
            headerHeight = Console.CursorTop;

            // --- VẼ VĂN BẢN (KHÔNG CLEAR) ---
            string text = localDoc.ToString();
            int consoleWidth = Console.WindowWidth;

            for (int i = 0; i < text.Length + 1; i++)
            {
                var remoteUser = remoteUsers.FirstOrDefault(u => u.CursorPosition == i);
                bool isMyCursor = (i == myCursorIndex);

                // Setup màu (Giữ nguyên logic cũ)
                ConsoleColor bg = ConsoleColor.Black;
                ConsoleColor fg = ConsoleColor.Gray;

                if (remoteUser != null) { bg = (ConsoleColor)remoteUser.ColorCode; fg = ConsoleColor.Black; }
                if (isMyCursor) { bg = ConsoleColor.White; fg = ConsoleColor.Black; }

                Console.BackgroundColor = bg;
                Console.ForegroundColor = fg;

                // --- LOGIC MỚI SỬA Ở ĐÂY ---
                if (i < text.Length)
                {
                    char c = text[i];

                    if (c == '\n')
                    {
                        // Reset màu về đen để xóa dòng cho sạch (không bị vệt màu trắng/đỏ kéo dài)
                        Console.ResetColor();

                        // Tính xem còn bao nhiêu ô trống từ đây đến cuối dòng
                        int currentX = Console.CursorLeft;
                        int spacesNeeded = consoleWidth - currentX;

                        // Nếu chưa chạm lề phải, in khoảng trắng đè lên chữ cũ
                        if (spacesNeeded > 0)
                        {
                            Console.Write(new string(' ', spacesNeeded));
                        }
                        // Khi in full dòng, Console tự động xuống dòng, ta không cần in \n nữa
                        // Trừ trường hợp đặc biệt: Nếu đang đứng sát lề phải thì cần xuống dòng thủ công
                        if (spacesNeeded <= 0)
                        {
                            Console.WriteLine();
                        }
                    }
                    else
                    {
                        // Ký tự bình thường thì in ra
                        Console.Write(c);
                    }
                }
                else if (isMyCursor || remoteUser != null)
                {
                    Console.Write(" ");
                }
                // ---------------------------

                Console.ResetColor();
            }

            // --- QUAN TRỌNG: XÓA RÁC Ở CUỐI ---
            // Vì ta không dùng Clear(), nếu văn bản cũ dài hơn văn bản mới, 
            // các ký tự cũ sẽ vẫn còn ở cuối màn hình. Ta phải in khoảng trắng đè lên.
            int currentPos = Console.CursorLeft + (Console.CursorTop * consoleWidth);
            // In khoảng 50 dấu cách trắng (hoặc hết màn hình) để xóa đuôi thừa
            Console.Write(new string(' ', 50));
        }
    }

    // Hàm phụ trợ để viết info user cho gọn code
    static void WriteColoredUser(string name, int colorCode, ConsoleColor bgColor, int pos)
    {
        Console.Write("  ");
        Console.BackgroundColor = bgColor;
        Console.ForegroundColor = ConsoleColor.Black;
        Console.Write($" {name} ");
        Console.ResetColor();
        // In thêm khoảng trắng ở cuối dòng này để đảm bảo xóa sạch chữ cũ nếu tên user thay đổi độ dài
        Console.WriteLine($" -> Pos: {pos}      ");
    }

    // Hàm tính toán vị trí mới khi bấm Lên (-1) hoặc Xuống (+1)
    static int CalculateVerticalMove(int direction)
    {
        string text = localDoc.ToString();
        // Tách văn bản thành các dòng dựa trên dấu xuống dòng \n
        // Lưu ý: Cách này hơi tốn kém hiệu năng nếu văn bản quá dài, nhưng với bài tập thì OK
        string[] lines = text.Split('\n');

        // 1. Tìm xem con trỏ hiện tại đang ở Dòng nào, Cột nào
        int currentPos = 0;
        int currentRow = 0;
        int currentCol = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            // Độ dài dòng này (+1 vì tính cả ký tự \n, trừ dòng cuối không có \n cũng không sao)
            int lineLen = lines[i].Length + 1;

            // Nếu con trỏ nằm trong phạm vi dòng này
            if (myCursorIndex < currentPos + lineLen)
            {
                currentRow = i;
                currentCol = myCursorIndex - currentPos;
                break;
            }
            currentPos += lineLen;
        }

        // 2. Tính dòng đích (Lên hoặc Xuống)
        int targetRow = currentRow + direction;

        // Chặn biên: Nếu Lên quá đỉnh hoặc Xuống quá đáy thì đứng im
        if (targetRow < 0 || targetRow >= lines.Length)
        {
            return myCursorIndex;
        }

        // 3. Tính toán vị trí Index mới (từ 2D đổi ngược về 1D)
        int newIndex = 0;
        for (int i = 0; i < targetRow; i++)
        {
            newIndex += lines[i].Length + 1; // Cộng dồn độ dài các dòng phía trên
        }

        // Logic quan trọng: Giữ cột dọc
        // Ví dụ: Đang ở cột 10 dòng trên, xuống dòng dưới chỉ có 5 chữ -> Phải nhảy về cột 5 (cuối dòng)
        int targetCol = Math.Min(currentCol, lines[targetRow].Length);

        newIndex += targetCol;

        return newIndex;
    }
}