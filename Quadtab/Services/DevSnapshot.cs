using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quadtab.Models;
using Quadtab.ViewModels;

namespace Quadtab.Services;

/// <summary>
/// Só para desenvolvimento: abre o app fora da tela, aplica um estado (menu, bandeja, tema...),
/// salva uma imagem PNG da janela e fecha. Não é usado no uso normal.
/// </summary>
public static class DevSnapshot
{
    public static void Run(MainWindow window, string file, string actions)
    {
        window.ContentRendered += async (_, _) =>
        {
            await Task.Delay(900);
            var vm = App.ViewModel;
            foreach (var action in actions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (action)
                {
                    case "menu": window.ShowMenuAt(null, new Point(760, 330), new Point(900, 420)); break;
                    case "menu-item": window.ShowMenuAt(vm.Items.First(i => i.Id == 3), default, new Point(1000, 300)); break;
                    case "bandeja": vm.TrayOpen = true; break;
                    case "titulo": vm.TitleHidden = true; break;
                    case "lateral": vm.SidebarOpen = false; break;
                    case "editar": vm.BeginEdit(vm.Items.First(), EditField.Text); break;
                    case "selecionar": vm.Selected = vm.Items.First(i => i.Id == 6); break;
                    case "busca": vm.Search = "metas"; break;
                    case "branco": vm.Model = BoardModel.Branco; break;
                    case "lousa": vm.Model = BoardModel.Lousa; break;
                    case "pontilhado": vm.Model = BoardModel.Pontilhado; break;
                    case "zoom": window.Board.ZoomStep(+1); window.Board.ZoomStep(+1); break;
                    case "arrastar":
                        File.WriteAllText(file + ".arraste.txt", await window.Board.TestDragAsync(vm.Items.First(i => i.Id == 1), 200 / window.Board.Zoom * window.Board.Zoom, 100));
                        break;
                    case "longe": window.Board.PanBy(new Vector(-6000, 0)); break;
                    case "bandeja-cursor": window.ShowContextTrayAt(new Point(700, 420)); break;
                    case "pagina": App.OpenPage(StorageService.PageFiles().First()); break;
                    case "ler":
                        {
                            StorageService.CreatePage("Viagem", "# Viagem\n\nIdeias para o #chile.\n");
                            var p = StorageService.CreatePage("Roteiro", "# Roteiro\n\n## Antes de ir\n- [x] Passaporte\n- [ ] Seguro **viagem**\n- Ver [[Viagem]] e [[Orçamento]]\n> Pequenos passos.\n\nAbrir [[Quadro: Meu quadro]] · #planos #2027\n");
                            StorageService.CreatePage("Notas", "Veja [[Roteiro]].");
                            App.OpenPage(p);
                            break;
                        }
                    case "rapido": window.Quick.Show(); break;
                    case "procurar": window.TestSearchBox("metas"); break;
                    case "ajuda": Controls.HelpTips.ShowFor(window.MoreButton); break;
                    case "ajuda-lateral": Controls.HelpTips.ShowFor(window.SidebarView.FindFirstRow()); break;
                    case "dock": window.TestDock(); break;
                    case "cursores":
                        foreach (var sc in new[] { 1.0, 1.25, 1.5, 2.0 })
                        {
                            var e2 = new PngBitmapEncoder();
                            e2.Frames.Add(BitmapFrame.Create(Rendering.BoardCursors.Preview(sc)));
                            using var f2 = File.Create($"{file}.cursores-{sc * 100:0}.png");
                            e2.Save(f2);
                        }
                        break;
                    case "tema": ThemeService.Toggle(); break;
                    case "importante":
                        StorageService.ToggleFavorite(vm.FilePath);
                        StorageService.TogglePinned(StorageService.PageFiles().First());
                        break;
                    case "abrir-itens": App.Library.Boards[0].IsOpen = true; break;
                    case "busca-quadro": window.TestSearch("post"); break;
                    case "caneta": window.Board.Tool = Controls.BoardTool.Pen; break;
                    case "apresentar": vm.AddScene("Começo"); vm.AddScene("Fim"); window.TestPresent(); break;
                    case "galeria": App.Library.ShowGalleryCommand.Execute("todos"); break;
                    case "cheia": window.TestFullscreen(); break;
                    case "grade": vm.ShowGrid = true; break;
                    case "mais": window.TestMore(); break;
                    case "formas":
                        {
                            var shapes = new[] { "estrela", "coracao", "seta-direita", "hexagono" };
                            for (int i = 0; i < shapes.Length; i++)
                                vm.AddItem(new BoardItem { Tipo = "forma", Forma = shapes[i], X = 200 + i * 220, Y = 900, Fixacao = "nenhuma", Estilo = BoardItemViewModel.DefaultStyle(ItemKind.Shape) }, select: false);
                            var a = vm.Items.First(i => i.Id == 1);
                            var b = vm.Items.First(i => i.Id == 3);
                            window.Board.AddConnector(a, "direita", b, "esquerda");
                            vm.AddItem(new BoardItem { Tipo = "tabela", X = 1200, Y = 900, Fixacao = "nenhuma", Estilo = BoardItemViewModel.DefaultStyle(ItemKind.Table), Tabela = [["Tarefa", "Quem"], ["Fita", "Ana"], ["Metas", "Leo"]] }, select: false);
                            vm.AddItem(new BoardItem { Tipo = "desenho", X = 900, Y = 650, Fixacao = "nenhuma", Tracos = [new InkStroke { Pontos = [0, 40, 30, 0, 60, 40, 90, 0, 120, 40], Espessura = 4 }] }, select: false);
                            window.Board.Fit(animate: false);
                            break;
                        }
                    case "estresse":
                        {
                            // 500 itens espalhados + 3 s andando pela cena; grava a duração de cada quadro.
                            var rng = new Random(7);
                            var kinds = new[] { ItemKind.PostIt, ItemKind.PostIt, ItemKind.PostIt, ItemKind.Page, ItemKind.Block, ItemKind.Text };
                            for (int i = vm.Items.Count; i < 500; i++)
                                vm.Add(kinds[rng.Next(kinds.Length)], Palette.Colors[rng.Next(8)].Key, rng.Next(-3000, 4500), rng.Next(-2000, 3000));
                            vm.EndEdit();
                            vm.Selected = null;
                            window.Board.Fit(animate: false);
                            await Task.Delay(2000);
                            var frames = await window.Board.BenchmarkAsync(3);
                            frames.Sort();
                            double P(double q) => frames.Count == 0 ? 0 : frames[(int)Math.Min(frames.Count - 1, q * frames.Count)];
                            File.WriteAllText(file + ".txt",
                                $"quadros={frames.Count} em 3 s ({frames.Count / 3.0:0} fps) · mediana={P(0.5):0.0} ms · p95={P(0.95):0.0} ms · pior={P(1):0.0} ms · " +
                                $"itens={window.Board.ItemCount} criados={window.Board.RealizedCount} pico={window.Board.PeakRealized} zoom={window.Board.Zoom:0.00}");
                            break;
                        }
                }
                await Task.Delay(150);
            }
            await Task.Delay(1200);
            if (actions.Contains("faiscas"))
            {
                window.Sparks.Fire(new Point(900, 520));
                window.Sparks.Fire(new Point(1200, 300));
                await Task.Delay(110);
            }

            var root = (FrameworkElement)window.Content;
            var dpi = VisualTreeHelper.GetDpi(root);
            var bmp = new RenderTargetBitmap(
                (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bmp.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(file)) enc.Save(fs);
            Application.Current.Shutdown();
        };
    }
}
