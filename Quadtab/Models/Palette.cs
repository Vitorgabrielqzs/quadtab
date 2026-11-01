using System.Windows.Media;

namespace Quadtab.Models;

public enum ItemKind { PostIt, Page, Block, Text, TextBox, Shape, Line, Drawing, Image, Video, File, Link, Table }

public enum Fastening { Pin, Tape, None }

/// <summary>Fundo do quadro. Liso = estilo Canvas do Obsidian (fundo do tema com pontinhos).</summary>
public enum BoardModel { Cortica, Branco, Lousa, Pontilhado, Liso, Escuro }

/// <summary>Estilo escolhido no menu para os próximos post-its.</summary>
public enum NoteStyle { Pin, Tape, None, Lined }

/// <summary>Uma cor de post-it: papel (fundo) e tinta (detalhes).</summary>
public sealed class NoteColor
{
    public NoteColor(string key, string name, Color paper, Color ink, bool strong = false)
    {
        Key = key;
        Name = name;
        Paper = paper;
        IsStrong = strong;
        IsDark = Luminance(paper) < 0.3;
        Ink = strong ? (IsDark ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A)) : ink;
        PaperBrush = Freeze(new SolidColorBrush(paper));
        InkBrush = Freeze(new SolidColorBrush(Ink));
        // Bloco de vidro: 75% da cor com um pouco de branco, para o texto preto ler bem. Cor forte fica sólida.
        BlockFill = Freeze(new SolidColorBrush(strong ? paper : Rendering.ColorMath.Mix(paper, 0.75, Color.FromArgb(200, 255, 255, 255))));
    }

    static double Luminance(Color c)
    {
        static double L(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }

    public string Key { get; }
    public string Name { get; }
    /// <summary>Cor forte de verdade: sólida, sem misturar com branco.</summary>
    public bool IsStrong { get; }
    /// <summary>Fundo escuro: o texto dentro do item fica branco.</summary>
    public bool IsDark { get; }
    public Color Paper { get; }
    public Color Ink { get; }
    public SolidColorBrush PaperBrush { get; }
    public SolidColorBrush InkBrush { get; }
    public SolidColorBrush BlockFill { get; }

    static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}

public static class Palette
{
    public static readonly NoteColor[] Colors =
    [
        new("amarelo", "Amarelo", C(0xFFE27A), C(0x8A6400)),
        new("laranja", "Laranja", C(0xFFBE8C), C(0xA4470E)),
        new("rosa", "Rosa", C(0xFFB1C6), C(0xA61F4C)),
        new("roxo", "Roxo", C(0xD2BBFF), C(0x5A2DB5)),
        new("azul", "Azul", C(0xA6D3FF), C(0x1B5DA8)),
        new("menta", "Menta", C(0xA2EDD8), C(0x0B7658)),
        new("verde", "Verde", C(0xC6EC9E), C(0x3B770F)),
        new("branco", "Branco", C(0xFBFAF5), C(0x2A2E39)),
        // Mais cores para personalizar (papel claro + tinta forte, como as primeiras)
        new("vermelho", "Vermelho", C(0xFF9E99), C(0xB3261E)),
        new("pessego", "Pêssego", C(0xFFD3B6), C(0xA35A2A)),
        new("areia", "Areia", C(0xF3DFB8), C(0x7A5A1E)),
        new("lima", "Lima", C(0xE2F27A), C(0x6B7A00)),
        new("turquesa", "Turquesa", C(0x8FE3EF), C(0x0A6E7C)),
        new("indigo", "Índigo", C(0xB8C3FF), C(0x2F3FA8)),
        new("lilas", "Lilás", C(0xF2C4F0), C(0x8E2C87)),
        new("cinza", "Cinza", C(0xDADDE3), C(0x4B5263)),
        new("coral", "Coral", C(0xFFB59E), C(0xB23C17)),
        new("framboesa", "Framboesa", C(0xF7A8C8), C(0x9C1452)),
        new("lavanda", "Lavanda", C(0xDCD3FF), C(0x4F3E99)),
        new("ceu", "Céu", C(0xCFEAFF), C(0x1D6FB8)),
        new("petroleo", "Petróleo", C(0xA9D8D2), C(0x1F5E58)),
        new("salvia", "Sálvia", C(0xCFE3CF), C(0x3E6B45)),
        new("oliva", "Oliva", C(0xD5DDA8), C(0x55611D)),
        new("chocolate", "Chocolate", C(0xE3C7B0), C(0x6B3F22)),
        // Cores fortes (a tinta sai do contraste: branca nas escuras)
        new("forte-vermelho", "Vermelho forte", C(0xFF0000), default, true),
        new("forte-laranja", "Laranja forte", C(0xFF6D00), default, true),
        new("forte-amarelo", "Amarelo forte", C(0xFFD600), default, true),
        new("forte-verde", "Verde forte", C(0x00A83E), default, true),
        new("forte-azul", "Azul forte", C(0x0050FF), default, true),
        new("forte-roxo", "Roxo forte", C(0x7B00E0), default, true),
        new("forte-rosa", "Rosa forte", C(0xFF0080), default, true),
        new("forte-marrom", "Marrom forte", C(0x5D3200), default, true),
        new("preto", "Preto", C(0x000000), default, true),
        new("branco-puro", "Branco puro", C(0xFFFFFF), default, true),
    ];

    /// <summary>Cores dos alfinetes e fitas, escolhidas por id % 5.</summary>
    public static readonly Color[] PinColors = [C(0xFF453A), C(0x0A84FF), C(0x30D158), C(0xFF9F0A), C(0xBF5AF2)];

    public static NoteColor Get(string? key)
    {
        foreach (var c in Colors)
            if (c.Key == key) return c;
        return Colors[0];
    }

    public static Color PinFor(int id) => PinColors[((id % 5) + 5) % 5];

    public static readonly (BoardModel Model, string Key, string Name)[] Models =
    [
        (BoardModel.Cortica, "cortica", "Cortiça"),
        (BoardModel.Branco, "branco", "Branco"),
        (BoardModel.Lousa, "lousa", "Lousa"),
        (BoardModel.Pontilhado, "pontilhado", "Pontilhado"),
        (BoardModel.Liso, "liso", "Liso"),
        (BoardModel.Escuro, "escuro", "Escuro"),
    ];

    /// <summary>Ordem em que os modelos aparecem nas bandejas (Liso primeiro).</summary>
    public static readonly BoardModel[] ModelOrder = [BoardModel.Liso, BoardModel.Cortica, BoardModel.Branco, BoardModel.Escuro, BoardModel.Lousa, BoardModel.Pontilhado];

    public static string ModelName(BoardModel m) => Models[(int)m].Name;
    public static string ModelKey(BoardModel m) => Models[(int)m].Key;

    public static BoardModel ParseModel(string? key)
    {
        foreach (var m in Models)
            if (m.Key == key) return m.Model;
        return BoardModel.Liso;
    }

    public static Color C(uint rgb, byte a = 255) =>
        Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
