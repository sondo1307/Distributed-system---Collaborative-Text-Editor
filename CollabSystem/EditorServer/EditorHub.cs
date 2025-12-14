using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Text;

namespace EditorServer
{
    // --- CÁC CLASS DÙNG CHUNG (MODEL) ---
    public class UserCursor
    {
        public string ConnectionId { get; set; }
        public string UserName { get; set; }
        public int CursorPosition { get; set; }
        public int ColorCode { get; set; }
    }

    public class TextAction
    {
        public string Type { get; set; } // "INSERT" hoặc "DELETE"
        public int Position { get; set; }
        public string Content { get; set; }
    }
    // ------------------------------------

    public class EditorHub : Hub
    {
        private readonly DocumentState _docState;

        // Lưu user vẫn dùng static vì nó gắn liền với connection life-cycle
        private static ConcurrentDictionary<string, UserCursor> _connectedUsers = new ConcurrentDictionary<string, UserCursor>();

        // Constructor: Nhận DocumentState từ hệ thống
        public EditorHub(DocumentState docState)
        {
            _docState = docState;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _connectedUsers.TryRemove(Context.ConnectionId, out _);
            await Clients.Others.SendAsync("UserLeft", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinChat(string userName)
        {
            var user = new UserCursor
            {
                ConnectionId = Context.ConnectionId,
                UserName = userName,
                CursorPosition = 0,
                ColorCode = new Random().Next(1, 14)
            };

            _connectedUsers.TryAdd(Context.ConnectionId, user);

            // Lấy nội dung từ Service thay vì static
            await Clients.Caller.SendAsync("LoadDocument", _docState.Content.ToString());

            await Clients.Caller.SendAsync("UpdateUserList", _connectedUsers.Values.ToList());
            await Clients.Others.SendAsync("UserJoined", user);
        }

        public async Task SendAction(TextAction action)
        {
            // Lock trên đối tượng Content của Service
            lock (_docState.Content)
            {
                try
                {
                    if (action.Type == "INSERT")
                    {
                        int pos = Math.Min(action.Position, _docState.Content.Length);
                        _docState.Content.Insert(pos, action.Content);
                    }
                    else if (action.Type == "DELETE" && action.Position < _docState.Content.Length)
                    {
                        _docState.Content.Remove(action.Position, 1);
                    }
                }
                catch { }
            }
            await Clients.Others.SendAsync("ReceiveAction", action);
        }

        public async Task UpdateCursor(int newPos)
        {
            if (_connectedUsers.TryGetValue(Context.ConnectionId, out UserCursor user))
            {
                user.CursorPosition = newPos;
                await Clients.Others.SendAsync("RemoteCursorMoved", Context.ConnectionId, newPos);
            }
        }

        public async Task RequestSave(string userName)
        {
            try
            {
                string folder = Path.Combine(Directory.GetCurrentDirectory(), "SavedDocs");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                string filePath = Path.Combine(folder, _docState.CurrentFileName);

                lock (_docState.Content)
                {
                    File.WriteAllText(filePath, _docState.Content.ToString());
                }

                string timeStr = DateTime.Now.ToString("HH:mm:ss dd:MM:yyyy");

                // --- THÊM DÒNG NÀY ĐỂ HIỆN LOG TRÊN SERVER ---
                Console.WriteLine($"[LOG] User '{userName}' da luu bai vao luc {timeStr}");
                // ---------------------------------------------

                // Báo lại cho Client biết (kèm tên người lưu để mọi người cùng biết)
                await Clients.All.SendAsync("ReceiveNotification", $"[SYSTEM] {userName} da luu bai vao luc {timeStr}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Luu file that bai: {ex.Message}");
                await Clients.Caller.SendAsync("ReceiveNotification", $"[ERROR] Loi luu file: {ex.Message}");
            }
        }
    }
}
