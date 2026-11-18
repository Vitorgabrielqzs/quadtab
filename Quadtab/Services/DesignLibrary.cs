using Quadtab.Models;

namespace Quadtab.Services;

/// <summary>Um design pronto da aba "Designs": nome, frase curta e o quadro que ele gera.</summary>
public sealed record Design(string Name, string Description, Func<Board> Build);

/// <summary>
/// Aqui usamos a classe DesignLibrary para guardar os modelos prontos de mapas mentais e diagramas (inspirados nos tipos clássicos: mapa radial, espinha de
/// peixe de Ishikawa, linha do tempo, bolha dupla, árvore, fluxograma, kanban, matriz de Eisenhower,
/// Ikigai e mural de sonhos). Cada um vira um quadro novo, com textos de exemplo para trocar.
/// </summary>
public static class DesignLibrary
{
    public static readonly Design[] All =
    [
        new("Mapa pastel", "Ideia central num borrão de tinta e oito tópicos em etiquetas pastel com setas.", Pastel),
        new("Caderno de estudos", "Resumo de matéria: nuvem no centro, ramos coloridos, listas e post-its de lembrete.", Notebook),
        new("Teia orgânica", "Ramos grossos que afinam até as folhas, com o texto direto nas linhas.", Organic),
        new("Teia de conceitos", "Conceitos em caixas ligadas por linhas finas, espalhados como uma teia.", ConceptWeb),
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

    /// <summary>Aqui usamos a classe Builder para montar um quadro: cada método põe um item e devolve o id.</summary>
    sealed class Builder(string model)
    {
        readonly List<BoardItem> _items = [];
        readonly Dictionary<int, (double X, double Y, double W, double H)> _boxes = [];
        int _id;
        readonly string _today = DateTime.Today.ToString("yyyy-MM-dd");

        // Dá id, camada e data ao item e guarda a caixa dele (para ligar linhas depois).
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
            double size = 18, string ink = "#FF1D2130", bool bold = true, double stroke = 2.5, string font = "Inter", string align = "centro") =>
            Put(new BoardItem
            {
                Tipo = "forma", Forma = shape, X = x, Y = y, Largura = w, Altura = h, Texto = text, Fixacao = "nenhuma",
                Estilo = new ItemStyle { Preenchimento = fill, Borda = border, Espessura = stroke, Tamanho = size, CorTexto = ink, Alinhamento = align, Negrito = bold, Fonte = font },
            }, w, h);

        /// <summary>Forma centrada em (cx, cy).</summary>
        public int ShapeAt(double cx, double cy, double w, double h, string shape, string fill, string border, string text = "",
            double size = 18, string ink = "#FF1D2130", bool bold = true, double stroke = 2.5, string font = "Inter") =>
            Shape(cx - w / 2, cy - h / 2, w, h, shape, fill, border, text, size, ink, bold, stroke, font);

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

        /// <summary>Tabela (a primeira linha é o cabeçalho). Altura estimada só para as linhas acharem a borda.</summary>
        public int Table(double x, double y, double w, params string[][] rows) =>
            Put(new BoardItem { Tipo = "tabela", X = x, Y = y, Largura = w, Tabela = rows.Select(r => r.ToList()).ToList(), Fixacao = "nenhuma" }, w, rows.Length * 44);

        /// <summary>Ícone da biblioteca de formas (lâmpada, livro, estrela...), centrado em (cx, cy).</summary>
        public int Icon(double cx, double cy, double size, string shape, string fill, string border) =>
            ShapeAt(cx, cy, size, size, shape, fill, border, "", 14, "#FF1D2130", false, 2);

        /// <summary>Ponto do meio de um lado do item (onde a linha encosta).</summary>
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

        // Cria o item de linha com as duas pontas.
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

        /// <summary>Fecha o quadro: linhas atrás, compacta (scale) e posiciona a câmera no centro.</summary>
        public Board Done(bool hideTitle = false, double scale = 1)
        {
            // Linhas atrás de tudo: os itens ficam por cima das pontas.
            int z = 1;
            foreach (var it in _items.OrderBy(i => i.Tipo == "linha" ? 0 : 1).ThenBy(i => i.Id)) it.Z = z++;
            if (scale != 1) Shrink(scale);

            // Abre no meio do conteúdo, num zoom em que o texto dos blocos dá para ler
            // (abaixo de 34% o quadro mostra só os títulos). Afastar mostra o mapa inteiro.
            var boxes = _items.Where(i => i.Tipo != "linha").Select(Box).ToList();
            double minX = boxes.Min(r => r.X), maxX = boxes.Max(r => r.X + r.W), minY = boxes.Min(r => r.Y), maxY = boxes.Max(r => r.Y + r.H);
            double zoom = Math.Clamp(Math.Min(1150 / (maxX - minX), 680 / (maxY - minY)), 0.55, 1);
            return new Board
            {
                Modelo = model, Itens = _items, TituloOculto = hideTitle, Diagrama = true,
                Camera = new Camera { X = (minX + maxX) / 2, Y = (minY + maxY) / 2, Zoom = zoom },
            };
        }

        // Caixa atual do item (tamanho escolhido ou o estimado ao criar).
        (double X, double Y, double W, double H) Box(BoardItem it)
        {
            var (_, _, w, h) = _boxes[it.Id];
            return (it.X, it.Y, it.Largura ?? w, it.Altura ?? h);
        }

        /// <summary>
        /// Deixa o design mais compacto: posições, formas e letras das formas e títulos encolhem; blocos,
        /// tabelas, caixas e post-its (letra fixa) mantêm um tamanho mínimo, centrados onde estavam.
        /// </summary>
        void Shrink(double s)
        {
            foreach (var it in _items)
            {
                if (it.Linha is { } l)
                {
                    foreach (var e in new[] { l.De, l.Para }) { e.X *= s; e.Y *= s; }
                    it.X *= s; it.Y *= s;
                    if (it.Largura is { } lw) it.Largura = Math.Max(4, lw * s);
                    if (it.Altura is { } lh) it.Altura = Math.Max(4, lh * s);
                    continue;
                }
                var (x, y, w, h) = Box(it);
                double cx = (x + w / 2) * s, cy = (y + h / 2) * s;
                (double minW, double minH) = it.Tipo switch
                {
                    "bloco" => (250, 110),
                    "tabela" => (300, 0),
                    "caixa" => (220, 70),
                    _ => (0, 0),
                };
                double nw = it.Tipo == "postit" ? 172 : Math.Max(minW, w * s);
                double nh = it.Tipo == "postit" ? 172 : Math.Max(minH, h * s);
                if (it.Tipo != "postit")
                {
                    if (it.Largura is not null) it.Largura = nw;
                    if (it.Altura is not null) it.Altura = nh;
                }
                if (it.Estilo is { } st && it.Tipo is "forma" or "texto") st.Tamanho = Math.Max(12, Math.Round(st.Tamanho * s, 1));
                // Forma com texto: nunca menor que o texto (a forma tem 16 px de margem por dentro).
                if (it is { Tipo: "forma", Estilo: { } ts } && it.Texto.Length > 0)
                {
                    var rows = it.Texto.Split('\n');
                    nw = Math.Max(nw, rows.Max(r => r.Length) * ts.Tamanho * 0.6 + 36);
                    nh = Math.Max(nh, rows.Length * ts.Tamanho * 1.35 + 34);
                    it.Largura = nw;
                    it.Altura = nh;
                }
                it.X = cx - nw / 2;
                it.Y = cy - nh / 2;
                _boxes[it.Id] = (it.X, it.Y, nw, nh);
            }
        }
    }

