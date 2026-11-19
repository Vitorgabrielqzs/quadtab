using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quadtab.Models;

namespace Quadtab.Services;

// Aqui usamos o BoardJsonContext para gerar o código do JSON na compilação (mais rápido e sem reflexão).
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
/// Aqui usamos a classe StorageService para ler e gravar o cofre: a pasta Documentos\Cofre com tudo do usuário.
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

    // Uma gravação por vez (o salvamento automático roda em outra thread).
    static readonly SemaphoreSlim WriteLock = new(1, 1);

    // ---------- pastas do cofre ----------
    public static string Root { get; private set; } = "";
    public static string BoardsDir => Path.Combine(Root, "Quadros");
    /// <summary>Notas (.md). A pasta se chamava "Páginas"; é renomeada ao abrir.</summary>
    public static string PagesDir => Path.Combine(Root, "Notas");
    static string OldPagesDir => Path.Combine(Root, "Páginas");
    // Estrutura do cofre (todas as pastas visíveis no Explorador):
    //   Quadros\                        quadros (.quadtab), com as pastas do usuário
    //   Notas\                          notas (.md)
    //   Anexos\Imagens, Vídeos, Arquivos, Prévias
    //   Lixeira\Quadros, Notas, Arquivos (some de vez depois de 30 dias)
    //   Sistema\estado.json, Miniaturas\, Registros\ (erros.log), Temporários\
    // Até a versão 1.3.1 a lixeira era ".lixeira" e o sistema ".quadtab" (ocultas): MigrateLayout muda ao abrir.
    public static string AttachDir => Path.Combine(Root, "Anexos");
    public static string TrashDir => Path.Combine(Root, "Lixeira");
    public static string MetaDir => Path.Combine(Root, "Sistema");
    static string TrashBoardsDir => Path.Combine(TrashDir, "Quadros");
    static string TrashNotesDir => Path.Combine(TrashDir, "Notas");
    static string TrashFilesDir => Path.Combine(TrashDir, "Arquivos");
    static string ThumbDir => Path.Combine(MetaDir, "Miniaturas");
    static string TempDir => Path.Combine(MetaDir, "Temporários");
    static string StatePath => Path.Combine(MetaDir, "estado.json");

    /// <summary>"Quadro" usado quando todos foram apagados (tela com "Criar novo quadro"); não aparece na lista.</summary>
    public static string EmptyBoardPath => Path.Combine(TempDir, "vazio" + BoardExt);
    /// <summary>Caminho de mentira da prévia de um design (nunca é gravado nem aparece na lista).</summary>
    public static string PreviewBoardPath => Path.Combine(TempDir, "previa" + BoardExt);

    /// <summary>Lixeira e Sistema: aparecem no Explorador, mas não na árvore do Cofre dentro do app.</summary>
    public static bool IsInternalDir(string dir) =>
        SamePath(dir, Path.GetFullPath(TrashDir)) || SamePath(dir, Path.GetFullPath(MetaDir));
    public static bool IsEmptyBoard(string path) => string.Equals(path, EmptyBoardPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Registros de erro: dentro do cofre (Sistema\Registros), como todo o resto.</summary>
    public static string LogDir => Root.Length > 0 ? Path.Combine(MetaDir, "Registros") : OldDataDir;

    /// <summary>Onde o app antigo guardava o quadro (%AppData%\Quadtab), só para migrar.</summary>
    static string OldDataDir =>
        Environment.GetEnvironmentVariable("QUADTAB_DADOS") is { Length: > 0 } dev ? dev
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quadtab");

    public static AppState State { get; private set; } = new();

    /// <summary>Avisa a interface quando a lista de quadros, pastas, páginas ou lixeira muda.</summary>
    public static event Action? LibraryChanged;

    public static void NotifyLibrary() => LibraryChanged?.Invoke();

    /// <summary>Uma gravação falhou (disco cheio, arquivo travado, sem permissão): a interface avisa o usuário.</summary>
    public static event Action<string, Exception>? WriteFailed;

    // ---------- abertura e migrações ----------

    /// <summary>Escolhe o cofre, migra versões antigas, cria as pastas e o quadro de exemplo (cofre novo).</summary>
    public static void Initialize()
    {
        var dev = Environment.GetEnvironmentVariable("QUADTAB_DADOS");
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultRoot = !string.IsNullOrEmpty(dev) ? dev : Path.Combine(docs, "Cofre");
        if (string.IsNullOrEmpty(dev)) MigrateOldVault(Path.Combine(docs, "Quadtab"), defaultRoot);

        // Tudo fica em Documentos\Cofre; o estado pode apontar para outro cofre (Ajustes).
        Root = defaultRoot;
        MigrateLayout();
        bool freshVault = !File.Exists(StatePath);
        State = ReadJson(StatePath, Json.AppState) ?? Damaged(StatePath, new AppState());
        // estado.json editado à mão: listas nulas viram listas vazias.
        State.Recentes ??= [];
        State.Favoritos ??= [];
        State.Fixados ??= [];
        State.Abertos ??= [];
        State.CoresRecentes ??= [];
        State.CoresLinhaRecentes ??= [];
        State.OrdemPastas ??= [];
        State.EstiloLinha ??= new();
        if (!string.IsNullOrEmpty(State.Cofre) && Directory.Exists(State.Cofre))
        {
            Root = State.Cofre;
            MigrateLayout(); // o outro cofre também pode estar no formato antigo
        }
        else State.Cofre = null;

        Directory.CreateDirectory(MetaDir);
        MigratePagesFolder();
        foreach (var d in new[] { BoardsDir, PagesDir, AttachDir, TrashBoardsDir, TrashNotesDir, MetaDir, ThumbDir })
            Directory.CreateDirectory(d);

        MigrateOldBoard();
        // Só um cofre novo ganha o quadro de exemplo; quem apagou todos vê "Criar novo quadro".
        if (freshVault && !BoardFiles().Any()) CreateBoard("", "Meu quadro", Board.CreateDefault());
        PurgeTrash();
    }

    /// <summary>
    /// "Páginas" vira "Notas". Também apaga as cópias "*.md.erro-*" que uma versão anterior criava por
    /// engano (tentava abrir notas como se fossem quadros; as cópias são idênticas às notas).
    /// </summary>
    static void MigratePagesFolder()
    {
        try
        {
            if (Directory.Exists(OldPagesDir) && !Directory.Exists(PagesDir))
            {
                Directory.Move(OldPagesDir, PagesDir);
                // Recentes, fixados e importantes guardam caminhos relativos: acompanham o novo nome.
                foreach (var list in new[] { State.Recentes, State.Fixados, State.Favoritos })
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].StartsWith("Páginas", StringComparison.Ordinal)) list[i] = "Notas" + list[i][7..];
                SaveState();
            }
            if (Directory.Exists(PagesDir))
                foreach (var f in Directory.EnumerateFiles(PagesDir, "*.md.erro-*", SearchOption.AllDirectories).ToList())
                    File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Estrutura antiga (até a 1.3.1) → nova: ".lixeira" vira "Lixeira" (quadros em Lixeira\Quadros, notas e páginas
    /// em Lixeira\Notas), ".quadtab" vira "Sistema" (miniaturas, registros e temporários em subpastas), os anexos
    /// soltos vão para Anexos\Imagens, Vídeos ou Arquivos e a pasta "Páginas" vazia some. Nada é apagado: se um
    /// arquivo não puder sair (travado pelo OneDrive, por exemplo), fica onde está e a próxima abertura tenta de novo.
    /// </summary>
    static void MigrateLayout()
    {
        try
        {
            var oldTrash = Path.Combine(Root, ".lixeira");
            if (Directory.Exists(oldTrash))
            {
                MoveFiles(oldTrash, TrashBoardsDir, "*" + BoardExt);
                MoveTree(Path.Combine(oldTrash, "Notas"), TrashNotesDir);
                MoveTree(Path.Combine(oldTrash, "Páginas"), TrashNotesDir);
                MoveTree(Path.Combine(oldTrash, "Arquivos"), TrashFilesDir);
                MoveTree(oldTrash, TrashDir); // o que mais houver
            }

            var oldMeta = Path.Combine(Root, ".quadtab");
            if (Directory.Exists(oldMeta))
            {
                MoveTree(Path.Combine(oldMeta, "miniaturas"), ThumbDir);
                MoveFiles(oldMeta, Path.Combine(MetaDir, "Registros"), "*.log");
                foreach (var temp in new[] { "vazio", "previa" })
                    MoveFile(Path.Combine(oldMeta, temp + BoardExt), Path.Combine(TempDir, temp + BoardExt));
                MoveTree(oldMeta, MetaDir); // estado.json e o resto
            }
            // O quadro vazio antigo era aberto por ".quadtab\vazio.quadtab".
            ReplaceStatePrefix(@".quadtab\", @"Sistema\Temporários\");

            // Anexos soltos na raiz de Anexos (versões antigas) vão para a subpasta do tipo.
            if (Directory.Exists(AttachDir))
                foreach (var f in Directory.EnumerateFiles(AttachDir).ToList())
                    MoveFile(f, Path.Combine(AttachDir, AttachSubdir(f), Path.GetFileName(f)));

            DeleteIfEmpty(OldPagesDir);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Troca o começo dos caminhos guardados no estado.json (direto no texto: o estado ainda não foi carregado).
    static void ReplaceStatePrefix(string oldPrefix, string newPrefix)
    {
        if (!File.Exists(StatePath)) return;
        var text = File.ReadAllText(StatePath);
        // No JSON a barra invertida aparece dobrada.
        string oldJson = "\"" + oldPrefix.Replace(@"\", @"\\"), newJson = "\"" + newPrefix.Replace(@"\", @"\\");
        if (!text.Contains(oldJson, StringComparison.Ordinal)) return;
        WriteText(StatePath, text.Replace(oldJson, newJson, StringComparison.Ordinal));
    }

    // Move os arquivos de uma pasta (só o primeiro nível) que combinam com o filtro.
    static void MoveFiles(string from, string to, string pattern)
    {
        if (!Directory.Exists(from)) return;
        foreach (var f in Directory.EnumerateFiles(from, pattern).ToList()) MoveFile(f, Path.Combine(to, Path.GetFileName(f)));
    }

    // Move uma pasta inteira para dentro de outra, mesclando com o que já existe lá; a de origem some se esvaziar.
    static void MoveTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).ToList())
            MoveFile(f, Path.Combine(to, Path.GetRelativePath(from, f)));
        foreach (var d in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            DeleteIfEmpty(d);
        DeleteIfEmpty(from);
    }

    // Move um arquivo; se o destino já existe, o nome ganha um número (nada é sobrescrito).
    static void MoveFile(string from, string to)
    {
        if (!File.Exists(from)) return;
        try
        {
            var dir = Path.GetDirectoryName(to)!;
            Directory.CreateDirectory(dir);
            if (File.Exists(to)) to = Unique(dir, Path.GetFileNameWithoutExtension(to), Path.GetExtension(to));
            File.Move(from, to);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    static void DeleteIfEmpty(string dir)
    {
        try { if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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

    /// <summary>Cópia independente de um item (passa pelo JSON, então copia tudo, inclusive linhas e tabelas).</summary>
    public static BoardItem CloneItem(BoardItem m) =>
        JsonSerializer.Deserialize(JsonSerializer.Serialize(new List<BoardItem> { m }, Json.ListBoardItem), Json.ListBoardItem)![0];

    // ---------- caminhos ----------

    /// <summary>Caminho relativo ao cofre (é o que vai para o JSON) e o caminho completo de volta.</summary>
    public static string Rel(string full) => Path.GetRelativePath(Root, full);
    public static string Full(string rel)
    {
        if (Path.IsPathRooted(rel)) return rel;
        // Quadros antigos guardam "Páginas\nota.md": a pasta agora se chama "Notas".
        if (rel.StartsWith("Páginas\\", StringComparison.Ordinal) || rel.StartsWith("Páginas/", StringComparison.Ordinal))
            rel = "Notas" + rel[7..];
        var full = Path.Combine(Root, rel);
        // Quadros antigos guardam "Anexos\foto.png": o arquivo agora mora na subpasta do tipo (Anexos\Imagens\foto.png).
        if ((rel.StartsWith("Anexos\\", StringComparison.Ordinal) || rel.StartsWith("Anexos/", StringComparison.Ordinal))
            && rel.IndexOfAny(['\\', '/'], 7) < 0 && !File.Exists(full))
        {
            var moved = Path.Combine(AttachDir, AttachSubdir(rel), rel[7..]);
            if (File.Exists(moved)) return moved;
        }
        return full;
    }

    /// <summary>Nome sem caracteres proibidos pelo Windows.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var s = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(s) ? "Sem título" : s;
    }

    /// <summary>
    /// Destino de um renomear: o mesmo caminho (nada muda), o mesmo nome com outras maiúsculas
    /// (o Windows considera igual, então não ganha número) ou um nome livre.
    /// </summary>
    static string RenameTarget(string path, string newName, string ext)
    {
        var safe = SafeName(newName);
        var dir = Path.GetDirectoryName(path)!;
        var current = ext.Length > 0 ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
        if (string.Equals(current, safe, StringComparison.Ordinal)) return path;
        if (string.Equals(current, safe, StringComparison.OrdinalIgnoreCase)) return Path.Combine(dir, safe + ext);
        return Unique(dir, safe, ext);
    }

    // Caminho livre: "nome", "nome 2", "nome 3"...
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

    /// <summary>Lê um quadro; arquivo estragado ganha uma cópia ".erro-data" e abre vazio.</summary>
    public static Board LoadBoard(string path)
    {
        // Só arquivos de quadro: uma nota (.md) nunca é lida como quadro (nem gera cópia de erro).
        if (!path.EndsWith(BoardExt, StringComparison.OrdinalIgnoreCase)) return new Board { Titulo = Path.GetFileNameWithoutExtension(path) };
        var b = ReadBoard(path);
        if (b is not null) return b;
        return Damaged(path, new Board { Titulo = Path.GetFileNameWithoutExtension(path) });
    }

    /// <summary>Lê um quadro sem efeitos colaterais: null se o arquivo não existe ou está estragado.</summary>
    public static Board? ReadBoard(string path) => ReadJson(path, Json.Board) is { } b ? Clean(b, path) : null;

    // Arquivo editado à mão: listas e textos nulos viram vazios (a lateral, a busca e a tela contam com eles).
    static Board Clean(Board b, string path)
    {
        b.Itens = (b.Itens ?? []).Where(i => i is not null).ToList();
        b.Cenas = (b.Cenas ?? []).Where(c => c is not null).ToList();
        b.Titulo ??= Path.GetFileNameWithoutExtension(path);
        b.Modelo ??= "liso";
        foreach (var i in b.Itens)
        {
            i.Tipo ??= "postit";
            i.Cor ??= "amarelo";
            i.Fixacao ??= "alfinete";
            i.Titulo ??= "";
            i.Texto ??= "";
        }
        return b;
    }

    // Arquivo estragado: guarda uma cópia ".erro-data" (uma só por versão do arquivo) e segue com o valor padrão.
    static T Damaged<T>(string path, T fallback)
    {
        if (!File.Exists(path)) return fallback;
        try
        {
            var stamp = File.GetLastWriteTime(path);
            var dir = Path.GetDirectoryName(path)!;
            // A cópia herda a data do original: se já existe uma com a mesma data, essa versão já foi guardada.
            bool saved = Directory.EnumerateFiles(dir, Path.GetFileName(path) + ".erro-*").Any(f => File.GetLastWriteTime(f) == stamp);
            if (!saved) File.Copy(path, path + $".erro-{DateTime.Now:yyyyMMdd-HHmmss}", true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return fallback;
    }

    public static string Serialize(Board board) => JsonSerializer.Serialize(board, Json.Board);
    public static string SerializeItems(List<BoardItem> items) => JsonSerializer.Serialize(items, Json.ListBoardItem);
    public static List<BoardItem> DeserializeItems(string json) =>
        JsonSerializer.Deserialize(json, Json.ListBoardItem) ?? [];

    /// <summary>Grava o quadro em segundo plano; devolve false se não conseguiu.</summary>
    public static Task<bool> SaveBoardAsync(string path, Board board)
    {
        var text = Serialize(board);
        return Task.Run(() => WriteText(path, text));
    }

    public static bool SaveBoard(string path, Board board) => WriteText(path, Serialize(board));

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
        var target = RenameTarget(path, newName, BoardExt);
        if (target == path) return path;
        File.Move(path, target);
        ReplaceInState(path, target);
        NotifyLibrary();
        return target;
    }

    public static string DuplicateBoard(string path)
    {
        var b = LoadBoard(path);
        // A cópia ganha as próprias notas: mexer ou apagar em um quadro não afeta o outro.
        foreach (var it in b.Itens)
            if (it is { Tipo: "pagina", Arquivo: { } rel } && File.Exists(Full(rel)))
                it.Arquivo = Rel(CreatePage(Path.GetFileNameWithoutExtension(rel), ReadPage(Full(rel))));
        return CreateBoard(FolderKey(path), Path.GetFileNameWithoutExtension(path) + " (cópia)", b);
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

    public static string CreateFolder(string parentRel, string name)
    {
        var p = Unique(Path.Combine(BoardsDir, parentRel), SafeName(name), "");
        Directory.CreateDirectory(p);
        NotifyLibrary();
        return p;
    }

    /// <summary>Renomeia uma pasta de quadros; devolve o caminho novo. A ordem dos quadros dentro dela continua.</summary>
    public static string RenameFolder(string rel, string newName)
    {
        var src = Path.Combine(BoardsDir, rel);
        var target = RenameTarget(src, newName, "");
        if (target == src) return src;
        if (string.Equals(src, target, StringComparison.OrdinalIgnoreCase))
        {
            // Só as maiúsculas mudam: o Windows vê o mesmo nome, então passa por um nome temporário.
            var tmp = src + ".renomeando";
            Directory.Move(src, tmp);
            Directory.Move(tmp, target);
        }
        else Directory.Move(src, target);
        foreach (var f in Directory.EnumerateFiles(target, "*" + BoardExt, SearchOption.AllDirectories))
            ReplaceInState(Path.Combine(src, Path.GetRelativePath(target, f)), f, order: false);
        // A ordem salva acompanha o nome novo (a pasta e as subpastas dela).
        var newRel = FolderKey(target);
        foreach (var key in State.OrdemPastas.Keys.ToList())
            if (string.Equals(key, rel, StringComparison.OrdinalIgnoreCase) || key.StartsWith(rel + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var list = State.OrdemPastas[key];
                State.OrdemPastas.Remove(key);
                State.OrdemPastas[newRel + key[rel.Length..]] = list;
            }
        SaveState();
        NotifyLibrary();
        return target;
    }

    /// <summary>Pasta de um quadro (ou a própria pasta), relativa a Quadros\. "" = a raiz.</summary>
    public static string FolderKey(string path)
    {
        var dir = path.EndsWith(BoardExt, StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(path)! : path;
        var rel = Path.GetRelativePath(BoardsDir, dir);
        return rel == "." ? "" : rel.TrimEnd(Path.DirectorySeparatorChar);
    }

    /// <summary>Quadros de uma pasta (sem as subpastas), na ordem salva; os que não estão na lista vêm depois, pelo nome.</summary>
    public static List<string> FolderBoards(string folder)
    {
        if (!Directory.Exists(folder)) return [];
        var files = Directory.EnumerateFiles(folder, "*" + BoardExt).ToList();
        var order = State.OrdemPastas.TryGetValue(FolderKey(folder), out var o) ? o : [];
        int Rank(string f)
        {
            int i = order.FindIndex(n => string.Equals(n, Path.GetFileName(f), StringComparison.OrdinalIgnoreCase));
            return i < 0 ? int.MaxValue : i;
        }
        return files.OrderBy(Rank).ThenBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Grava a ordem dos quadros de uma pasta (caminhos completos, na ordem nova).</summary>
    public static void SetFolderOrder(string folder, IEnumerable<string> boards)
    {
        var key = FolderKey(folder);
        if (key.Length == 0) return; // a raiz segue a ordem do último aberto
        State.OrdemPastas[key] = boards.Select(Path.GetFileName).OfType<string>().ToList();
        SaveState();
    }

    /// <summary>Apaga uma pasta: os quadros dela vão para a lixeira.</summary>
    public static void DeleteFolder(string rel)
    {
        var dir = Path.Combine(BoardsDir, rel);
        if (!Directory.Exists(dir)) return;
        foreach (var f in Directory.EnumerateFiles(dir, "*" + BoardExt, SearchOption.AllDirectories).ToList()) TrashBoard(f, notify: false);
        RemoveFolder(dir);
        NotifyLibrary();
    }

    /// <summary>
    /// Tira uma pasta (já sem os quadros) do cofre: arquivos que não são quadros (colocados pelo Explorador)
    /// vão para Lixeira\Arquivos, nunca são apagados direto; depois somem as pastas vazias.
    /// </summary>
    public static void RemoveFolder(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var bin = TrashFilesDir;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList())
        {
            try
            {
                Directory.CreateDirectory(bin);
                File.Move(f, TrashTarget(bin, Path.GetRelativePath(BoardsDir, f).Replace('\\', '¦')));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // Só pastas vazias (das mais fundas para cima): o que não pôde sair continua lá.
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(dir).ToList())
            try { Directory.Delete(d); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // Nome livre na lixeira: "data__nome"; se já existe um apagado no mesmo segundo, o segundo seguinte.
    static string TrashTarget(string dir, string name)
    {
        var when = DateTime.Now;
        string p;
        while (File.Exists(p = Path.Combine(dir, $"{when:yyyyMMddHHmmss}__{name}"))) when = when.AddSeconds(1);
        return p;
    }

    // Um quadro mudou de nome ou de pasta: atualiza recentes, favoritos, fixados, ordem das pastas e miniatura.
    static void ReplaceInState(string oldPath, string newPath, bool order = true)
    {
        string o = Rel(oldPath), n = Rel(newPath);
        foreach (var list in new[] { State.Recentes, State.Favoritos, State.Fixados })
            for (int i = 0; i < list.Count; i++) if (list[i] == o) list[i] = n;
        if (order)
        {
            // Ordem das pastas: renomear mantém o lugar; mudar de pasta tira da antiga e põe no fim da nova.
            string oldKey = FolderKey(oldPath), newKey = FolderKey(newPath);
            string oldName = Path.GetFileName(oldPath), newName = Path.GetFileName(newPath);
            if (State.OrdemPastas.TryGetValue(oldKey, out var from))
            {
                int i = from.FindIndex(x => string.Equals(x, oldName, StringComparison.OrdinalIgnoreCase));
                if (i >= 0 && string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)) from[i] = newName;
                else if (i >= 0) from.RemoveAt(i);
            }
            if (newKey.Length > 0 && !string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
            {
                if (!State.OrdemPastas.TryGetValue(newKey, out var to)) State.OrdemPastas[newKey] = to = FolderBoards(Path.Combine(BoardsDir, newKey))
                    .Where(f => !string.Equals(f, newPath, StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OfType<string>().ToList();
                if (!to.Contains(newName, StringComparer.OrdinalIgnoreCase)) to.Add(newName);
            }
        }
        if (State.QuadroAberto == o) State.QuadroAberto = n;
        var oldThumb = ThumbPath(oldPath);
        if (File.Exists(oldThumb)) try { File.Move(oldThumb, ThumbPath(newPath), true); } catch { }
        SaveState();
    }

    /// <summary>Quadro aberto agora: vai para o topo dos recentes (até 12).</summary>
    public static void Touch(string path)
    {
        var rel = Rel(path);
        State.Recentes.Remove(rel);
        State.Recentes.Insert(0, rel);
        if (State.Recentes.Count > 12) State.Recentes.RemoveRange(12, State.Recentes.Count - 12);
        State.QuadroAberto = rel;
        SaveState();
    }

    // ---------- importantes e fixados ----------
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

    /// <summary>Move o quadro para a lixeira (o nome guarda a data e a pasta de origem).</summary>
    public static void TrashBoard(string path, bool notify = true)
    {
        var rel = Path.GetRelativePath(BoardsDir, path).Replace('\\', '¦');
        Directory.CreateDirectory(TrashBoardsDir);
        File.Move(path, TrashTarget(TrashBoardsDir, rel));
        State.Recentes.Remove(Rel(path));
        State.Favoritos.Remove(Rel(path));
        SaveState();
        if (notify) NotifyLibrary();
    }

    /// <summary>
    /// Notas pertencem aos quadros: as que nenhum quadro usa mais (de um quadro apagado, tiradas do
    /// quadro, antigas) vão para a lixeira.
    /// </summary>
    public static void TrashOrphanNotes()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in BoardFiles())
        {
            // Quadro que não deu para ler: sem saber as notas dele, nenhuma é tratada como órfã.
            if (NotesOf(b) is not { } notes) return;
            used.UnionWith(notes);
        }
        foreach (var note in PageFiles().ToList())
            if (!used.Contains(Path.GetFullPath(note)))
                try { TrashPage(note); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Quadros na lixeira, lidos do nome "data__pasta¦quadro.quadtab".</summary>
    public static IEnumerable<TrashEntry> TrashEntries()
    {
        if (!Directory.Exists(TrashBoardsDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(TrashBoardsDir, "*" + BoardExt))
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
        // Cascata: as notas do quadro voltam com ele (o conteúdo exato, da lixeira).
        foreach (var (note, trashed) in TrashedNotesOf(e.TrashPath))
            if (!File.Exists(note))
                try { Directory.CreateDirectory(Path.GetDirectoryName(note)!); File.Move(trashed, note); } catch (IOException) { }
        var target = Path.Combine(BoardsDir, e.OriginalRel);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        target = Unique(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target), BoardExt);
        File.Move(e.TrashPath, target);
        NotifyLibrary();
        return target;
    }

    public static void DeleteForever(TrashEntry e)
    {
        DeleteWithNotes(e.TrashPath);
        NotifyLibrary();
    }

    /// <summary>Cascata: o quadro é o dono das notas; apagado de vez, as notas dele somem junto.</summary>
    static void DeleteWithNotes(string trashedBoard)
    {
        foreach (var (_, trashed) in TrashedNotesOf(trashedBoard))
            try { File.Delete(trashed); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(trashedBoard); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Notas de um quadro que está na lixeira: (onde a nota morava, a cópia mais nova na lixeira).</summary>
    static IEnumerable<(string Note, string Trashed)> TrashedNotesOf(string trashedBoard)
    {
        var dir = TrashNotesDir;
        if (!Directory.Exists(dir)) yield break;
        var board = ReadBoard(trashedBoard);
        if (board is null) yield break;
        foreach (var it in board.Itens)
        {
            if (it is not { Tipo: "pagina", Arquivo: { } rel }) continue;
            var note = Full(rel);
            var newest = Directory.EnumerateFiles(dir, "*__" + Path.GetFileName(note)).OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
            if (newest is not null) yield return (note, newest);
        }
    }

    // Apaga de vez o que está na lixeira há mais de 30 dias.
    static void PurgeTrash()
    {
        foreach (var e in TrashEntries().ToList())
            if ((DateTime.Now - e.DeletedAt).TotalDays > 30)
                DeleteWithNotes(e.TrashPath);
        // Arquivos soltos de pastas apagadas: mesmo prazo de 30 dias.
        var bin = TrashFilesDir;
        if (!Directory.Exists(bin)) return;
        foreach (var f in Directory.EnumerateFiles(bin).ToList())
        {
            var name = Path.GetFileName(f);
            if (name.Length > 14 && DateTime.TryParseExact(name[..14], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var when)
                && (DateTime.Now - when).TotalDays > 30)
                try { File.Delete(f); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // ---------- páginas (.md) ----------

    public static IEnumerable<string> PageFiles() =>
        Directory.Exists(PagesDir) ? Directory.EnumerateFiles(PagesDir, "*.md", SearchOption.AllDirectories) : [];

    /// <summary>Acha uma página pelo nome (sem extensão, sem diferenciar maiúsculas).</summary>
    public static string? FindPage(string name) =>
        PageFiles().FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string ReadPage(string path) => File.Exists(path) ? File.ReadAllText(path) : "";

    public static void WritePage(string path, string text) => WriteText(path, text);

    /// <summary>Título da nota: o "# Título" do arquivo ou, sem ele, o nome do arquivo.</summary>
    public static string PageTitle(string path) => Rendering.Markdown.TitleOf(ReadPage(path)) ?? Path.GetFileNameWithoutExtension(path);

    /// <summary>Cria, renomeia e apaga notas (.md em Notas\).</summary>
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
        var target = RenameTarget(path, newName, ".md");
        if (target == path) return path;
        File.Move(path, target);
        NotifyLibrary();
        return target;
    }

    /// <summary>Apaga a nota de vez, sem passar pela lixeira.</summary>
    public static void DeletePage(string path)
    {
        File.Delete(path);
        State.Recentes.Remove(Rel(path));
        State.Fixados.Remove(Rel(path));
        State.Favoritos.Remove(Rel(path));
        SaveState();
        NotifyLibrary();
    }

    /// <summary>Algum outro quadro usa este arquivo de nota (ex.: quadro duplicado)?</summary>
    public static bool NoteUsedElsewhere(string path, string board)
    {
        var full = Path.GetFullPath(path);
        foreach (var b in BoardFiles())
        {
            if (string.Equals(b, board, StringComparison.OrdinalIgnoreCase)) continue;
            // Quadro ilegível pode usar a nota: na dúvida, ela fica.
            if (NotesOf(b) is not { } notes || notes.Contains(full)) return true;
        }
        return false;
    }

    /// <summary>
    /// Notas usadas por um quadro (caminhos completos). Null quando o quadro não pôde ser lido ou está
    /// estragado: aí não dá para saber quais notas ele usa, e nenhuma pode ser tratada como sem dono.
    /// </summary>
    static HashSet<string>? NotesOf(string boardPath)
    {
        if (ReadBoard(boardPath) is not { } b) return null;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in b.Itens)
            if (it is { Tipo: "pagina", Arquivo: { } rel })
                try { set.Add(Path.GetFullPath(Full(rel))); } catch (ArgumentException) { } catch (NotSupportedException) { }
        return set;
    }

    /// <summary>
    /// Outros quadros que usam uma nota (o aberto fica de fora: a tela cuida dele). Só quadros legíveis
    /// são regravados; "change" devolve true quando mudou alguma coisa.
    /// </summary>
    public static void UpdateBoardsUsing(string notePath, string? except, Func<Board, List<BoardItem>, bool> change)
    {
        var full = Path.GetFullPath(notePath);
        foreach (var path in BoardFiles().ToList())
        {
            if (except is not null && string.Equals(path, except, StringComparison.OrdinalIgnoreCase)) continue;
            if (ReadBoard(path) is not { } b) continue;
            var hits = b.Itens.Where(it => it is { Tipo: "pagina", Arquivo: { } rel } && SamePath(Full(rel), full)).ToList();
            if (hits.Count > 0 && change(b, hits)) SaveBoard(path, b);
        }
    }

    static bool SamePath(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), b, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    public static void TrashPage(string path)
    {
        var dir = TrashNotesDir;
        Directory.CreateDirectory(dir);
        File.Move(path, TrashTarget(dir, Path.GetFileName(path)));
        NotifyLibrary();
    }

    // ---------- anexos ----------

    /// <summary>
    /// Grava bytes (SaveAttachment) ou copia um arquivo (ImportAttachment) para a subpasta do tipo em Anexos;
    /// devolve o caminho relativo. Prévias (1ª página de PDF, imagem de link) vão para Anexos\Prévias.
    /// </summary>
    public static string SaveAttachment(byte[] data, string name, bool preview = false)
    {
        var path = NewAttachment(name, preview ? "Prévias" : null);
        File.WriteAllBytes(path, data);
        return Rel(path);
    }

    public static string ImportAttachment(string file)
    {
        var path = NewAttachment(file);
        File.Copy(file, path);
        return Rel(path);
    }

    // Caminho livre em Anexos\<tipo> com o nome (seguro) do arquivo.
    static string NewAttachment(string name, string? subdir = null)
    {
        var dir = Path.Combine(AttachDir, subdir ?? AttachSubdir(name));
        Directory.CreateDirectory(dir);
        return Unique(dir, SafeName(Path.GetFileNameWithoutExtension(name)), Path.GetExtension(name));
    }

    static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".svg", ".heic", ".avif" };
    static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg" };

    /// <summary>Subpasta de Anexos pelo tipo do arquivo: Imagens, Vídeos ou Arquivos.</summary>
    static string AttachSubdir(string name)
    {
        var ext = Path.GetExtension(name);
        return ImageExts.Contains(ext) ? "Imagens" : VideoExts.Contains(ext) ? "Vídeos" : "Arquivos";
    }

    /// <summary>Miniatura do quadro (galeria), em Sistema\Miniaturas.</summary>
    public static string ThumbPath(string boardPath) => Path.Combine(ThumbDir, Rel(boardPath).Replace('\\', '¦') + ".png");

    // ---------- leitura e escrita segura ----------

    // Lê um JSON; arquivo ausente ou estragado devolve null.
    static T? ReadJson<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), info) : null; }
        catch { return null; }
    }

    /// <summary>
    /// Grava num temporário e troca no fim (nunca deixa o arquivo pela metade). Devolve false se não deu
    /// (disco cheio, arquivo somente leitura ou aberto em outro programa) e avisa pelo WriteFailed.
    /// </summary>
    public static bool WriteText(string path, string text)
    {
        Exception? error = null;
        WriteLock.Wait();
        var tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex;
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { } // não deixa o .tmp para trás
        }
        finally { WriteLock.Release(); }
        if (error is null) return true;
        WriteFailed?.Invoke(path, error);
        return false;
    }
}
