using Quadtab.Models;

namespace Quadtab.Services;

/// <summary>Um design pronto da aba "Designs": nome, frase curta e o quadro que ele gera.</summary>
public sealed record Design(string Name, string Description, Func<Board> Build);

/// <summary>
/// Modelos prontos de mapas mentais e diagramas (inspirados nos tipos clássicos: mapa radial, espinha de
/// peixe de Ishikawa, linha do tempo, bolha dupla, árvore, fluxograma, kanban, matriz de Eisenhower,
/// Ikigai e mural de sonhos). Cada um vira um quadro novo, com textos de exemplo para trocar.
/// </summary>
public static class DesignLibrary
{
    public static readonly Design[] All =
    [
        new("Mapa mental radial", "Uma ideia no centro e seis ramos coloridos, cada um com dois detalhes.", Radial),
        new("Espinha de peixe", "Causas e efeito (Ishikawa): seis espinhas levam ao problema na cabeça.", Fishbone),
        new("Linha do tempo neon", "Seis marcos sobre uma linha luminosa, no quadro escuro.", Timeline),
        new("Bolha dupla", "Compare duas coisas: o que é só de uma, só da outra e o que têm em comum.", DoubleBubble),
        new("Árvore na lousa", "Uma raiz, três galhos e as folhas, desenhados a giz.", Tree),
        new("Fluxograma de decisão", "Início, etapas, uma pergunta de sim ou não e o fim.", Flow),
        new("Kanban", "A fazer, fazendo e feito, com post-its para mover entre as colunas.", Kanban),
        new("Matriz de Eisenhower", "Urgente × importante: fazer agora, agendar, delegar ou eliminar.", Eisenhower),
        new("Ikigai", "Quatro círculos que se cruzam: o que você ama, faz bem, o mundo precisa e pagam.", Ikigai),
        new("Mural de sonhos", "Metas da vida presas na cortiça e ligadas por barbante.", VisionBoard),
    ];

    // ---------- montagem ----------

    /// <summary>Ajuda a montar um quadro: cada método põe um item e devolve o id.</summary>
    sealed class Builder(string model)
    {
        readonly List<BoardItem> _items = [];
        readonly Dictionary<int, (double X, double Y, double W, double H)> _boxes = [];
        int _id;
        readonly string _today = DateTime.Today.ToString("yyyy-MM-dd");

        int Put(BoardItem it, double w, double h)
        {
            it.Id = ++_id;
            it.Z = it.Id;
            it.Criado = _today;
            if (it.Fixacao == "alfinete" && it.Tipo != "postit") it.Fixacao = "nenhuma";
            _items.Add(it);
            _boxes[it.Id] = (it.X, it.Y, w, h);
            return it.Id;
        }

        /// <summary>Forma da biblioteca com texto no meio. x, y = canto de cima à esquerda.</summary>
        public int Shape(double x, double y, double w, double h, string shape, string fill, string border, string text = "",
            double size = 18, string ink = "#FF1D2130", bool bold = true, double stroke = 2.5, string font = "Inter") =>
            Put(new BoardItem
            {
                Tipo = "forma", Forma = shape, X = x, Y = y, Largura = w, Altura = h, Texto = text, Fixacao = "nenhuma",
                Estilo = new ItemStyle { Preenchimento = fill, Borda = border, Espessura = stroke, Tamanho = size, CorTexto = ink, Alinhamento = "centro", Negrito = bold, Fonte = font },
            }, w, h);

        /// <summary>Forma centrada em (cx, cy).</summary>
        public int ShapeAt(double cx, double cy, double w, double h, string shape, string fill, string border, string text = "",
            double size = 18, string ink = "#FF1D2130", bool bold = true, double stroke = 2.5) =>
            Shape(cx - w / 2, cy - h / 2, w, h, shape, fill, border, text, size, ink, bold, stroke);