    // Cores da paleta em "#AARRGGBB" (papel e tinta) e a mesma cor com outro alfa.
    static (string Paper, string Ink) C(string key)
    {
        var c = Palette.Get(key);
        return ($"#FF{c.Paper.R:X2}{c.Paper.G:X2}{c.Paper.B:X2}", $"#FF{c.Ink.R:X2}{c.Ink.G:X2}{c.Ink.B:X2}");
    }

    static string Alpha(string hex, byte a) => $"#{a:X2}{hex[3..]}";

    // ---------- estilo "teia" (pedidos depois): pastel, caderno, orgânico e conceitos ----------

    /// <summary>Largura de um texto solto numa forma transparente (estimada pelo tamanho da letra).</summary>
    static double TextWidth(string text, double size) => text.Split('\n').Max(l => l.Length) * size * 0.6 + 50; // + as margens do texto da forma

    /// <summary>Texto sem caixa: forma sem fundo e sem borda, centrada em (cx, cy).</summary>
    static int Label(Builder b, double cx, double cy, string text, double size, string ink, bool bold = false, string font = "Inter", string align = "centro", double? width = null)
    {
        double w = width ?? TextWidth(text, size);
        double h = text.Split('\n').Length * size * 1.4 + 40;
        return b.Shape(cx - w / 2, cy - h / 2, w, h, "retangulo", "#00FFFFFF", "#00FFFFFF", text, size, ink, bold, 0, font, align);
    }

