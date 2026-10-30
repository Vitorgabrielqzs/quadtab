using System.Windows.Media;

namespace Quadtab.Models;

public enum ItemKind { PostIt, Page, Block, Text, TextBox, Shape, Line, Drawing, Image, Video, File, Link, Table }

public enum Fastening { Pin, Tape, None }

/// <summary>Fundo do quadro. Liso = estilo Canvas do Obsidian (fundo do tema com pontinhos).</summary>
public enum BoardModel { Cortica, Branco, Lousa, Pontilhado, Liso }

/// <summary>Estilo escolhido no menu para os próximos post-its.</summary>
public enum NoteStyle { Pin, Tape, None, Lined }

/// <summary>Uma cor de post-it: papel (fundo) e tinta (detalhes).</summary>
public sealed class NoteColor
{
    public NoteColor(string key, string name, Color paper, Color ink)
    {
        Key = key;
        Name = name;
        Paper = paper;
        Ink = ink;
        PaperBrush = Freeze(new SolidColorBrush(paper));
        InkBrush = Freeze(new SolidColorBrush(ink));
        // Bloco de vidro: cor forte (75% da cor com um pouco de branco), quase opaca, para o texto preto ler bem.
        BlockFill = Freeze(new SolidColorBrush(Rendering.ColorMath.Mix(paper, 0.75, Color.FromArgb(200, 255, 255, 255))));
    }

    public string Key { get; }
    public string Name { get; }
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
    ];

    /// <summary>Ordem em que os modelos aparecem nas bandejas (Liso primeiro).</summary>
    public static readonly BoardModel[] ModelOrder = [BoardModel.Liso, BoardModel.Cortica, BoardModel.Branco, BoardModel.Lousa, BoardModel.Pontilhado];

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
