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
    public int Versao { get; set; } = 3;
    public string Titulo { get; set; } = "Meu quadro";
    public string Modelo { get; set; } = "cortica";

    /// <summary>Câmera da última vez. Null = nunca aberto (usa "Ajustar").</summary>
    public Camera? Camera { get; set; }

    public bool TituloOculto { get; set; }
    public bool Grade { get; set; }
    public bool Diagrama { get; set; } = true;
    public List<Scene> Cenas { get; set; } = [];
    public List<BoardItem> Itens { get; set; } = [];

    /// <summary>Formato antigo (versão 1) guardava só o zoom; é ignorado ao abrir.</summary>
    public double? Zoom { get; set; }

    /// <summary>Conteúdo da primeira vez que o app abre.</summary>
    public static Board CreateDefault()
    {
        var hoje = DateTime.Today.ToString("yyyy-MM-dd");
        BoardItem P(int id, string tipo, string cor, double x, double y, double rot, string fix,
                    string texto, string titulo = "", bool pautado = false) => new()
        {
            Id = id, Tipo = tipo, Cor = cor, X = x, Y = y, Z = id, Rotacao = rot, Fixacao = fix,
            Texto = texto, Titulo = titulo, Pautado = pautado, Criado = hoje,
        };

        return new Board
        {
            Itens =
            [
                P(1, "postit", "amarelo", 90, 70, -4, "alfinete", "Crie sua vida"),
                P(2, "pagina", "branco", 330, 50, 1.5, "fita",
                  "- [ ] Reserva de 6 meses\n- [ ] Viajar para o Chile\n- [ ] Ler 24 livros\n- [ ] Aprender a nadar\n- [ ] Correr 10 km", "Metas para 2027"),
                P(3, "postit", "rosa", 640, 104, 3, "fita", "Aventure-se"),
                P(4, "bloco", "azul", 890, 62, -1, "alfinete",
                  "Pequenos passos todos os dias. Revise este quadro aos domingos e mova o que já foi feito para Conquistas.", "Lembrete"),
                P(5, "postit", "verde", 1250, 90, -2, "alfinete", "Casa nova"),
                P(6, "postit", "azul", 110, 330, 2, "alfinete", "Liberdade financeira"),
                P(7, "texto", "branco", 640, 392, 0, "nenhuma", "GRATIDÃO"),
                P(8, "postit", "roxo", 380, 450, -3, "fita", "Formatura"),
                P(9, "postit", "laranja", 985, 330, 4, "alfinete", "Comprar fita\nRevisar metas\nLigar para a vó", "Esta semana", pautado: true),
                P(10, "pagina", "menta", 1200, 320, -1.5, "fita",
                  "1. O Pequeno Príncipe\n2. Sapiens\n3. Hábitos Atômicos\n4. Dom Casmurro", "Lista de leitura"),
                P(11, "postit", "menta", 120, 620, -2, "alfinete", "Mais tempo ao ar livre"),
                P(12, "bloco", "rosa", 600, 540, 1, "alfinete",
                  "Separar uma área do quadro só para viagens, com fotos e o orçamento de cada uma.", "Ideia"),
                P(13, "postit", "branco", 950, 630, 3, "fita", "Respirar fundo"),
                P(14, "texto", "verde", 1160, 760, 0, "nenhuma", "Conquistas"),
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
}