    // ---------- mapa pastel ----------

    static Board Pastel()
    {
        var b = new Builder("liso");
        const string ink = "#FF3A3F4B";
        int center = b.Shape(-320, -165, 640, 330, "pincelada", "#FFF8D5DE", "#00FFFFFF", "Mapa\nmental", 76, "#FF4A4D55", false, 0, "Fredoka");
        (string Title, string Key, double X, double Y, string Text, string Icon)[] topics =
        [
            ("Planejamento", "ceu", -780, -340, "Defina o que fazer primeiro, os prazos e quem ajuda em cada parte.", "documento"),
            ("Benefício", "areia", 0, -500, "O que melhora para você e para quem está perto.", "estrela"),
            ("Conceito", "amarelo", 780, -340, "Explique a ideia em poucas palavras, como para um amigo.", "lampada"),
            ("Pesquisa", "verde", -920, 0, "Junte fontes, exemplos e números que confirmem a ideia.", "livro"),
            ("Solução", "turquesa", 920, 0, "Como resolver o problema, passo a passo.", "raio"),
            ("Valores", "lima", -780, 340, "O que é importante e não pode ficar de fora.", "coracao"),
            ("Checklist", "rosa", 0, 520, "- Revisar o plano com calma\n- Pedir a opinião de alguém\n- Marcar o que já foi feito", "check"),
            ("Resultado", "menta", 780, 340, "Como saber que deu certo: metas e sinais de avanço.", "bandeira"),
        ];
        foreach (var (title, key, x, y, text, icon) in topics)
        {
            double w = 340, h = title == "Checklist" ? 190 : 160;
            int block = b.Block(x - w / 2, y - h / 2, w, h, key, title, text);
            b.Link(center, block, ink, "curva", 2.2, arrow: true);
            var (paper, dark) = C(key);
            b.Icon(x + w / 2 - 6, y - h / 2 - 6, 56, icon, paper, dark);
        }
        // enfeites leves em volta do centro
        foreach (var (x, y, s) in new[] { (-390.0, -200.0, 34.0), (400, -190, 26), (380, 215, 30), (-360, 210, 22) })
            b.Icon(x, y, s, "estrela", "#FFFFE9A8", "#FFD9A13A");
        b.Text(-1360, -860, "Como montar o seu", 44, "framboesa", "Pacifico");
        b.PostIt(-1360, -680, "rosa", "- Uma ideia por bloco\n- Palavras-chave\n- Cores iguais = ideias ligadas", "alfinete", lined: true, title: "Lembrar");
        b.Box(1140, -760, 360, 100, "cinza", "Dica: 2 cliques no vazio criam outro item igual ao último.", 17);
        b.PostIt(1180, 560, "amarelo", "Comece pelo centro!", "fita");
        b.Table(-1400, 560, 460, ["Etapa", "Quando", "Feito"], ["Pesquisar", "Semana 1", "✓"], ["Planejar", "Semana 2", ""], ["Apresentar", "Semana 3", ""]);
        return b.Done(hideTitle: true, scale: 0.62);
    }

