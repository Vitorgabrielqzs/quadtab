using System.Windows;

namespace Quadtab.Rendering;

/// <summary>
/// Índice espacial: responde rápido "quais itens estão nesta área?".
/// Cada item fica no menor quadrante que o contém por inteiro.
/// O quadro é infinito na prática: a raiz cobre ±1 milhão de unidades (o que passar disso fica na raiz).
/// </summary>
public sealed class QuadTree<T> where T : class
{
    const int MaxItems = 8, MaxDepth = 14;
    const double Half = 1 << 20;

    sealed class Node(Rect bounds, int depth)
    {
        public readonly Rect Bounds = bounds;
        public readonly int Depth = depth;
        public readonly List<(T Item, Rect Box)> Items = [];
        public Node[]? Kids;
    }

    readonly Node _root = new(new Rect(-Half, -Half, 2 * Half, 2 * Half), 0);
    readonly Dictionary<T, (Node Node, Rect Box)> _where = new();

    public int Count => _where.Count;

    public void Clear()
    {
        _where.Clear();
        _root.Items.Clear();
        _root.Kids = null;
    }

    public void Set(T item, Rect box)
    {
        if (_where.TryGetValue(item, out var cur))
        {
            if (cur.Box == box) return;
            // Ainda cabe no mesmo quadrante e ele não tem filhos melhores: só atualiza a caixa.
            if (cur.Node.Kids is null && cur.Node.Bounds.Contains(box))
            {
                int i = cur.Node.Items.FindIndex(e => ReferenceEquals(e.Item, item));
                cur.Node.Items[i] = (item, box);
                _where[item] = (cur.Node, box);
                return;
            }
            Remove(item);
        }
        Insert(_root, item, box);
    }

    public void Remove(T item)
    {
        if (!_where.Remove(item, out var cur)) return;
        int i = cur.Node.Items.FindIndex(e => ReferenceEquals(e.Item, item));
        if (i >= 0) cur.Node.Items.RemoveAt(i);
    }

    public void Query(Rect area, List<T> results) => Query(_root, area, results);

    public bool Any(Rect area) => Any(_root, area);

    /// <summary>Retângulo que envolve todos os itens (vazio se não houver itens).</summary>
    public Rect Extent()
    {
        var r = Rect.Empty;
        foreach (var (_, box) in _where.Values) r.Union(box);
        return r;
    }

    void Insert(Node node, T item, Rect box)
    {
        while (true)
        {
            if (node.Kids is null)
            {
                node.Items.Add((item, box));
                _where[item] = (node, box);
                if (node.Items.Count > MaxItems && node.Depth < MaxDepth) Split(node);
                return;
            }
            var kid = KidFor(node, box);
            if (kid is null)
            {
                node.Items.Add((item, box));
                _where[item] = (node, box);
                return;
            }
            node = kid;
        }
    }

    void Split(Node node)
    {
        var b = node.Bounds;
        double w = b.Width / 2, h = b.Height / 2;
        node.Kids =
        [
            new Node(new Rect(b.X, b.Y, w, h), node.Depth + 1),
            new Node(new Rect(b.X + w, b.Y, w, h), node.Depth + 1),
            new Node(new Rect(b.X, b.Y + h, w, h), node.Depth + 1),
            new Node(new Rect(b.X + w, b.Y + h, w, h), node.Depth + 1),
        ];
        var items = node.Items.ToArray();
        node.Items.Clear();
        foreach (var (item, box) in items)
        {
            var kid = KidFor(node, box);
            var target = kid ?? node;
            target.Items.Add((item, box));
            _where[item] = (target, box);
        }
    }

    static Node? KidFor(Node node, Rect box)
    {
        foreach (var k in node.Kids!)
            if (k.Bounds.Contains(box)) return k;
        return null;
    }

    static void Query(Node node, Rect area, List<T> results)
    {
        foreach (var (item, box) in node.Items)
            if (box.IntersectsWith(area)) results.Add(item);
        if (node.Kids is null) return;
        foreach (var k in node.Kids)
            if (k.Bounds.IntersectsWith(area)) Query(k, area, results);
    }

    static bool Any(Node node, Rect area)
    {
        foreach (var (_, box) in node.Items)
            if (box.IntersectsWith(area)) return true;
        if (node.Kids is null) return false;
        foreach (var k in node.Kids)
            if (k.Bounds.IntersectsWith(area) && Any(k, area)) return true;
        return false;
    }
}