        /// <summary>Texto solto (título). A largura é estimada só para não encostar em nada.</summary>
        public int Text(double x, double y, string text, double size, string color = "branco", string font = "Inter") =>
            Put(new BoardItem
            {
                Tipo = "texto", Cor = color, X = x, Y = y, Texto = text, Fixacao = "nenhuma",
                Estilo = new ItemStyle { Tamanho = size, Fonte = font },
            }, text.Length * size * 0.55 + 20, size * 1.3);

        /// <summary>Texto solto centrado na horizontal em cx.</summary>
        public int TextCentered(double cx, double y, string text, double size, string color = "branco", string font = "Inter", double widthFactor = 0.55) =>
            Text(cx - (text.Length * size * widthFactor + 20) / 2, y, text, size, color, font);

        public int PostIt(double x, double y, string color, string text, string fix = "alfinete", bool lined = false, string title = "") =>
            Put(new BoardItem { Tipo = "postit", Cor = color, X = x, Y = y, Texto = text, Titulo = title, Fixacao = fix, Pautado = lined }, 172, 172);

        public int Block(double x, double y, double w, double h, string color, string title, string text) =>
            Put(new BoardItem { Tipo = "bloco", Cor = color, X = x, Y = y, Largura = w, Altura = h, Titulo = title, Texto = text, Fixacao = "nenhuma" }, w, h);

        public int Box(double x, double y, double w, double h, string color, string text, double size = 18) =>
            Put(new BoardItem
            {
                Tipo = "caixa", Cor = color, X = x, Y = y, Largura = w, Altura = h, Texto = text, Fixacao = "nenhuma",
                Estilo = new ItemStyle { Tamanho = size, CorTexto = "#FF1D2130" },
            }, w, h);

        public (double X, double Y) Anchor(int id, string side)
        {
            var (x, y, w, h) = _boxes[id];
            return side switch
            {
                "cima" => (x + w / 2, y),
                "baixo" => (x + w / 2, y + h),
                "esquerda" => (x, y + h / 2),
                _ => (x + w, y + h / 2),
            };
        }

        /// <summary>Lado de "a" que olha para "b" (pelo centro dos dois).</summary>
        public string Facing(int a, int b)
        {
            var (ax, ay, aw, ah) = _boxes[a];
            var (bx, by, bw, bh) = _boxes[b];
            double dx = (bx + bw / 2) - (ax + aw / 2), dy = (by + bh / 2) - (ay + ah / 2);
            return Math.Abs(dx) * 0.8 > Math.Abs(dy) ? (dx > 0 ? "direita" : "esquerda") : (dy > 0 ? "baixo" : "cima");
        }

        /// <summary>Linha entre dois itens (lados automáticos se não forem dados).</summary>
        public int Link(int from, int to, string color, string type = "curva", double width = 2.5, bool arrow = false,
            string? fromSide = null, string? toSide = null, string trace = "solido")
        {
            fromSide ??= Facing(from, to);
            toSide ??= Facing(to, from);
            var a = Anchor(from, fromSide);
            var b = Anchor(to, toSide);
            return Line(new LineEnd { X = a.X, Y = a.Y, Item = from, Lado = fromSide },
                        new LineEnd { X = b.X, Y = b.Y, Item = to, Lado = toSide, Ponta = arrow ? "seta" : "nenhuma" }, color, type, width, trace);
        }

        /// <summary>Linha com pontas soltas (ou uma presa) em coordenadas do mundo.</summary>
        public int Free(double x1, double y1, double x2, double y2, string color, double width = 3, bool arrow = false, int? toItem = null, string? toSide = null)
        {
            if (toItem is { } t && toSide is { } s) (x2, y2) = Anchor(t, s);
            return Line(new LineEnd { X = x1, Y = y1 }, new LineEnd { X = x2, Y = y2, Item = toItem, Lado = toSide, Ponta = arrow ? "seta" : "nenhuma" }, color, "reta", width, "solido");
        }