    // ---------- caderno de estudos ----------

    static Board Notebook()
    {
        var b = new Builder("pontilhado");
        const string dark = "#FF2A2E39";
        b.Text(-500, -820, "Resumo · Informática", 60, "branco", "Permanent Marker");
        b.Icon(-560, -770, 50, "estrela", "#FFFFE27A", "#FF8A6400");
        b.Icon(560, -770, 40, "estrela", "#FFFFB1C6", "#FFA61F4C");
        int cloud = b.ShapeAt(0, 0, 440, 320, "nuvem", "#FFFFFFFF", dark, "REDES E\nINTERNET", 36, dark, false, 3.5, "Fredoka");
        b.Icon(0, -175, 54, "sol", "#FFFFE27A", "#FF8A6400");

        (string Title, string Key, double X, double Y, double W, double H, string Body, string Icon)[] blocks =
        [
            ("NAVEGADORES", "azul", -800, -430, 400, 220, "Programas para acessar as páginas da internet.\n- Google Chrome\n- Mozilla Firefox\n- Microsoft Edge", "documento"),
            ("BUSCAS", "roxo", -840, 90, 420, 260, "- \"aspas\": a expressão exata\n- site: procura num site só\n- filetype: tipo de arquivo (pdf)\n- intitle: palavra no título\n- -palavra: tira do resultado", "interrogacao"),
            ("E-MAIL", "laranja", 840, 130, 420, 230, "- CC: cópia, todos veem\n- CCO: cópia oculta\n- Anexo: arquivo junto\n- Spam: lixo eletrônico", "nuvem-chat"),
        ];
        foreach (var (title, key, x, y, w, h, body, icon) in blocks)
        {
            var (paper, ink) = C(key);
            int block = b.Block(x - w / 2, y - h / 2, w, h, key, title, body);
            b.Link(cloud, block, ink, "curva", 6);
            b.Icon(x - w / 2 + 4, y - h / 2 - 4, 54, icon, paper, ink);
        }

        // Atalhos: bloco curto e tabela embaixo
        var (gp, gi) = C("verde");
        int atalhos = b.Block(560, -620, 420, 120, "verde", "ATALHOS", "Para ganhar tempo no navegador.");
        b.Icon(564, -624, 54, "raio", gp, gi);
        b.Link(cloud, atalhos, gi, "curva", 6);
        int table = b.Table(560, -470, 420, ["Ação", "Atalho"], ["Nova guia", "Ctrl+T"], ["Fechar guia", "Ctrl+W"], ["Reabrir guia", "Ctrl+Shift+T"], ["Histórico", "Ctrl+H"], ["Procurar", "Ctrl+F"], ["Favoritos", "Ctrl+D"]);
        _ = table;

        // Protocolos: bloco e quatro cartões
        var (pp, pi) = C("indigo");
        int proto = b.Block(-200, 300, 400, 120, "indigo", "PROTOCOLOS", "Regras para os computadores conversarem.");
        b.Icon(-196, 296, 54, "pasta", pp, pi);
        b.Link(cloud, proto, pi, "curva", 6, fromSide: "baixo", toSide: "cima");
        (string Name, string Text)[] cards = [("HTTP", "páginas da web\nporta 80"), ("HTTPS", "páginas seguras\nporta 443"), ("FTP", "envio de arquivos\nportas 20 e 21"), ("SMTP", "envio de e-mails\nporta 587")];
        for (int i = 0; i < cards.Length; i++)
        {
            int card = b.ShapeAt(-420 + i * 280, 560, 240, 110, "retangulo-arredondado", "#FFFFFFFF", pi, cards[i].Name + "\n" + cards[i].Text, 17, dark, false, 2);
            b.Link(proto, card, pi, "curva", 2.5, fromSide: "baixo", toSide: "cima");
        }

        // Avisos e lembretes
        b.Icon(-1090, 340, 62, "alerta", "#FFFF9E99", "#FFB3261E");
        Label(b, -870, 340, "Cai muito na prova!", 22, "#FFB3261E", bold: true);
        b.PostIt(-1180, 460, "amarelo", "Use a internet com sabedoria!", "fita");
        b.PostIt(1000, 460, "verde", "Informação boa leva longe!", "fita");
        int review = b.PostIt(1180, -300, "rosa", "- Protocolos\n- Atalhos\n- Buscas", "alfinete", lined: true, title: "Revisar");
        b.Link(review, atalhos, "#FFC2A06E", "curva", 3, trace: "barbante");
        b.Block(-220, 720, 440, 130, "lavanda", "Lembrete", "HTTPS tem cadeado: os dados vão protegidos.");
        b.Box(480, 740, 300, 80, "cinza", "Fonte: aula 3 · página 42", 16);
        return b.Done(hideTitle: true, scale: 0.6);
    }

