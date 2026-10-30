using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quadtab.Models;

namespace Quadtab.Services;

[JsonSerializable(typeof(Board))]
[JsonSerializable(typeof(AppState))]
[JsonSerializable(typeof(List<BoardItem>))]
internal partial class BoardJsonContext : JsonSerializerContext;

/// <summary>Um quadro apagado na lixeira.</summary>
public sealed record TrashEntry(string TrashPath, string OriginalRel, DateTime DeletedAt)
{
    public string Name => Path.GetFileNameWithoutExtension(OriginalRel);
}

/// <summary>
/// O cofre: a pasta Documentos\Quadtab com tudo do usuário.
///   Quadros\   um .quadtab (JSON) por quadro, em subpastas = pastas do app
///   Páginas\   páginas em Markdown (.md)
///   Anexos\    imagens, PDFs e outros arquivos colados
///   .lixeira\  itens apagados (30 dias)
///   .quadtab\  estado da interface e miniaturas
/// Toda gravação vai primeiro para um arquivo temporário e depois substitui o original.
/// </summary>
public static class StorageService
{
    public const string BoardExt = ".quadtab";

    static readonly BoardJsonContext Json = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // mantém "ç", "ã" legíveis no arquivo
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    });

    static readonly SemaphoreSlim WriteLock = new(1, 1);

    public static string Root { get; private set; } = "";
    public static string BoardsDir => Path.Combine(Root, "Quadros");
    public static string PagesDir => Path.Combine(Root, "Páginas");
    public static string AttachDir => Path.Combine(Root, "Anexos");
    public static string TrashDir => Path.Combine(Root, ".lixeira");
    public static string MetaDir => Path.Combine(Root, ".quadtab");
    static string StatePath => Path.Combine(MetaDir, "estado.json");

    /// <summary>Registros de erro: dentro do cofre (.quadtab), como todo o resto.</summary>
    public static string LogDir => Root.Length > 0 ? MetaDir : OldDataDir;

    /// <summary>Onde o app antigo guardava o quadro (%AppData%\Quadtab), só para migrar.</summary>
    static string OldDataDir =>
        Environment.GetEnvironmentVariable("QUADTAB_DADOS") is { Length: > 0 } dev ? dev
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quadtab");

    public static AppState State { get; private set; } = new();

    /// <summary>Avisa a interface quando a lista de quadros, pastas, páginas ou lixeira muda.</summary>
    public static event Action? LibraryChanged;

    public static void NotifyLibrary() => LibraryChanged?.Invoke();

    public static void Initialize()
    {
        var dev = Environment.GetEnvironmentVariable("QUADTAB_DADOS");
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultRoot = !string.IsNullOrEmpty(dev) ? dev : Path.Combine(docs, "Cofre");
        if (string.IsNullOrEmpty(dev)) MigrateOldVault(Path.Combine(docs, "Quadtab"), defaultRoot);

        // Tudo fica em Documentos\Cofre; o estado pode apontar para outro cofre (Ajustes).
        Root = defaultRoot;
        State = ReadJson(StatePath, Json.AppState) ?? new AppState();
        if (!string.IsNullOrEmpty(State.Cofre) && Directory.Exists(State.Cofre)) Root = State.Cofre;
        else State.Cofre = null;

        foreach (var d in new[] { BoardsDir, PagesDir, AttachDir, TrashDir, MetaDir }) Directory.CreateDirectory(d);
        try { File.SetAttributes(TrashDir, FileAttributes.Directory | FileAttributes.Hidden); } catch { }
        try { File.SetAttributes(MetaDir, FileAttributes.Directory | FileAttributes.Hidden); } catch { }

        MigrateOldBoard();
        if (!BoardFiles().Any()) CreateBoard("", "Meu quadro", Board.CreateDefault());
        PurgeTrash();
    }

    /// <summary>Troca a pasta do cofre (Ajustes).</summary>
    public static void ChangeVault(string folder)
    {
        State.Cofre = folder;
        State.QuadroAberto = null;
        SaveState();
        Initialize();
        NotifyLibrary();
    }

    /// <summary>A versão anterior usava Documentos\Quadtab: a pasta inteira passa a se chamar Cofre.</summary>
    static void MigrateOldVault(string old, string vault)
    {
        try
        {
            if (!Directory.Exists(old) || !Directory.Exists(Path.Combine(old, "Quadros"))) return;
            if (Directory.Exists(vault) && Directory.EnumerateFileSystemEntries(vault).Any()) return;
            if (Directory.Exists(vault)) Directory.Delete(vault);
            Directory.Move(old, vault);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // O app antigo guardava um único quadro em %AppData%\Quadtab\quadro.json.
    static void MigrateOldBoard()
    {
        var old = Path.Combine(OldDataDir, "quadro.json");
        if (!File.Exists(old) || BoardFiles().Any()) return;
        var b = ReadJson(old, Json.Board);
        if (b is null) return;
        CreateBoard("", string.IsNullOrWhiteSpace(b.Titulo) ? "Meu quadro" : b.Titulo, b);
        try { File.Move(old, old + ".migrado", true); } catch { }
    }

    public static void SaveState() => WriteText(StatePath, JsonSerializer.Serialize(State, Json.AppState));

    // ---------- caminhos ----------

    public static string Rel(string full) => Path.GetRelativePath(Root, full);
    public static string Full(string rel) => Path.IsPathRooted(rel) ? rel : Path.Combine(Root, rel);

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(s) ? "Sem título" : s;
    }

    static string Unique(string folder, string name, string ext)
    {
        var p = Path.Combine(folder, name + ext);
        for (int i = 2; File.Exists(p) || Directory.Exists(p); i++) p = Path.Combine(folder, $"{name} {i}{ext}");
        return p;
    }

    // ---------- quadros ----------

    public static IEnumerable<string> BoardFiles() =>
        Directory.Exists(BoardsDir) ? Directory.EnumerateFiles(BoardsDir, "*" + BoardExt, SearchOption.AllDirectories) : [];

    /// <summary>Pastas de quadros, relativas a Quadros\ (ex.: "Viagens", "Viagens\2027").</summary>
    public static IEnumerable<string> BoardFolders() =>
        Directory.EnumerateDirectories(BoardsDir, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(BoardsDir, d));

    public static Board LoadBoard(string path)
    {
        var b = ReadJson(path, Json.Board);
        if (b is not null) return b;
        if (File.Exists(path))
            try { File.Copy(path, path + $".erro-{DateTime.Now:yyyyMMdd-HHmmss}", true); } catch { }
        return new Board { Titulo = Path.GetFileNameWithoutExtension(path) };
    }

    public static string Serialize(Board board) => JsonSerializer.Serialize(board, Json.Board);
    public static string SerializeItems(List<BoardItem> items) => JsonSerializer.Serialize(items, Json.ListBoardItem);
    public static List<BoardItem> DeserializeItems(string json) =>
        JsonSerializer.Deserialize(json, Json.ListBoardItem) ?? [];

    public static Task SaveBoardAsync(string path, Board board)
    {
        var text = Serialize(board);
        return Task.Run(() => WriteText(path, text));
    }

    public static void SaveBoard(string path, Board board) => WriteText(path, Serialize(board));

    public static string CreateBoard(string folderRel, string name, Board? content = null)
    {
        var folder = Path.Combine(BoardsDir, folderRel);
        Directory.CreateDirectory(folder);
        var path = Unique(folder, SafeName(name), BoardExt);
        var board = content ?? new Board { Itens = [] };
        board.Titulo = Path.GetFileNameWithoutExtension(path);
        SaveBoard(path, board);
        NotifyLibrary();
        return path;
    }

    public static string RenameBoard(string path, string newName)
    {
        var target = Unique(Path.GetDirectoryName(path)!, SafeName(newName), BoardExt);
        if (string.Equals(Path.GetFileNameWithoutExtension(path), SafeName(newName), StringComparison.Ordinal)) return path;
        File.Move(path, target);
        ReplaceInState(path, target);
        NotifyLibrary();
        return target;
    }

    public static string DuplicateBoard(string path)
    {
        var b = LoadBoard(path);
        var copy = CreateBoard(Path.GetRelativePath(BoardsDir, Path.GetDirectoryName(path)!) is "." ? "" :
            Path.GetRelativePath(BoardsDir, Path.GetDirectoryName(path)!), Path.GetFileNameWithoutExtension(path) + " (cópia)", b);
        return copy;
    }

    public static string MoveBoard(string path, string folderRel)
    {
        var folder = Path.Combine(BoardsDir, folderRel);
        Directory.CreateDirectory(folder);
        if (string.Equals(Path.GetFullPath(folder).TrimEnd('\\'), Path.GetDirectoryName(Path.GetFullPath(path))!.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return path;
        var target = Unique(folder, Path.GetFileNameWithoutExtension(path), BoardExt);
        File.Move(path, target);
        ReplaceInState(path, target);
        NotifyLibrary();
        return target;
    }

    public static void CreateFolder(string parentRel, string name)
    {
        var p = Unique(Path.Combine(BoardsDir, parentRel), SafeName(name), "");
        Directory.CreateDirectory(p);
        NotifyLibrary();
    }

    public static void RenameFolder(string rel, string newName)
    {
        var src = Path.Combine(BoardsDir, rel);
        var target = Unique(Path.GetDirectoryName(src)!, SafeName(newName), "");
        Directory.Move(src, target);
        foreach (var f in Directory.EnumerateFiles(target, "*" + BoardExt, SearchOption.AllDirectories))
            ReplaceInState(Path.Combine(src, Path.GetRelativePath(target, f)), f);
        NotifyLibrary();
    }

    /// <summary>Apaga uma pasta: os quadros dela vão para a lixeira.</summary>
    public static void DeleteFolder(string rel)
    {
        var dir = Path.Combine(BoardsDir, rel);
        foreach (var f in Directory.EnumerateFiles(dir, "*" + BoardExt, SearchOption.AllDirectories).ToList()) TrashBoard(f, notify: false);
        try { Directory.Delete(dir, true); } catch { }
        NotifyLibrary();
    }

    static void ReplaceInState(string oldPath, string newPath)
    {
        string o = Rel(oldPath), n = Rel(newPath);
        for (int i = 0; i < State.Recentes.Count; i++) if (State.Recentes[i] == o) State.Recentes[i] = n;
        for (int i = 0; i < State.Favoritos.Count; i++) if (State.Favoritos[i] == o) State.Favoritos[i] = n;
        if (State.QuadroAberto == o) State.QuadroAberto = n;
        var oldThumb = ThumbPath(oldPath);
        if (File.Exists(oldThumb)) try { File.Move(oldThumb, ThumbPath(newPath), true); } catch { }
        SaveState();
    }

    public static void Touch(string path)
    {
        var rel = Rel(path);
        State.Recentes.Remove(rel);
        State.Recentes.Insert(0, rel);
        if (State.Recentes.Count > 12) State.Recentes.RemoveRange(12, State.Recentes.Count - 12);
        State.QuadroAberto = rel;
        SaveState();
    }

    public static bool IsFavorite(string path) => State.Favoritos.Contains(Rel(path));

    public static bool IsPinned(string path) => State.Fixados.Contains(Rel(path));

    public static void TogglePinned(string path)
    {
        var rel = Rel(path);
        if (!State.Fixados.Remove(rel)) State.Fixados.Insert(0, rel);
        SaveState();
        NotifyLibrary();
    }

    public static void ToggleFavorite(string path)
    {
        var rel = Rel(path);
        if (!State.Favoritos.Remove(rel)) State.Favoritos.Add(rel);
        SaveState();
        NotifyLibrary();
    }

    // ---------- lixeira (30 dias) ----------

    public static void TrashBoard(string path, bool notify = true)
    {
        var rel = Path.GetRelativePath(BoardsDir, path).Replace('\\', '¦');
        Directory.CreateDirectory(TrashDir);
        var target = Path.Combine(TrashDir, $"{DateTime.Now:yyyyMMddHHmmss}__{rel}");
        File.Move(path, target, true);
        State.Recentes.Remove(Rel(path));
        State.Favoritos.Remove(Rel(path));
        SaveState();
        if (notify) NotifyLibrary();
    }

    public static IEnumerable<TrashEntry> TrashEntries()
    {
        if (!Directory.Exists(TrashDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(TrashDir, "*" + BoardExt))
        {
            var name = Path.GetFileName(f);
            int i = name.IndexOf("__", StringComparison.Ordinal);
            if (i < 0) continue;
            if (!DateTime.TryParseExact(name[..i], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var when)) continue;
            yield return new TrashEntry(f, name[(i + 2)..].Replace('¦', '\\'), when);
        }
    }

    public static string RestoreBoard(TrashEntry e)
    {
        var target = Path.Combine(BoardsDir, e.OriginalRel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        target = Unique(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target), BoardExt);
        File.Move(e.TrashPath, target);
        NotifyLibrary();
        return target;
    }

    public static void DeleteForever(TrashEntry e)
    {
        try { File.Delete(e.TrashPath); } catch { }
        NotifyLibrary();
    }

    static void PurgeTrash()
    {
        foreach (var e in TrashEntries().ToList())
            if ((DateTime.Now - e.DeletedAt).TotalDays > 30)
                try { File.Delete(e.TrashPath); } catch { }
    }

    // ---------- páginas (.md) ----------

    public static IEnumerable<string> PageFiles() =>
        Directory.Exists(PagesDir) ? Directory.EnumerateFiles(PagesDir, "*.md", SearchOption.AllDirectories) : [];

    public static IEnumerable<string> PageFolders() =>
        Directory.EnumerateDirectories(PagesDir, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(PagesDir, d));

    /// <summary>Acha uma página pelo nome (sem extensão, sem diferenciar maiúsculas).</summary>
    public static string? FindPage(string name) =>
        PageFiles().FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ReadPage(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    public static void WritePage(string path, string text) => WriteText(path, text);

    public static string CreatePage(string name, string content = "", string folderRel = "")
    {
        var folder = Path.Combine(PagesDir, folderRel);
        Directory.CreateDirectory(folder);
        var path = Unique(folder, SafeName(name), ".md");
        WriteText(path, content);
        NotifyLibrary();
        return path;
    }

    public static string RenamePage(string path, string newName)
    {
        if (string.Equals(Path.GetFileNameWithoutExtension(path), SafeName(newName), StringComparison.Ordinal)) return path;
        var target = Unique(Path.GetDirectoryName(path)!, SafeName(newName), ".md");
        File.Move(path, target);
        NotifyLibrary();
        return target;
    }

    public static void TrashPage(string path)
    {
        Directory.CreateDirectory(Path.Combine(TrashDir, "Páginas"));
        File.Move(path, Path.Combine(TrashDir, "Páginas", $"{DateTime.Now:yyyyMMddHHmmss}__{Path.GetFileName(path)}"), true);
        NotifyLibrary();
    }

    // ---------- anexos ----------

    public static string SaveAttachment(byte[] data, string name)
    {
        Directory.CreateDirectory(AttachDir);
        var path = Unique(AttachDir, SafeName(Path.GetFileNameWithoutExtension(name)), Path.GetExtension(name));
        File.WriteAllBytes(path, data);
        return Rel(path);
    }

    public static string ImportAttachment(string file)
    {
        Directory.CreateDirectory(AttachDir);
        var path = Unique(AttachDir, SafeName(Path.GetFileNameWithoutExtension(file)), Path.GetExtension(file));
        File.Copy(file, path);
        return Rel(path);
    }

    public static string ThumbPath(string boardPath)
    {
        var name = Rel(boardPath).Replace('\\', '¦');
        return Path.Combine(MetaDir, "miniaturas", name + ".png");
    }

    // ---------- leitura e escrita segura ----------

    static T? ReadJson<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), info) : null; }
        catch { return null; }
    }

    /// <summary>Grava num temporário e troca no fim (nunca deixa o arquivo pela metade).</summary>
    public static void WriteText(string path, string text)
    {
        WriteLock.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { WriteLock.Release(); }
    }
}