        int Line(LineEnd de, LineEnd para, string color, string type, double width, string trace)
        {
            double x = Math.Min(de.X, para.X), y = Math.Min(de.Y, para.Y);
            return Put(new BoardItem
            {
                Tipo = "linha", X = x, Y = y, Largura = Math.Max(4, Math.Abs(de.X - para.X)), Altura = Math.Max(4, Math.Abs(de.Y - para.Y)),
                Fixacao = "nenhuma", Estilo = new ItemStyle { Borda = color, Espessura = width },
                Linha = new LineData { De = de, Para = para, Tipo = type, Traco = trace },
            }, 4, 4);
        }

        public Board Done(bool hideTitle = false)
        {
            // Linhas atrás de tudo: os itens ficam por cima das pontas.
            int z = 1;
            foreach (var it in _items.OrderBy(i => i.Tipo == "linha" ? 0 : 1).ThenBy(i => i.Id)) it.Z = z++;
            return new Board { Modelo = model, Itens = _items, TituloOculto = hideTitle, Diagrama = true };
        }
    }

    static (string Paper, string Ink) C(string key)
    {
        var c = Palette.Get(key);
        return ($"#FF{c.Paper.R:X2}{c.Paper.G:X2}{c.Paper.B:X2}", $"#FF{c.Ink.R:X2}{c.Ink.G:X2}{c.Ink.B:X2}");
    }

    static string Alpha(string hex, byte a) => $"#{a:X2}{hex[3..]}";

    // ---------- 1. mapa mental radial ----------

