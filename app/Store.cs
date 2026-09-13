// 대화와 키는 사용자 폴더(LOCALAPPDATA)에 둔다. EXE 옆에 두면 실행할 때마다 파일이 생겨
// "EXE 하나"가 깨지고, Program Files 처럼 쓰기 금지인 곳에서는 아예 저장이 안 된다.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NvidiaConsole;

sealed class Chat
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Title { get; set; } = "새 대화";
    public List<Message> Messages { get; set; } = new();
}

static class Store
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Folder { get; } = Prepare(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    // 이름을 NvidiaConsole 에서 Zako Code 로 바꿨다. 옛 폴더의 대화·키·보낼 곳을 새 폴더로 한 번 복사해 온다 —
    // 이름이 바뀌었다고 기록이 사라지면 안 된다. 옛 파일은 지우지 않고, 새 쪽에 이미 있으면 덮지 않는다.
    public static string Prepare(string localAppData)
    {
        var folder = Directory.CreateDirectory(Path.Combine(localAppData, "ZakoCode")).FullName;
        var old = Path.Combine(localAppData, "NvidiaConsole");
        foreach (var name in new[] { "chats.json", "key.dat", "root.txt" })
        {
            var from = Path.Combine(old, name);
            var to = Path.Combine(folder, name);
            try
            {
                if (File.Exists(from) && !File.Exists(to)) File.Copy(from, to);
            }
            catch (IOException) { }                    // 못 옮겨도 켜지는 게 먼저다 — 옛 파일은 그대로 남아 있다
            catch (UnauthorizedAccessException) { }
        }
        return folder;
    }

    static string ChatsPath => Path.Combine(Folder, "chats.json");
    static string KeyPath => Path.Combine(Folder, "key.dat");

    public static List<Chat> Load()
    {
        try
        {
            return JsonSerializer.Deserialize<List<Chat>>(File.ReadAllText(ChatsPath)) ?? new List<Chat>();
        }
        catch (Exception)
        {
            return new List<Chat>();   // 파일이 없거나 깨졌으면 빈 목록으로 시작한다
        }
    }

    public static void Save(List<Chat> chats) => File.WriteAllText(ChatsPath, JsonSerializer.Serialize(chats, Pretty));

    // 키를 그냥 두면 EXE 옆 파일에 평문으로 남는다. Windows 사용자 계정에 묶어 암호화한다
    // (다른 계정·다른 PC 에서는 풀리지 않는다).
    public static void SaveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            try { File.Delete(KeyPath); } catch (Exception) { }
            return;
        }
        File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    }

    // 보낼 곳은 비밀이 아니라 그냥 파일에 적는다. 비어 있으면 기본값을 쓴다.
    public static void SaveRoot(string url) => File.WriteAllText(Path.Combine(Folder, "root.txt"), url);

    public static string LoadRoot()
    {
        try
        {
            return File.ReadAllText(Path.Combine(Folder, "root.txt")).Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    public static string LoadKey()
    {
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception)
        {
            return "";
        }
    }
}
