using Microsoft.AspNetCore.SignalR.Client;
using System.Text;
using System.Runtime.InteropServices;

// --- CÁC CLASS DÙNG CHUNG (Copy y hệt bên Server) ---
public class UserCursor { public string ConnectionId { get; set; } public string UserName { get; set; } public int CursorPosition { get; set; } public int ColorCode { get; set; } }
public class TextAction { public string Type { get; set; } public int Position { get; set; } public string Content { get; set; } }
// ----------------------------------------------------

// --- STRUCT MỚI: ĐẠI DIỆN CHO 1 Ô TRÊN MÀN HÌNH ---
public struct ConsolePixel
{
    public char Char;
    public ConsoleColor Background;
    public ConsoleColor Foreground;

    // So sánh xem 2 ô có giống hệt nhau không
    public static bool operator ==(ConsolePixel a, ConsolePixel b) => a.Char == b.Char && a.Background == b.Background && a.Foreground == b.Foreground;
    public static bool operator !=(ConsolePixel a, ConsolePixel b) => !(a == b);
}

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
    static string statusMessage = "San sang";

    // --- BỘ ĐỆM PIXEL (Lưu cả chữ và màu) ---
    static List<ConsolePixel[]> previousFrame = new List<ConsolePixel[]>();
    static int consoleWidth = 0;
    static int consoleHeight = 0;

    static async Task Main(string[] args)
    {
        try { Console.SetWindowSize(120, 30); } catch { }
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;

        Console.WriteLine("=== KET NOI SERVER ===");
        Console.Write("Nhap IP Server: ");
        string ip = Console.ReadLine();
        //if (string.IsNullOrWhiteSpace(ip)) ip = "localhost";

        Console.Write("Nhap ten cua ban: ");
        myName = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(myName)) myName = $"User_{new Random().Next(100, 999)}";

        connection = new HubConnectionBuilder()
            .WithUrl($"http://{ip}:5186/editorhub")
            .Build();

        RegisterEvents();

        try
        {
            await connection.StartAsync();
            await connection.InvokeAsync("JoinChat", myName);

            Console.Clear();
            RenderUI(true); // Force vẽ lại toàn bộ lần đầu

            await InputLoop();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"LOI: {ex.Message}");
            Console.ReadKey();
        }
    }

    static void RegisterEvents()
    {
        connection.On<string>("LoadDocument", (doc) => {
            localDoc = new StringBuilder(doc);
            RenderUI();
        });
        connection.On<UserCursor>("UserJoined", (user) => {
            remoteUsers.Add(user);
            RenderUI();
        });
        connection.On<string>("UserLeft", (id) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null) remoteUsers.Remove(user);
            RenderUI();
        });
        connection.On<List<UserCursor>>("UpdateUserList", (users) => {
            remoteUsers = users.Where(u => u.ConnectionId != connection.ConnectionId).ToList();
            RenderUI();
        });
        connection.On<TextAction>("ReceiveAction", (action) => {
            ApplyLocalChange(action);
            RenderUI();
        });
        connection.On<string, int>("RemoteCursorMoved", (id, pos) => {
            var user = remoteUsers.FirstOrDefault(u => u.ConnectionId == id);
            if (user != null) { user.CursorPosition = pos; RenderUI(); }
        });
        connection.On<string>("ReceiveNotification", (msg) => {
            statusMessage = msg;
            RenderUI();
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

            if (key.Modifiers == ConsoleModifiers.Control && key.Key == ConsoleKey.S)
            {
                statusMessage = "Dang luu...";
                RenderUI();
                _ = connection.InvokeAsync("RequestSave", myName);
                continue;
            }

            if (key.Key == ConsoleKey.LeftArrow && myCursorIndex > 0) { myCursorIndex--; cursorMoved = true; }
            else if (key.Key == ConsoleKey.RightArrow && myCursorIndex < localDoc.Length) { myCursorIndex++; cursorMoved = true; }
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
            else if (key.Key == ConsoleKey.Backspace && myCursorIndex > 0)
            {
                int delPos = myCursorIndex - 1;
                localDoc.Remove(delPos, 1);
                myCursorIndex--;
                _ = connection.InvokeAsync("SendAction", new TextAction { Type = "DELETE", Position = delPos });
                docChanged = true;
            }
            else if (key.Key == ConsoleKey.Enter)
            {
                string nl = "\n";
                localDoc.Insert(myCursorIndex, nl);
                _ = connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = nl });
                myCursorIndex++;
                docChanged = true;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                localDoc.Insert(myCursorIndex, key.KeyChar);
                _ = connection.InvokeAsync("SendAction", new TextAction { Type = "INSERT", Position = myCursorIndex, Content = key.KeyChar.ToString() });
                myCursorIndex++;
                docChanged = true;
            }

            if (docChanged || cursorMoved)
            {
                RenderUI();
                _ = connection.InvokeAsync("UpdateCursor", myCursorIndex);
            }
        }
    }

    // --- RENDER ENGINE NÂNG CẤP (HỖ TRỢ MÀU) ---
    static void RenderUI(bool forceRedraw = false)
    {
        lock (_renderLock)
        {
            // 1. Cập nhật kích thước
            if (consoleWidth != Console.WindowWidth || consoleHeight != Console.WindowHeight)
            {
                consoleWidth = Console.WindowWidth;
                consoleHeight = Console.WindowHeight;
                previousFrame.Clear(); // Reset bộ đệm để vẽ lại từ đầu
                Console.Clear();
                forceRedraw = true;
            }

            // 2. Tính toán Frame mới (Bao gồm cả ký tự và màu)
            List<ConsolePixel[]> currentFrame = BuildPixelFrame();

            // 3. So sánh và vẽ lại
            for (int y = 0; y < currentFrame.Count && y < consoleHeight; y++)
            {
                ConsolePixel[] newRow = currentFrame[y];
                ConsolePixel[] oldRow = (y < previousFrame.Count) ? previousFrame[y] : null;

                // Nếu dòng thay đổi hoặc bắt buộc vẽ lại
                if (forceRedraw || !AreRowsEqual(newRow, oldRow))
                {
                    DrawRow(y, newRow);
                }
            }

            // 4. Lưu lại
            previousFrame = currentFrame;
        }
    }

    // Hàm vẽ 1 dòng tối ưu (chỉ đổi màu khi cần thiết)
    static void DrawRow(int y, ConsolePixel[] row)
    {
        Console.SetCursorPosition(0, y);

        // Màu mặc định
        ConsoleColor currentBg = ConsoleColor.Black;
        ConsoleColor currentFg = ConsoleColor.Gray;
        Console.BackgroundColor = currentBg;
        Console.ForegroundColor = currentFg;

        for (int x = 0; x < row.Length; x++)
        {
            var p = row[x];
            // Chỉ gọi lệnh đổi màu khi màu của ô này khác màu đang set
            if (p.Background != currentBg) { Console.BackgroundColor = p.Background; currentBg = p.Background; }
            if (p.Foreground != currentFg) { Console.ForegroundColor = p.Foreground; currentFg = p.Foreground; }

            Console.Write(p.Char);
        }
        // Reset màu cuối dòng
        Console.ResetColor();
    }

    static bool AreRowsEqual(ConsolePixel[] a, ConsolePixel[] b)
    {
        if (a == null || b == null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    // --- HÀM TẠO BUFFER (LOGIC MÀU NẰM Ở ĐÂY) ---
    static List<ConsolePixel[]> BuildPixelFrame()
    {
        List<ConsolePixel[]> frame = new List<ConsolePixel[]>();

        // 1. Header
        frame.Add(CreateTextRow("================ DANH SACH THAM GIA ================", ConsoleColor.Black, ConsoleColor.White));
        frame.Add(CreateTextRow($" THONG BAO: {statusMessage}", ConsoleColor.Black, ConsoleColor.Yellow));

        // 2. Users Info
        frame.Add(CreateUserRow(myName, -1, myCursorIndex));
        foreach (var u in remoteUsers)
        {
            frame.Add(CreateUserRow(u.UserName, u.ColorCode, u.CursorPosition));
        }

        frame.Add(CreateTextRow("====================================================", ConsoleColor.Black, ConsoleColor.White));
        frame.Add(CreateTextRow("NOI DUNG VAN BAN:", ConsoleColor.Black, ConsoleColor.White));
        frame.Add(CreateTextRow("----------------------------------------------------", ConsoleColor.Black, ConsoleColor.Gray));

        // 3. Document Content (Quan trọng nhất: Xử lý màu nền User)
        string text = localDoc.ToString();
        List<ConsolePixel> currentRow = new List<ConsolePixel>();

        for (int i = 0; i < text.Length + 1; i++)
        {
            // Xác định màu nền tại vị trí i
            ConsoleColor bg = ConsoleColor.Black;
            ConsoleColor fg = ConsoleColor.Gray;

            // Kiểm tra Cursor của người khác
            var remoteUser = remoteUsers.FirstOrDefault(u => u.CursorPosition == i);
            if (remoteUser != null)
            {
                bg = (ConsoleColor)remoteUser.ColorCode;
                fg = ConsoleColor.Black; // Chữ đen trên nền màu cho dễ đọc
            }

            // Kiểm tra Cursor của mình (đè lên nếu trùng)
            if (i == myCursorIndex)
            {
                bg = ConsoleColor.White;
                fg = ConsoleColor.Black;
            }

            // Ký tự cần vẽ
            char c = (i < text.Length) ? text[i] : ' ';

            // Xử lý xuống dòng
            if (c == '\n')
            {
                // Nếu có cursor ở ngay ký tự \n, ta vẽ 1 khoảng trắng có màu để hiện cursor
                currentRow.Add(new ConsolePixel { Char = ' ', Background = bg, Foreground = fg });

                // Fill nốt phần còn lại của dòng bằng màu đen
                FillRowPadding(currentRow);
                frame.Add(currentRow.ToArray());
                currentRow.Clear();
            }
            else
            {
                currentRow.Add(new ConsolePixel { Char = c, Background = bg, Foreground = fg });

                // Tự động xuống dòng nếu tràn màn hình
                if (currentRow.Count >= consoleWidth)
                {
                    frame.Add(currentRow.ToArray());
                    currentRow.Clear();
                }
            }
        }
        // Thêm dòng cuối cùng (nếu còn dư)
        if (currentRow.Count > 0)
        {
            FillRowPadding(currentRow);
            frame.Add(currentRow.ToArray());
        }

        // Điền nốt các dòng trống bên dưới để xóa rác
        while (frame.Count < consoleHeight)
        {
            frame.Add(CreateTextRow("", ConsoleColor.Black, ConsoleColor.Gray));
        }

        return frame;
    }

    static void FillRowPadding(List<ConsolePixel> row)
    {
        while (row.Count < consoleWidth)
        {
            row.Add(new ConsolePixel { Char = ' ', Background = ConsoleColor.Black, Foreground = ConsoleColor.Gray });
        }
    }

    static ConsolePixel[] CreateTextRow(string text, ConsoleColor bg, ConsoleColor fg)
    {
        var row = new ConsolePixel[consoleWidth];
        for (int i = 0; i < consoleWidth; i++)
        {
            char c = (i < text.Length) ? text[i] : ' ';
            row[i] = new ConsolePixel { Char = c, Background = bg, Foreground = fg };
        }
        return row;
    }

    static ConsolePixel[] CreateUserRow(string name, int colorCode, int pos)
    {
        var row = new List<ConsolePixel>();

        // Vẽ icon màu
        ConsoleColor userColor = (colorCode == -1) ? ConsoleColor.White : (ConsoleColor)colorCode;
        row.Add(new ConsolePixel { Char = ' ', Background = userColor, Foreground = ConsoleColor.Black });
        row.Add(new ConsolePixel { Char = ' ', Background = userColor, Foreground = ConsoleColor.Black }); // 2 ô cho dễ nhìn

        // Vẽ tên
        string info = $" {name} (Pos: {pos})";
        if (colorCode == -1) info += " [YOU]";

        foreach (char c in info)
        {
            row.Add(new ConsolePixel { Char = c, Background = ConsoleColor.Black, Foreground = ConsoleColor.Gray });
        }

        FillRowPadding(row);
        return row.ToArray();
    }

    static int CalculateVerticalMove(int direction)
    {
        string text = localDoc.ToString();
        string[] lines = text.Split('\n');
        int currentPos = 0, currentRow = 0, currentCol = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            int lineLen = lines[i].Length + 1;
            if (myCursorIndex < currentPos + lineLen) { currentRow = i; currentCol = myCursorIndex - currentPos; break; }
            currentPos += lineLen;
        }
        int targetRow = currentRow + direction;
        if (targetRow < 0 || targetRow >= lines.Length) return myCursorIndex;
        int newIndex = 0;
        for (int i = 0; i < targetRow; i++) newIndex += lines[i].Length + 1;
        int targetCol = Math.Min(currentCol, lines[targetRow].Length);
        newIndex += targetCol;
        return newIndex;
    }
}