    // ---------- teia orgânica (ramos que afinam) ----------

    static Board Organic()
    {
        var b = new Builder("liso");
        b.TextCentered(0, -800, "Plano do app", 76, "branco", "Caveat", 0.42);
        int center = b.ShapeAt(0, 0, 230, 110, "retangulo", "#FF9A9DA3", "#00FFFFFF", "meu app", 28, "#FFFFFFFF", true, 0);
        b.Icon(0, -105, 66, "lampada", "#FFFFE27A", "#FF8A6400");
        (string Name, string Color, double X, double Y, (string Leaf, string[] Subs)[] Leaves)[] branches =
        [
            ("Problema", "#FFC77B6B", -560, -300, [("Ninguém acha nada", ["arquivos espalhados", "pastas demais"]), ("Tudo é lento", ["muitos cliques"]), ("Difícil de dividir", ["só por e-mail"])]),
            ("Solução", "#FF7FAE4E", -560, 300, [("Simples de usar", ["um clique", "sem cadastro"]), ("Rápido", ["abre na hora"]), ("Bonito", ["cores leves", "letras grandes"])]),
            ("Público", "#FF4F8DCB", 560, -330, [("Estudantes", ["resumos", "trabalhos em grupo"]), ("Professores", ["aulas"]), ("Equipes pequenas", ["projetos", "reuniões"])]),
            ("Concorrência", "#FFE0A93A", 600, 30, [("Caro demais", []), ("Complicado", []), ("Sem versão grátis", [])]),
            ("História", "#FF9A79C6", 560, 380, [("Ideia em 2026", ["num caderno"]), ("Primeiro teste", ["10 amigos"]), ("Lançamento", ["em breve"])]),
        ];
        foreach (var (name, color, x, y, leaves) in branches)
        {
            double side = Math.Sign(x);
            int label = Label(b, x, y, name, 30, color, bold: true);
            b.Link(center, label, color, "curva", 9);
            for (int i = 0; i < leaves.Length; i++)
            {
                var (leaf, subs) = leaves[i];
                double ly = y + (i - (leaves.Length - 1) / 2.0) * 115;
                double lx = x + side * (TextWidth(name, 30) / 2 + 60 + TextWidth(leaf, 20) / 2);
                int l = Label(b, lx, ly, leaf, 20, "#FF3A3F4B");
                b.Link(label, l, color, "curva", 3.5, fromSide: side > 0 ? "direita" : "esquerda", toSide: side > 0 ? "esquerda" : "direita");
                for (int k = 0; k < subs.Length; k++)
                {
                    double sy = ly + (k - (subs.Length - 1) / 2.0) * 46;
                    double sx = lx + side * (TextWidth(leaf, 20) / 2 + 30 + TextWidth(subs[k], 16) / 2);
                    int s = Label(b, sx, sy, subs[k], 16, "#FF6E737C");
                    b.Link(l, s, color, "curva", 1.5, fromSide: side > 0 ? "direita" : "esquerda", toSide: side > 0 ? "esquerda" : "direita");
                }
            }
        }
        int mission = b.Block(-200, -620, 400, 140, "amarelo", "Missão", "Ajudar quem estuda a organizar ideias sem complicação.");
        b.Link(mission, center, "#FF9A9DA3", "curva", 2, arrow: true, fromSide: "baixo", toSide: "cima");
        b.Block(-220, 560, 440, 210, "verde", "Próximos passos", "- Desenhar as telas\n- Testar com 5 pessoas\n- Corrigir o que confundir\n- Lançar a versão 1");
        b.Table(-1420, 600, 460, ["Mês", "Meta", "Status"], ["Jan", "Protótipo", "✓"], ["Mar", "10 usuários", "…"], ["Jun", "Lançar", ""]);
        int idea = b.PostIt(330, 560, "rosa", "Ideia: modo escuro", "alfinete");
        b.Link(idea, center, "#FF9A79C6", "curva", 2, arrow: true, trace: "barbante");
        b.Box(1060, -800, 380, 100, "cinza", "Concorrentes: compare preço, facilidade e versão grátis.", 17);
        b.Icon(1040, -750, 44, "bandeira", "#FFFFBE8C", "#FFA4470E");
        return b.Done(hideTitle: true, scale: 0.75);
    }

