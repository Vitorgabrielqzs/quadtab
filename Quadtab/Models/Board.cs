namespace Quadtab.Models;

/// <summary>Onde a câmera estava: centro (x, y) em coordenadas do mundo e zoom.</summary>
public sealed class Camera
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

/// <summary>Vista salva da câmera (para navegar e apresentar).</summary>
public sealed class Scene
{
    public string Nome { get; set; } = "Cena";
    public double X { get; set; }
    public double Y { get; set; }
    public double Zoom { get; set; } = 1;
}

/// <summary>Um quadro inteiro, como fica salvo em Quadros\*.quadtab.</summary>
public sealed class Board
{
    /// <summary>4 = já tem o modelo "liso" (quadros da versão 3 abrem como liso, que era o visual deles).</summary>
    public int Versao { get; set; } = 4;
    public string Titulo { get; set; } = "Meu quadro";
    public string Modelo { get; set; } = "liso";

    /// <summary>Câmera da última vez. Null = nunca aberto (usa "Ajustar").</summary>
    public Camera? Camera { get; set; }

    public bool TituloOculto { get; set; }
    public bool Grade { get; set; }
    public bool Diagrama { get; set; } = true;
    public List<Scene> Cenas { get; set; } = [];
    public List<BoardItem> Itens { get; set; } = [];

    /// <summary>Formato antigo (versão 1) guardava só o zoom; é ignorado ao abrir.</summary>
    public double? Zoom { get; set; }

    /// <summary>Conteúdo da primeira vez que o app abre: uma página, três blocos e duas linhas ligando-os.</summary>
    public static Board CreateDefault()
    {
        var hoje = DateTime.Today.ToString("yyyy-MM-dd");
        BoardItem P(int id, string tipo, string cor, double x, double y, string titulo, string texto, double? largura = null) => new()
        {
            Id = id, Tipo = tipo, Cor = cor, X = x, Y = y, Z = id, Fixacao = "nenhuma", Titulo = titulo, Texto = texto,
            Largura = largura, Criado = hoje, SemEspiral = true,
        };
        BoardItem L(int id, int from, string fromSide, int to, string toSide, string cor, string traco) => new()
        {
            Id = id, Tipo = "linha", Z = id, Fixacao = "nenhuma", Criado = hoje,
            Estilo = new ItemStyle { Borda = cor, Espessura = 2.5 },
            Linha = new LineData
            {
                De = new LineEnd { Item = from, Lado = fromSide },
                Para = new LineEnd { Item = to, Lado = toSide, Ponta = "seta" },
                Traco = traco,
            },
        };

        return new Board
        {
            Itens =
            [
                P(1, "pagina", "branco", 40, 40, "Bem-vindo ao Quadtab",
                  "Dê 2 cliques no vazio para criar outro item igual ao último que você criou (no começo, uma página).\n\nO botão direito abre a bandeja de elementos.\n\n" +
                  "Segure o botão direito num item e arraste até outro para ligar os dois com uma linha."),
                P(2, "bloco", "azul", 420, 40, "Ideias", "Anote aqui o que vier à cabeça.", 260),
                P(3, "bloco", "verde", 420, 250, "Tarefas", "- Organizar o quadro\n- Revisar as metas", 260),
                P(4, "bloco", "roxo", 800, 140, "Projeto", "Ligue os blocos com linhas sólidas ou de barbante, com setas.", 260),
                L(5, 2, "direita", 4, "esquerda", "#FF8A8F98", "solido"),
                L(6, 3, "direita", 4, "esquerda", "#FFC2A06E", "barbante"),
            ],
        };
    }
}

/// <summary>Estado da interface, salvo em .quadtab\estado.json.</summary>
public sealed class AppState
{
    public string? Cofre { get; set; }
    public string? QuadroAberto { get; set; }
    public List<string> Recentes { get; set; } = [];
    /// <summary>Quadros e páginas marcados como importantes (caminhos relativos ao cofre).</summary>
    public List<string> Favoritos { get; set; } = [];

    /// <summary>Quadros e páginas fixados no topo da lateral.</summary>
    public List<string> Fixados { get; set; } = [];

    public List<string> Abertos { get; set; } = ["fixados", "importantes", "paginas"];
    public bool TelaCheia { get; set; }
    public bool LateralAberta { get; set; } = true;

    /// <summary>"claro" ou "escuro" depois que o usuário escolhe no botão; null = segue o Windows.</summary>
    public string? Tema { get; set; }

    /// <summary>Faíscas ao clicar (Ajustes).</summary>
    public bool Faiscas { get; set; } = true;

    /// <summary>Última cor e último estilo de post-it escolhidos.</summary>
    public string CorPostit { get; set; } = "amarelo";
    public string EstiloPostit { get; set; } = "Pin";

    /// <summary>Molde do último item criado: 2 cliques com o botão direito colocam outro igual.</summary>
    public BoardItem? UltimoItem { get; set; }
}