    static Board Radial()
    {
        var b = new Builder("liso");
        b.TextCentered(0, -720, "Meu grande projeto", 80, "branco", "Caveat", 0.42);
        int center = b.ShapeAt(0, 0, 280, 150, "circulo", "#FF1D2130", "#FF1D2130", "IDEIA\nCENTRAL", 24, "#FFFFFFFF");
        (string Color, string Title, string A, string B)[] branches =
        [
            ("rosa", "Objetivo", "Aonde quero chegar", "Como saber que deu certo"),
            ("laranja", "Recursos", "Dinheiro e tempo", "Ferramentas"),
            ("amarelo", "Pessoas", "Quem ajuda", "Quem decide"),
            ("verde", "Prazos", "Primeira entrega", "Data final"),
            ("azul", "Riscos", "O que pode dar errado", "Plano B"),
            ("roxo", "Ideias", "Rascunhos", "Inspirações"),
        ];
        for (int i = 0; i < branches.Length; i++)
        {
            var (key, title, a, bb) = branches[i];
            var (paper, ink) = C(key);
            double ang = (-90 + i * 60) * Math.PI / 180;
            double cx = Math.Cos(ang) * 400, cy = Math.Sin(ang) * 290;
            int branch = b.ShapeAt(cx, cy, 200, 72, "retangulo-arredondado", paper, ink, title, 22, ink);
            b.Link(center, branch, ink, "curva", 5);
            for (int k = 0; k < 2; k++)
            {
                double sub = ang + (k == 0 ? -0.24 : 0.24);
                double sx = Math.Cos(sub) * 760, sy = Math.Sin(sub) * 500;
                int leaf = b.ShapeAt(sx, sy, 250, 64, "retangulo-arredondado", "#FFFFFFFF", ink, k == 0 ? a : bb, 16, "#FF1D2130", false, 2);
                b.Link(branch, leaf, ink, "curva", 2.5);
            }
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 2. espinha de peixe (Ishikawa) ----------

    static Board Fishbone()
    {
        var b = new Builder("branco");
        b.Text(-760, -470, "Por que isso acontece?", 56, "vermelho", "Bebas Neue");
        int head = b.Shape(560, -70, 260, 140, "pentagono", "#FFFF9E99", "#FFB3261E", "PROBLEMA\n(o efeito)", 20, "#FF5A0F0B");
        b.Free(-760, 0, 0, 0, "#FF2A2E39", 6, arrow: true, toItem: head, toSide: "esquerda");
        (string Name, string Key, double X, bool Top, string[] Causes)[] bones =
        [
            ("Métodos", "azul", -560, true, ["Passos confusos", "Sem padrão"]),
            ("Máquinas", "roxo", -160, true, ["Equipamento velho", "Falta manutenção"]),
            ("Pessoas", "laranja", 240, true, ["Pouco treino", "Equipe cansada"]),
            ("Materiais", "verde", -560, false, ["Fornecedor atrasa", "Qualidade baixa"]),
            ("Medidas", "turquesa", -160, false, ["Sem indicadores", "Dados errados"]),
            ("Ambiente", "rosa", 240, false, ["Barulho", "Pouco espaço"]),
        ];
        foreach (var (name, key, x, top, causes) in bones)
        {
            var (paper, ink) = C(key);
            double y = top ? -330 : 270;
            int cat = b.Shape(x - 95, y, 190, 60, "retangulo-arredondado", paper, ink, name, 20, ink);
            // A espinha desce (ou sobe) na diagonal até a coluna principal.
            b.Free(x + 170, 0, 0, 0, ink, 3, toItem: cat, toSide: top ? "baixo" : "cima");
            for (int k = 0; k < causes.Length; k++)
            {
                double t = (k + 1) / 3.0;
                double cy = top ? y + 60 + t * 270 : y - t * 270;
                double cx = x + t * 170 + 14;
                b.Text(cx, cy - 16, "• " + causes[k], 17, key);
            }
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 3. linha do tempo neon ----------

    static Board Timeline()
    {
        var b = new Builder("escuro");
        b.Text(-40, -560, "Linha do tempo", 80, "ceu", "Pacifico");
        string[] keys = ["forte-rosa", "forte-laranja", "forte-amarelo", "forte-verde", "forte-azul", "forte-roxo"];
        (string When, string Title, string Text)[] marks =
        [
            ("JAN", "Começo", "A ideia nasce e o plano sai do papel."),
            ("MAR", "Pesquisa", "Conversar com pessoas e juntar referências."),
            ("MAI", "Protótipo", "Primeira versão para testar."),
            ("JUL", "Ajustes", "O que aprendemos vira melhoria."),
            ("SET", "Lançamento", "Apresentar para o mundo."),
            ("DEZ", "Celebrar", "Olhar para trás e planejar o próximo ano."),
        ];
        b.Free(-60, 0, 1720, 0, "#FFE6E7EB", 4, arrow: true);
        for (int i = 0; i < marks.Length; i++)
        {
            var neon = Palette.Get(keys[i]).Paper;
            string hex = $"#FF{neon.R:X2}{neon.G:X2}{neon.B:X2}";
            double x = 80 + i * 300;
            bool up = i % 2 == 0;
            int dot = b.ShapeAt(x, 0, 96, 96, "circulo", hex, "#FFFFFFFF", marks[i].When, 17, "#FFFFFFFF", true, 3);
            int card = b.Shape(x - 135, up ? -340 : 140, 270, 200, "retangulo-arredondado", Alpha(hex, 0x33), hex,
                marks[i].Title.ToUpperInvariant() + "\n\n" + marks[i].Text, 19, "#FFFFFFFF", false, 2.5);
            b.Link(dot, card, hex, "reta", 2.5, fromSide: up ? "cima" : "baixo", toSide: up ? "baixo" : "cima");
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 4. bolha dupla ----------

    static Board DoubleBubble()
    {
        var b = new Builder("liso");
        b.TextCentered(0, -520, "Gato  ×  Cachorro", 60, "indigo", "Fredoka", 0.5);
        var (pa, ia) = C("rosa");
        var (pb, ib) = C("azul");
        var (pc, ic) = C("roxo");
        int a = b.ShapeAt(-330, 0, 220, 220, "circulo", pa, ia, "GATO", 26, ia);
        int bb = b.ShapeAt(330, 0, 220, 220, "circulo", pb, ib, "CACHORRO", 24, ib);
        string[] both = ["Animal de\nestimação", "Precisa de\ncarinho", "Vai ao\nveterinário"];
        for (int i = 0; i < 3; i++)
        {
            int s = b.ShapeAt(0, -260 + i * 260, 180, 180, "circulo", pc, ic, both[i], 18, ic, false);
            b.Link(a, s, ic, "curva", 2.5);
            b.Link(bb, s, ic, "curva", 2.5);
        }
        string[] onlyA = ["Independente", "Usa caixa\nde areia", "Sobe em\ntudo"];
        string[] onlyB = ["Passeia na\nrua", "Abana o\nrabo", "Aprende\ncomandos"];
        for (int i = 0; i < 3; i++)
        {
            int l = b.ShapeAt(-720, -260 + i * 260, 180, 180, "circulo", "#FFFFFFFF", ia, onlyA[i], 18, ia, false);
            b.Link(a, l, ia, "curva", 2.5);
            int r = b.ShapeAt(720, -260 + i * 260, 180, 180, "circulo", "#FFFFFFFF", ib, onlyB[i], 18, ib, false);
            b.Link(bb, r, ib, "curva", 2.5);
        }
        b.TextCentered(-720, 400, "só do gato", 24, "rosa");
        b.TextCentered(0, 400, "os dois", 24, "roxo");
        b.TextCentered(720, 400, "só do cachorro", 24, "azul");
        return b.Done(hideTitle: true);
    }

    // ---------- 5. árvore na lousa ----------

    static Board Tree()
    {
        var b = new Builder("lousa");
        b.TextCentered(0, -420, "Plano de estudos", 64, "amarelo", "Amatic SC", 0.32);
        string chalk = "#FFF2F2EA";
        int root = b.ShapeAt(0, -230, 280, 84, "retangulo-arredondado", "#22FFFFFF", chalk, "ENEM 2027", 28, chalk, true, 3);
        (string Name, string Key, string[] Leaves)[] branches =
        [
            ("Exatas", "amarelo", ["Matemática", "Física", "Química"]),
            ("Humanas", "rosa", ["História", "Geografia", "Filosofia"]),
            ("Linguagens", "menta", ["Redação", "Literatura", "Inglês"]),
        ];
        for (int i = 0; i < branches.Length; i++)
        {
            var (name, key, leaves) = branches[i];
            var paper = C(key).Paper;
            double x = (i - 1) * 560;
            int node = b.ShapeAt(x, 0, 230, 72, "retangulo-arredondado", Alpha(paper, 0x33), paper, name, 24, paper, true, 3);
            b.Link(root, node, chalk, "angulo", 3, fromSide: "baixo", toSide: "cima");
            for (int k = 0; k < leaves.Length; k++)
            {
                double lx = x + (k - 1) * 190;
                int leaf = b.ShapeAt(lx, 220, 178, 64, "retangulo-arredondado", "#00FFFFFF", paper, leaves[k], 19, chalk, false, 2);
                b.Link(node, leaf, paper, "angulo", 2.5, fromSide: "baixo", toSide: "cima");
            }
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 6. fluxograma de decisão ----------

    static Board Flow()
    {
        var b = new Builder("liso");
        b.Text(-520, -470, "Como decidir?", 60, "petroleo", "DM Serif Display");
        var (pg, ig) = C("verde");
        var (pb, ib) = C("azul");
        var (py, iy) = C("amarelo");
        var (pr, ir) = C("vermelho");
        string gray = "#FF4B5263";
        int start = b.ShapeAt(0, -330, 200, 70, "retangulo-arredondado", pg, ig, "Início", 20, ig);
        int step1 = b.ShapeAt(0, -170, 260, 80, "retangulo", pb, ib, "Juntar as informações", 17, ib);
        int ask = b.ShapeAt(0, 30, 240, 170, "losango", py, iy, "Vale a\npena?", 18, iy);
        int yes = b.ShapeAt(-380, 30, 240, 80, "retangulo", pb, ib, "Fazer um plano", 17, ib);
        int no = b.ShapeAt(380, 30, 240, 80, "retangulo", pb, ib, "Procurar outra saída", 17, ib);
        int act = b.ShapeAt(-380, 240, 240, 80, "retangulo", pb, ib, "Colocar em prática", 17, ib);
        int end = b.ShapeAt(0, 400, 200, 70, "retangulo-arredondado", pr, ir, "Fim", 20, ir);
        b.Link(start, step1, gray, "reta", 2.5, true, "baixo", "cima");
        b.Link(step1, ask, gray, "reta", 2.5, true, "baixo", "cima");
        b.Link(ask, yes, ig, "reta", 2.5, true, "esquerda", "direita");
        b.Link(ask, no, ir, "reta", 2.5, true, "direita", "esquerda");
        b.Link(yes, act, gray, "reta", 2.5, true, "baixo", "cima");
        b.Link(act, end, gray, "angulo", 2.5, true, "baixo", "esquerda");
        b.Link(no, step1, gray, "angulo", 2.5, true, "cima", "direita");
        b.Text(-215, -14, "Sim", 20, "verde");
        b.Text(150, -14, "Não", 20, "vermelho");
        return b.Done(hideTitle: true);
    }

    // ---------- 7. kanban ----------

    static Board Kanban()
    {
        var b = new Builder("branco");
        b.Text(-30, -170, "Quadro de tarefas", 64, "branco", "Permanent Marker");
        (string Title, string Key, (string Color, string Text)[] Notes)[] cols =
        [
            ("A FAZER", "rosa", [("rosa", "Escrever o resumo"), ("amarelo", "Ligar para o cliente"), ("rosa", "Montar os slides")]),
            ("FAZENDO", "azul", [("azul", "Revisar o texto"), ("menta", "Separar as fotos")]),
            ("FEITO", "verde", [("verde", "Pesquisa inicial"), ("verde", "Lista de ideias")]),
        ];
        for (int i = 0; i < cols.Length; i++)
        {
            var (title, key, notes) = cols[i];
            var (paper, ink) = C(key);
            double x = i * 440;
            b.Box(x, -40, 400, 860, key, "");
            b.Shape(x + 24, -20, 352, 64, "retangulo-arredondado", paper, ink, title + $"  ·  {notes.Length}", 20, ink);
            for (int k = 0; k < notes.Length; k++)
                b.PostIt(x + 114, 80 + k * 230, notes[k].Color, notes[k].Text, k % 2 == 0 ? "alfinete" : "fita");
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 8. matriz de Eisenhower ----------

    static Board Eisenhower()
    {
        var b = new Builder("liso");
        b.Text(-20, -300, "O que fazer primeiro?", 64, "indigo", "Playfair Display");
        (string Title, string Hint, string Key, string Note)[] q =
        [
            ("FAZER AGORA", "urgente e importante", "vermelho", "Entregar o trabalho de amanhã"),
            ("AGENDAR", "importante, sem pressa", "azul", "Estudar para a prova do mês"),
            ("DELEGAR", "urgente, pouco importante", "laranja", "Responder mensagens do grupo"),
            ("ELIMINAR", "nem urgente nem importante", "cinza", "Rolar o feed sem parar"),
        ];
        b.TextCentered(300, -160, "URGENTE", 28, "cinza");
        b.TextCentered(880, -160, "NÃO URGENTE", 28, "cinza");
        b.Text(-230, 160, "IMPORTANTE", 26, "cinza");
        b.Text(-290, 740, "NÃO IMPORTANTE", 26, "cinza");
        for (int i = 0; i < 4; i++)
        {
            var (title, hint, key, note) = q[i];
            var (paper, ink) = C(key);
            // Ordem: fazer (esq. cima), agendar (dir. cima), delegar (esq. baixo), eliminar (dir. baixo).
            double x = i % 2 == 0 ? 20 : 600, y = i < 2 ? -100 : 480;
            b.Shape(x, y, 560, 560, "retangulo-arredondado", Alpha(paper, 0x66), ink, "", 16, ink, false, 2.5);
            b.Text(x + 30, y + 26, title, 34, key, "Bebas Neue");
            b.Text(x + 32, y + 80, hint, 20, key);
            b.PostIt(x + 60 + (i % 2) * 140, y + 170 + (i / 2) * 40, key == "cinza" ? "branco" : key, note, "fita");
        }
        return b.Done(hideTitle: true);
    }

    // ---------- 9. ikigai ----------

    static Board Ikigai()
    {
        var b = new Builder("liso");
        b.TextCentered(0, -760, "Meu Ikigai", 72, "framboesa", "Dancing Script", 0.42);
        (string Key, double Cx, double Cy, string Label, double Lx, double Ly)[] circles =
        [
            ("rosa", 0, -220, "O QUE VOCÊ AMA", 0, -540),
            ("verde", -220, 0, "NO QUE VOCÊ\nÉ BOM", -560, -20),
            ("azul", 220, 0, "DO QUE O MUNDO\nPRECISA", 560, -20),
            ("amarelo", 0, 220, "PELO QUE PODE\nSER PAGO", 0, 530),
        ];
        foreach (var (key, cx, cy, _, _, _) in circles)
        {
            var (paper, ink) = C(key);
            b.ShapeAt(cx, cy, 520, 520, "circulo", Alpha(paper, 0x8C), Alpha(ink, 0x99), "", 16, ink, false, 2);
        }
        foreach (var (key, _, _, label, lx, ly) in circles)
        {
            var ink = C(key).Ink;
            b.ShapeAt(lx, ly, 300, 90, "retangulo-arredondado", "#00FFFFFF", "#00FFFFFF", label, 24, ink);
        }
        (string Text, double X, double Y)[] meets = [("Paixão", -150, -150), ("Missão", 150, -150), ("Profissão", -150, 150), ("Vocação", 150, 150)];
        foreach (var (t, x, y) in meets) b.ShapeAt(x, y, 170, 56, "retangulo-arredondado", "#00FFFFFF", "#00FFFFFF", t, 21, "#FF1D2130");
        b.ShapeAt(0, 0, 150, 150, "estrela", "#FFFFFFFF", "#FF8E2C87", "IKIGAI", 16, "#FF8E2C87", true, 3);
        return b.Done(hideTitle: true);
    }

    // ---------- 10. mural de sonhos ----------

    static Board VisionBoard()
    {
        var b = new Builder("cortica");
        int title = b.Shape(-230, -95, 460, 190, "nuvem", "#FFFBFAF5", "#FF6B3F22", "MEUS SONHOS\n2027", 30, "#FF6B3F22", true, 3);
        (string Key, string Text, double X, double Y, string Fix)[] dreams =
        [
            ("amarelo", "Viajar para o Chile", -620, -420, "alfinete"),
            ("rosa", "Aprender a nadar", -120, -470, "fita"),
            ("menta", "Correr 10 km", 400, -420, "alfinete"),
            ("azul", "Reserva de 6 meses", 560, -60, "alfinete"),
            ("roxo", "Ler 24 livros", 400, 300, "fita"),
            ("verde", "Casa nova", -120, 340, "alfinete"),
            ("laranja", "Mais tempo com a família", -620, 300, "fita"),
            ("lilas", "Começar um negócio", -780, -60, "alfinete"),
        ];
        foreach (var (key, text, x, y, fix) in dreams)
        {
            int note = b.PostIt(x, y, key, text, fix);
            b.Link(title, note, "#FFC2A06E", "reta", 3, trace: "barbante");
        }
        b.ShapeAt(0, 640, 420, 84, "retangulo-arredondado", "#FFFBFAF5", "#FF6B3F22", "UM PASSO POR DIA", 26, "#FF6B3F22", true, 3);
        return b.Done(hideTitle: true);
    }
}