    // ---------- teia de conceitos ----------

    static Board ConceptWeb()
    {
        var b = new Builder("liso");
        const string line = "#FF6E737C", dark = "#FF2A2E39";
        int Node(string text, double x, double y, int level)
        {
            double size = level switch { 0 => 26, 1 => 19, _ => 17 };
            var (fill, border) = level switch
            {
                0 => ("#FFD9D2EE", dark),
                1 => ("#FFCFC8EA", "#FF6B6496"),
                _ => ("#FFC9DEF5", "#FF4C7FB5"),
            };
            double w = text.Split('\n').Max(l => l.Length) * size * 0.58 + 44;
            double h = level == 0 ? 78 : text.Contains('\n') ? 70 : 52;
            return b.ShapeAt(x, y, w, h, "retangulo", fill, border, text, size, dark, level < 2, level == 0 ? 2.5 : 1.5);
        }
        void Edge(int a, int c) => b.Link(a, c, line, "reta", 1.6);

        b.Text(-640, -820, "Psicologia da personalidade", 60, "indigo", "Playfair Display");
        int root = Node("Psicologia", 0, 0, 0);
        b.Icon(0, -78, 48, "lampada", "#FFFFE27A", "#FF8A6400");
        // Testes
        int tests = Node("Testes", 300, -170, 1); Edge(root, tests);
        int mbti = Node("MBTI", 260, -380, 2); Edge(tests, mbti);
        foreach (var (t, x, y) in new[] { ("Quatro\nindicadores", 80, -540), ("16 tipos", 430, -540), ("Simétrico", 470, -400) }) Edge(mbti, Node(t, x, y, 2));
        int enne = Node("Eneagrama", 640, -170, 2); Edge(tests, enne);
        int types = Node("9 tipos", 860, -40, 2); Edge(enne, types);
        foreach (var (t, x, y) in new[] { ("Perfeccionista", 1080, 90), ("Ajudante", 1100, -60), ("Realizador", 1060, -190), ("Não estático", 700, -320) })
            Edge(t == "Não estático" ? enne : types, Node(t, x, y, 2));
        int big = Node("Big Five", 260, 220, 2); Edge(tests, big);
        foreach (var (t, x, y) in new[] { ("Abertura", 520, 300), ("Extroversão", 440, 420), ("Amabilidade", 220, 470), ("Neuroticismo", 20, 400) }) Edge(big, Node(t, x, y, 2));
        b.Icon(370, 175, 34, "estrela", "#FFFFE27A", "#FF8A6400");
        // Críticas
        int crit = Node("Críticas", -320, -230, 1); Edge(root, crit);
        int valid = Node("Validade\ncientífica", -420, -420, 2); Edge(crit, valid);
        foreach (var (t, x, y) in new[] { ("Viés", -620, -520), ("Discriminação", -660, -380) }) Edge(valid, Node(t, x, y, 2));
        Edge(crit, Node("Confiabilidade", -140, -400, 2));
        Edge(crit, Node("Ética", -580, -250, 2));
        // Benefícios
        int ben = Node("Benefícios", -560, 0, 1); Edge(root, ben);
        foreach (var (t, x, y) in new[] { ("Comunicação", -820, -110), ("Empatia", -860, 20), ("Relações", -820, 140) }) Edge(ben, Node(t, x, y, 2));
        // Aplicações
        int app = Node("Aplicações", -300, 250, 1); Edge(root, app);
        Edge(app, Node("Coaching", -560, 190, 2));
        Edge(app, Node("Liderança", -600, 330, 2));
        int grow = Node("Crescimento", -420, 450, 2); Edge(app, grow);
        foreach (var (t, x, y) in new[] { ("Planejamento", -640, 560), ("Carreira", -380, 600) }) Edge(grow, Node(t, x, y, 2));
        Edge(app, Node("Treinamento", -140, 460, 2));

        // Blocos de apoio, legenda e lembretes
        int summary = b.Block(700, 330, 420, 190, "ceu", "Resumo", "Os testes descrevem como cada pessoa tende a agir. Ajudam a se conhecer, mas não são um rótulo.");
        b.Link(big, summary, "#FF4C7FB5", "curva", 2, arrow: true, trace: "barbante");
        b.Block(-1300, -640, 400, 170, "lilas", "Para lembrar", "- MBTI: 16 tipos\n- Eneagrama: 9 tipos\n- Big Five: 5 traços");
        b.Block(-1300, -420, 400, 150, "pessego", "Pergunta", "Um teste pode mudar com o tempo?");
        b.PostIt(-1200, 300, "amarelo", "Prova: MBTI e Big Five", "alfinete");
        b.Box(900, -820, 420, 190, "cinza", "", 16);
        b.ShapeAt(950, -770, 40, 30, "retangulo", "#FFCFC8EA", "#FF6B6496", "", 14, dark, false, 1.5);
        Label(b, 1080, -770, "Tema principal", 18, dark, align: "esquerda", width: 200);
        b.ShapeAt(950, -720, 40, 30, "retangulo", "#FFC9DEF5", "#FF4C7FB5", "", 14, dark, false, 1.5);
        Label(b, 1080, -720, "Detalhe", 18, dark, align: "esquerda", width: 200);
        b.Free(930, -670, 970, -670, line, 1.6);
        Label(b, 1080, -670, "Ligação", 18, dark, align: "esquerda", width: 200);
        return b.Done(hideTitle: true, scale: 0.8);
    }

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
        b.Block(-1260, -760, 380, 180, "ceu", "Como usar", "- Troque os textos\n- Some ramos com o botão direito\n- Use cores por assunto");
        b.PostIt(1000, -760, "amarelo", "Escreva só palavras-chave", "fita");
        b.Table(900, 580, 440, ["Ramo", "Prazo"], ["Objetivo", "Hoje"], ["Recursos", "Semana 1"], ["Riscos", "Semana 2"]);
        b.Icon(-1120, 640, 60, "lampada", "#FFFFE27A", "#FF8A6400");
        b.Block(-1050, 580, 360, 150, "lilas", "Ideia solta", "Anote aqui o que ainda não tem lugar no mapa.");
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
        b.Block(900, 150, 380, 210, "verde", "Plano de ação", "- Escrever um passo a passo\n- Treinar a equipe\n- Medir toda semana");
        b.Table(900, -470, 380, ["Causa", "Peso"], ["Pessoas", "Alto"], ["Métodos", "Médio"], ["Ambiente", "Baixo"]);
        b.Icon(1300, 120, 56, "check", "#FFC6EC9E", "#FF3B770F");
        b.PostIt(-1000, 420, "amarelo", "Pergunte 5 vezes: por quê?", "alfinete");
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
        b.Block(1820, -170, 340, 170, "indigo", "Meta do ano", "Lançar o projeto e celebrar com quem ajudou.");
        b.Icon(1990, -190, 56, "bandeira", "#FFFF6D00", "#FFFFFFFF");
        b.PostIt(-460, -100, "amarelo", "Revise todo mês", "fita");
        b.Table(-520, 220, 420, ["Mês", "Feito"], ["JAN", "✓"], ["MAR", "✓"], ["MAI", ""]);
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
        b.Table(-360, 500, 720, ["", "Gato", "Cachorro"], ["Energia", "Média", "Alta"], ["Espaço", "Pouco", "Mais"], ["Passeios", "Não precisa", "Todo dia"]);
        b.Block(-1120, -560, 360, 150, "rosa", "Gato", "Mais calmo e independente.");
        b.Block(760, -560, 360, 150, "azul", "Cachorro", "Mais agitado e companheiro.");
        b.Icon(0, -400, 54, "coracao", "#FFFFB1C6", "#FFA61F4C");
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
        b.Icon(-200, -230, 60, "livro", "#FFFFE27A", "#FF8A6400");
        b.Block(880, -460, 360, 180, "amarelo", "Rotina", "- 2 h por dia\n- 1 redação por semana\n- Simulado no sábado");
        b.PostIt(-1060, -460, "menta", "- Funções\n- Revolução Francesa\n- Crase", "alfinete", lined: true, title: "Semana 1");
        b.Table(-300, 380, 600, ["Matéria", "Nota", "Meta"], ["Matemática", "6", "8"], ["Redação", "7", "9"]);
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
        b.Box(420, -380, 300, 190, "cinza", "Verde: começo\nAzul: etapa\nAmarelo: pergunta\nVermelho: fim", 17);
        b.Block(-960, 360, 380, 150, "ceu", "Dica", "Cada losango é uma pergunta de sim ou não.");
        b.PostIt(440, 260, "rosa", "E se não der certo?", "alfinete");
        b.Icon(-300, -330, 56, "interrogacao", "#FFFFE27A", "#FF8A6400");
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
        b.Block(1360, -20, 360, 230, "lavanda", "Regras", "- No máximo 3 em Fazendo\n- Mova da esquerda para a direita\n- Revise na sexta");
        b.Icon(1700, -30, 56, "check", "#FFC6EC9E", "#FF3B770F");
        b.Table(1360, 260, 360, ["Quem", "Tarefas"], ["Ana", "2"], ["Leo", "3"]);
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
        b.Block(1240, -100, 360, 190, "ceu", "Dica", "Comece o dia pelo quadrado vermelho e agende o azul na agenda.");
        b.Icon(1580, -110, 56, "raio", "#FFFFE27A", "#FF8A6400");
        b.PostIt(1300, 200, "amarelo", "Diga não ao que é cinza", "fita");
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
        b.Block(720, -780, 400, 210, "lilas", "Perguntas", "- O que faço sem ver o tempo passar?\n- Em que as pessoas me pedem ajuda?");
        b.PostIt(-1060, -700, "rosa", "Escreva 3 respostas em cada círculo", "alfinete");
        b.Icon(-560, -320, 50, "coracao", "#FFFFB1C6", "#FFA61F4C");
        b.Icon(560, -320, 50, "sol", "#FFFFE27A", "#FF8A6400");
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
        b.Icon(-900, -560, 60, "estrela", "#FFFFE27A", "#FF8A6400");
        b.Icon(860, 620, 60, "coracao", "#FFFFB1C6", "#FFA61F4C");
        b.Icon(900, -620, 70, "sol", "#FFFFE27A", "#FF8A6400");
        b.Block(700, 560, 380, 170, "amarelo", "Este mês", "- Correr 3 vezes por semana\n- Guardar 10% do salário");
        return b.Done(hideTitle: true);
    }
}
