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
        connection.On<string>("LoadDocument", (doc) => {
            localDoc = new StringBuilder(doc);
            isFirstRender = true;
            RenderUI(true); // Load xong thì Clear sạch sẽ
        });

        connection.On<UserCursor>("UserJoined", (user) => {
            remoteUsers.Add(user);
            isFirstRender = true;
            RenderUI(true); // Người vào -> Thay đổi layout -> Clear
        });

        connection.On<string>("UserLeft", (id) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null) remoteUsers.Remove(user);
            isFirstRender = true;
            RenderUI(true); // Người ra -> Thay đổi layout -> Clear
        });

        connection.On<List<UserCursor>>("UpdateUserList", (users) => {
            remoteUsers = users.Where(u => u.ConnectionId != connection.ConnectionId).ToList();
            isFirstRender = true;
            RenderUI(true);
        });

        // --- NHẬN VĂN BẢN TỪ NGƯỜI KHÁC -> CẦN CLEAR ---
        connection.On<TextAction>("ReceiveAction", (action) => {
            ApplyLocalChange(action);
            RenderUI(true); // TRUE: Để xóa sạch ghosting khi nội dung thay đổi
        });

        // --- NGƯỜI KHÁC DI CHUYỂN -> KHÔNG CẦN CLEAR ---
        connection.On<string, int>("RemoteCursorMoved", (id, pos) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null)
            {
                user.CursorPosition = pos;
                RenderUI(false); // FALSE: Chỉ vẽ đè cho mượt
            }
        });

        connection.On<string>("ReceiveNotification", (msg) => {
            statusMessage = msg;
            RenderUI(false); // Chỉ hiện thông báo, không cần clear
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
            bool docChanged = false;  // Biến này ám chỉ văn bản thay đổi -> Cần Clear
            bool cursorMoved = false; // Biến này ám chỉ chỉ di chuyển -> Không Clear

            // --- BẮT CTRL + S ---
            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.S)
            {
                statusMessage = "Dang luu...";
                RenderUI(false); // Chỉ hiện thông báo, không cần clear

                await connection.InvokeAsync("RequestSave", myName);
                continue;
            }

            // --- DI CHUYỂN (KHÔNG CLEAR) ---
            if (key.Key == ConsoleKey.LeftArrow && myCursorIndex > 0)
            {
                myCursorIndex--; cursorMoved = true;
            }
            else if (key.Key == ConsoleKey.RightArrow && myCursorIndex < localDoc.Length)
            {
                myCursorIndex++; cursorMoved = true;
            }
            else if (key.Key == ConsoleKey.UpArrow)
            {
                int newIdx = CalculateVerticalMove(-1);
                if (newIdx != myCursorIndex) { myCursorIndex = newIdx; cursorMoved = true; }
            }
            else if (key.Key == ConsoleKey.DownArrow)
            {
                int newIdx = CalculateVerticalMove(1);
                if (newIdx != myCursorIndex) { myCursorIndex = newIdx; cursorMoved = true; }
            }

            // --- NHẬP LIỆU (CẦN CLEAR ĐỂ TRÁNH GHOSTING) ---
            else if (key.Key == ConsoleKey.Backspace && myCursorIndex > 0)
            {
                int delPos = myCursorIndex - 1;
                localDoc.Remove(delPos, 1);
                myCursorIndex--;
                await connection.InvokeAsync("SendAction", new TextAction { Type = "DELETE", Position = delPos });
                docChanged = true;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                string newline = "\n";
                localDoc.Insert(myCursorIndex, newline);
                await connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = newline });
                myCursorIndex++;
                docChanged = true;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                localDoc.Insert(myCursorIndex, key.KeyChar);
                await connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = key.KeyChar.ToString() });
                myCursorIndex++;
                docChanged = true;
            }

            // --- GỌI HÀM VẼ ---
            if (docChanged)
            {
                RenderUI(true); // TRUE: Clear màn hình vì văn bản thay đổi
                await connection.InvokeAsync("UpdateCursor", myCursorIndex);
            }
            else if (cursorMoved)
            {
                RenderUI(false); // FALSE: Chỉ vẽ đè vì chỉ di chuyển
                await connection.InvokeAsync("UpdateCursor", myCursorIndex);
            }
        }
    }

    // Biến lưu độ cao của phần Header (Danh sách người dùng)
    static int headerHeight = 0;
    static bool isFirstRender = true;
    // Thêm tham số mặc định clearScreen = false
    static void RenderUI(bool requestClear = false)
    {
        lock (_renderLock)
        {
            Console.CursorVisible = false;

            // Biến này để theo dõi xem thực tế ta có Clear màn hình không
            bool didClear = false;

            // 1. Xử lý Clear màn hình
            if (requestClear || isFirstRender)
            {
                // Reset màu trước khi Clear để tránh lỗi nền màu lạ
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;

                Console.Clear();

                isFirstRender = false;
                didClear = true; // Đánh dấu là ĐÃ CLEAR
            }
            else
            {
                Console.SetCursorPosition(0, 0);
            }

            // --- VẼ GIAO DIỆN (HEADER + USER) ---
            Console.WriteLine("================ DANH SÁCH THAM GIA ================");

            // In trạng thái
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"THONG BAO: {statusMessage}".PadRight(50));
            Console.ResetColor();

            // In User Info
            WriteColoredUser(myName, -1, ConsoleColor.White, myCursorIndex);
            foreach (var user in remoteUsers)
            {
                WriteColoredUser(user.UserName, user.ColorCode, (ConsoleColor)user.ColorCode, user.CursorPosition);
            }

            Console.WriteLine("====================================================");
            Console.WriteLine("NỘI DUNG VĂN BẢN:                                   ");
            Console.WriteLine("----------------------------------------------------");

            // Lưu vị trí bắt đầu văn bản
            headerHeight = Console.CursorTop;

            // --- VẼ VĂN BẢN ---
            string text = localDoc.ToString();
            int consoleWidth = Console.WindowWidth;

            for (int i = 0; i < text.Length + 1; i++)
            {
                var remoteUser = remoteUsers.FirstOrDefault(u => u.CursorPosition == i);
                bool isMyCursor = (i == myCursorIndex);

                ConsoleColor bg = ConsoleColor.Black;
                ConsoleColor fg = ConsoleColor.Gray;

                if (remoteUser != null) { bg = (ConsoleColor)remoteUser.ColorCode; fg = ConsoleColor.Black; }
                if (isMyCursor) { bg = ConsoleColor.White; fg = ConsoleColor.Black; }

                Console.BackgroundColor = bg;
                Console.ForegroundColor = fg;

                if (i < text.Length)
                {
                    char c = text[i];
                    if (c == '\n')
                    {
                        Console.ResetColor();
                        int currentX = Console.CursorLeft;
                        int spacesNeeded = consoleWidth - currentX;
                        if (spacesNeeded > 0) Console.Write(new string(' ', spacesNeeded));
                        // Chỉ xuống dòng nếu chưa chạm lề
                        if (Console.CursorLeft != 0) Console.WriteLine();
                    }
                    else
                    {
                        Console.Write(c);
                    }
                }
                else if (isMyCursor || remoteUser != null)
                {
                    Console.Write(" ");
                }
                Console.ResetColor();
            }

            // --- LOGIC QUAN TRỌNG ĐÃ SỬA ---
            // Chỉ xóa phần thừa (Ghosting) nếu như ta KHÔNG dùng Console.Clear()
            // Nếu đã Clear rồi (didClear == true) thì màn hình đã sạch, in thêm space sẽ gây lỗi cuộn trang
            if (!didClear)
            {
                try
                {
                    // Lấy vị trí hiện tại
                    int currentPos = Console.CursorLeft + (Console.CursorTop * consoleWidth);
                    // Tính tổng số ô của màn hình
                    int windowSize = consoleWidth * Console.WindowHeight;

                    // Tính xem còn bao nhiêu ô trống phía sau
                    int leftOver = windowSize - currentPos;

                    // Chỉ xóa một lượng vừa đủ để che chữ cũ (ví dụ 2 dòng), không xóa hết buffer để tránh lag/lỗi
                    if (leftOver > 0)
                    {
                        // Giới hạn xóa tối đa 2 dòng trắng để an toàn hiệu năng
                        int safeClearCount = Math.Min(leftOver, consoleWidth * 2);
                        Console.Write(new string(' ', safeClearCount));
                    }
                }
                catch
                {
                    // Bỏ qua lỗi nếu tính toán vượt buffer (tránh crash)
                }
            }
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