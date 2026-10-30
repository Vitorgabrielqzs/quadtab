namespace Quadtab.Models;

/// <summary>Um item salvo no arquivo do quadro (.quadtab).</summary>
public sealed class BoardItem
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "postit";
    public string Cor { get; set; } = "amarelo";
    public double X { get; set; }
    public double Y { get; set; }
    public int Z { get; set; }
    public double Rotacao { get; set; }

    /// <summary>Tamanho escolhido pelo usuário (alças). Null = tamanho padrão do tipo.</summary>
    public double? Largura { get; set; }
    public double? Altura { get; set; }

    public string Fixacao { get; set; } = "alfinete";
    public bool Pautado { get; set; }

    /// <summary>Página sem a espiral de caderno (o usuário tirou pelo menu).</summary>
    public bool SemEspiral { get; set; }

    /// <summary>Item bloqueado não se move (arrastar em cima dele move a câmera).</summary>
    public bool Bloqueado { get; set; }

    /// <summary>Itens com o mesmo número de grupo se movem e se selecionam juntos.</summary>
    public int? Grupo { get; set; }

    /// <summary>Texto alternativo, aparece ao passar o mouse.</summary>
    public string? Descricao { get; set; }

    public string Titulo { get; set; } = "";
    public string Texto { get; set; } = "";

    /// <summary>Data em que o item foi colado (aparece no rodapé do post-it).</summary>
    public string? Criado { get; set; }

    /// <summary>Aparência livre (caixa de texto, forma, linha, tabela...).</summary>
    public ItemStyle? Estilo { get; set; }

    /// <summary>Forma da biblioteca (ex.: "estrela").</summary>
    public string? Forma { get; set; }

    /// <summary>Linha ou conector.</summary>
    public LineData? Linha { get; set; }

    /// <summary>Desenho à mão: traços com pontos relativos a (X, Y).</summary>
    public List<InkStroke>? Tracos { get; set; }

    /// <summary>Arquivo ligado ao item: página (.md em Páginas) ou anexo (em Anexos). Caminho relativo ao cofre.</summary>
    public string? Arquivo { get; set; }

    /// <summary>Cartão de link.</summary>
    public string? Url { get; set; }
    public string? Site { get; set; }
    public string? Imagem { get; set; }

    /// <summary>Imagem: recorte em frações (0..1) e raio dos cantos.</summary>
    public double[]? Recorte { get; set; }
    public double Raio { get; set; }

    /// <summary>Tabela: células por linha.</summary>
    public List<List<string>>? Tabela { get; set; }

    public static string KindToJson(ItemKind k) => k switch
    {
        ItemKind.Page => "pagina",
        ItemKind.Block => "bloco",
        ItemKind.Text => "texto",
        ItemKind.TextBox => "caixa",
        ItemKind.Shape => "forma",
        ItemKind.Line => "linha",
        ItemKind.Drawing => "desenho",
        ItemKind.Image => "imagem",
        ItemKind.Video => "video",
        ItemKind.File => "arquivo",
        ItemKind.Link => "link",
        ItemKind.Table => "tabela",
        _ => "postit",
    };

    public static ItemKind KindFromJson(string? s) => s switch
    {
        "pagina" => ItemKind.Page,
        "bloco" => ItemKind.Block,
        "texto" => ItemKind.Text,
        "caixa" => ItemKind.TextBox,
        "forma" => ItemKind.Shape,
        "linha" => ItemKind.Line,
        "desenho" => ItemKind.Drawing,
        "imagem" => ItemKind.Image,
        "video" => ItemKind.Video,
        "arquivo" => ItemKind.File,
        "link" => ItemKind.Link,
        "tabela" => ItemKind.Table,
        _ => ItemKind.PostIt,
    };

    public static string FasteningToJson(Fastening f) => f switch
    {
        Fastening.Tape => "fita",
        Fastening.None => "nenhuma",
        _ => "alfinete",
    };

    public static Fastening FasteningFromJson(string? s) => s switch
    {
        "fita" => Fastening.Tape,
        "nenhuma" => Fastening.None,
        _ => Fastening.Pin,
    };
}

/// <summary>Estilo livre de um item (inspetor).</summary>
public sealed class ItemStyle
{
    public string? Preenchimento { get; set; }   // #AARRGGBB
    public string? Borda { get; set; }
    public double Espessura { get; set; } = 2;
    public double Opacidade { get; set; } = 1;
    public string Fonte { get; set; } = "Inter";
    public double Tamanho { get; set; } = 18;
    public string? CorTexto { get; set; }
    public string Alinhamento { get; set; } = "esquerda"; // esquerda | centro | direita
    public bool Negrito { get; set; }
    public bool Italico { get; set; }
    public bool Lista { get; set; }

    public ItemStyle Clone() => (ItemStyle)MemberwiseClone();
}

/// <summary>Ponta de uma linha: solta (X, Y no mundo) ou presa a um item (Id + lado).</summary>
public sealed class LineEnd
{
    public double X { get; set; }
    public double Y { get; set; }
    public int? Item { get; set; }
    public string? Lado { get; set; } // cima | baixo | esquerda | direita
    public string Ponta { get; set; } = "nenhuma"; // nenhuma | seta | bolinha
}

public sealed class LineData
{
    public LineEnd De { get; set; } = new();
    public LineEnd Para { get; set; } = new(); // sem seta: só quando o usuário escolhe
    public string Tipo { get; set; } = "reta"; // reta | curva | angulo
    public string Traco { get; set; } = "solido"; // solido | barbante

    /// <summary>Dobra escolhida (curva e ângulo): deslocamento a partir do meio das pontas. Null = automática.</summary>
    public double? DobraX { get; set; }
    public double? DobraY { get; set; }
}

public sealed class InkStroke
{
    public string Ferramenta { get; set; } = "caneta"; // caneta | lapis | marcador
    public string Cor { get; set; } = "#FF1C1812";
    public double Espessura { get; set; } = 3;
    public List<double> Pontos { get; set; } = [];
}
