using System.Windows;
using System.Windows.Media;

namespace Quadtab.Rendering;

public sealed record ShapeInfo(string Key, string Name, string Category, Geometry Geometry);

/// <summary>Biblioteca de formas (desenhadas numa grade 100×100 e esticadas para o tamanho do item).</summary>
public static class ShapeLibrary
{
    public static readonly string[] Categories = ["Básicas", "Setas", "Símbolos", "Objetos", "Natureza"];

    static readonly (string Key, string Name, string Cat, string Path)[] Defs =
    [
        ("retangulo", "Retângulo", "Básicas", "M0,0 H100 V100 H0 Z"),
        ("retangulo-arredondado", "Retângulo arredondado", "Básicas", "M16,0 H84 A16,16 0 0 1 100,16 V84 A16,16 0 0 1 84,100 H16 A16,16 0 0 1 0,84 V16 A16,16 0 0 1 16,0 Z"),
        ("circulo", "Círculo", "Básicas", "M50,0 A50,50 0 1 1 49.99,0 Z"),
        ("triangulo", "Triângulo", "Básicas", "M50,0 L100,100 H0 Z"),
        ("triangulo-retangulo", "Triângulo retângulo", "Básicas", "M0,0 L100,100 H0 Z"),
        ("losango", "Losango", "Básicas", "M50,0 L100,50 L50,100 L0,50 Z"),
        ("pentagono", "Pentágono", "Básicas", "M50,0 L100,38 L81,100 H19 L0,38 Z"),
        ("hexagono", "Hexágono", "Básicas", "M25,0 H75 L100,50 L75,100 H25 L0,50 Z"),
        ("octogono", "Octógono", "Básicas", "M29,0 H71 L100,29 V71 L71,100 H29 L0,71 V29 Z"),
        ("paralelogramo", "Paralelogramo", "Básicas", "M25,0 H100 L75,100 H0 Z"),
        ("trapezio", "Trapézio", "Básicas", "M20,0 H80 L100,100 H0 Z"),
        ("balao", "Balão de fala", "Básicas", "M12,0 H88 A12,12 0 0 1 100,12 V62 A12,12 0 0 1 88,74 H40 L18,100 L22,74 H12 A12,12 0 0 1 0,62 V12 A12,12 0 0 1 12,0 Z"),
        ("seta-direita", "Seta para a direita", "Setas", "M0,30 H60 V5 L100,50 L60,95 V70 H0 Z"),
        ("seta-esquerda", "Seta para a esquerda", "Setas", "M100,30 H40 V5 L0,50 L40,95 V70 H100 Z"),
        ("seta-cima", "Seta para cima", "Setas", "M30,100 V40 H5 L50,0 L95,40 H70 V100 Z"),
        ("seta-baixo", "Seta para baixo", "Setas", "M30,0 V60 H5 L50,100 L95,60 H70 V0 Z"),
        ("seta-dupla", "Seta dupla", "Setas", "M0,50 L25,10 V32 H75 V10 L100,50 L75,90 V68 H25 V90 Z"),
        ("chevron", "Divisa", "Setas", "M0,0 H60 L100,50 L60,100 H0 L40,50 Z"),
        ("seta-curva", "Seta curva", "Setas", "M10,100 V55 A45,45 0 0 1 55,10 H70 V0 L100,22 L70,44 V34 H55 A21,21 0 0 0 34,55 V100 Z"),
        ("estrela", "Estrela", "Símbolos", "M50,0 L62,36 L100,38 L70,61 L81,100 L50,77 L19,100 L30,61 L0,38 L38,36 Z"),
        ("coracao", "Coração", "Símbolos", "M50,95 C10,65 0,45 0,28 A24,24 0 0 1 50,18 A24,24 0 0 1 100,28 C100,45 90,65 50,95 Z"),
        ("mais", "Mais", "Símbolos", "M35,0 H65 V35 H100 V65 H65 V100 H35 V65 H0 V35 H35 Z"),
        ("x", "X", "Símbolos", "M15,0 L50,35 L85,0 L100,15 L65,50 L100,85 L85,100 L50,65 L15,100 L0,85 L35,50 L0,15 Z"),
        ("check", "Visto", "Símbolos", "M0,55 L15,40 L38,62 L85,10 L100,25 L38,92 Z"),
        ("raio", "Raio", "Símbolos", "M60,0 L15,58 H45 L35,100 L85,38 H55 Z"),
        ("interrogacao", "Interrogação", "Símbolos", "M30,30 A20,20 0 1 1 62,46 C55,52 55,56 55,66 H45 C45,52 48,46 56,40 A10,10 0 1 0 40,30 Z M44,76 H56 V90 H44 Z"),
        ("alerta", "Alerta", "Símbolos", "M50,0 L100,95 H0 Z M45,30 V65 H55 V30 Z M45,74 V86 H55 V74 Z"),
        ("casa", "Casa", "Objetos", "M50,0 L100,45 H86 V100 H60 V68 H40 V100 H14 V45 H0 Z"),
        ("livro", "Livro", "Objetos", "M0,10 C20,0 40,0 50,12 C60,0 80,0 100,10 V95 C80,85 60,85 50,97 C40,85 20,85 0,95 Z"),
        ("lampada", "Lâmpada", "Objetos", "M50,0 A34,34 0 0 1 72,60 C66,66 64,72 64,78 H36 C36,72 34,66 28,60 A34,34 0 0 1 50,0 Z M36,84 H64 V92 H36 Z M42,94 H58 V100 H42 Z"),
        ("documento", "Documento", "Objetos", "M10,0 H68 L90,22 V100 H10 Z"),
        ("pasta", "Pasta", "Objetos", "M0,12 H38 L48,24 H100 V92 H0 Z"),
        ("nuvem-chat", "Mensagem", "Objetos", "M50,8 C80,8 100,26 100,46 C100,66 80,82 50,82 C44,82 38,81 33,80 L12,96 L16,74 C6,67 0,57 0,46 C0,26 20,8 50,8 Z"),
        ("bandeira", "Bandeira", "Objetos", "M10,0 H18 V100 H10 Z M18,4 H90 L76,26 L90,48 H18 Z"),
        ("sol", "Sol", "Natureza", "M50,25 A25,25 0 1 1 49.99,25 Z M47,0 H53 V16 H47 Z M47,84 H53 V100 H47 Z M0,47 H16 V53 H0 Z M84,47 H100 V53 H84 Z M14,18 L18,14 L29,25 L25,29 Z M71,75 L75,71 L86,82 L82,86 Z M82,14 L86,18 L75,29 L71,25 Z M25,71 L29,75 L18,86 L14,82 Z"),
        ("lua", "Lua", "Natureza", "M60,0 A50,50 0 1 0 100,70 A40,40 0 1 1 60,0 Z"),
        ("nuvem", "Nuvem", "Natureza", "M25,85 A22,22 0 0 1 20,42 A28,28 0 0 1 72,32 A24,24 0 0 1 80,85 Z"),
        ("gota", "Gota", "Natureza", "M50,0 C70,30 85,50 85,65 A35,35 0 0 1 15,65 C15,50 30,30 50,0 Z"),
        ("folha", "Folha", "Natureza", "M5,95 C5,40 40,5 95,5 C95,60 60,95 5,95 Z"),
        ("flor", "Flor", "Natureza", "M50,30 A20,20 0 1 1 70,50 A20,20 0 1 1 50,70 A20,20 0 1 1 30,50 A20,20 0 1 1 50,30 Z"),
        ("arvore", "Árvore", "Natureza", "M50,0 L85,45 H68 L92,75 H56 V100 H44 V75 H8 L32,45 H15 Z"),
        ("montanha", "Montanha", "Natureza", "M0,100 L35,30 L50,55 L68,20 L100,100 Z"),
    ];

    static readonly Dictionary<string, ShapeInfo> ByKey = Defs.ToDictionary(d => d.Key, d =>
    {
        var g = Geometry.Parse(d.Path);
        g.Freeze();
        return new ShapeInfo(d.Key, d.Name, d.Cat, g);
    });

    public static IReadOnlyCollection<ShapeInfo> All => ByKey.Values;

    public static ShapeInfo Get(string? key) =>
        key is not null && ByKey.TryGetValue(key, out var s) ? s : ByKey["retangulo-arredondado"];

    public static IEnumerable<ShapeInfo> Search(string query, string? category)
    {
        var q = ViewModels.BoardItemViewModel.Normalize(query.Trim());
        return ByKey.Values.Where(s => (category is null || s.Category == category) &&
                                       (q.Length == 0 || ViewModels.BoardItemViewModel.Normalize(s.Name).Contains(q)));
    }
}
