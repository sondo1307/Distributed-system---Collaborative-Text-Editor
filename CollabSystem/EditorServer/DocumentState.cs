using System.Text;

namespace EditorServer
{
    // Class này chứa dữ liệu toàn cục của ứng dụng
    public class DocumentState
    {
        public StringBuilder Content { get; set; } = new StringBuilder();
        public string CurrentFileName { get; set; } = "moi_tao.txt"; // Tên file mặc định
    }
}