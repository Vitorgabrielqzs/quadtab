# Quadtab — CLAUDE.md

Quadtab é um app de quadro visual para Windows: um quadro de cortiça (ou outros modelos) onde o usuário cola post-its de várias cores, páginas, textos e blocos de vidro, arrasta tudo livremente e organiza ideias como num mapa mental.

Este arquivo descreve **todo o design que o app deve ter agora**. O objetivo do código é reproduzir a interface do protótipo o mais fielmente possível.

- **Protótipo (fonte da verdade visual):** canvas "Quadtab" no Claude Design → https://claude.ai/artifact/Vc5jsxgtthfVdo65LP2mE9
  - `App · tema claro` (tela principal, interativa)
  - `App · tema escuro`
  - `Post-its em detalhe`
  - `Componentes de vidro`
- **Texturas:** `assets/cork.png` (cortiça, 384×384, repete sem emenda) e `assets/paper-grain.png` (grão do papel, 256×256, transparente).

Quando houver dúvida entre este arquivo e o protótipo, o protótipo vence.

---

## 1. Como trabalhar com o usuário

- Responder sempre em **português**, de forma direta e ágil.
- O usuário escreve o código ele mesmo, no **VS Code**, no **Windows 11**.
- **Antes de qualquer parte de código, parar e perguntar:** ele quer explicação passo a passo (cada linha, cada tag, do básico) ou vai fazer sozinho?
  - Se for passo a passo: ensinar uma coisa pequena por vez, explicando o que cada linha faz e por quê.
  - Se for sozinho: entregar vários passos por resposta (mais de 5), com o código e uma explicação curta de cada passo.
- Nunca usar fundo amarelo ou dourado na interface do app (o post-it amarelo é permitido, porque é cor de conteúdo).

## 2. Tecnologia

- **C# + .NET 10 (LTS) + WPF**, gerando um `.exe` para Windows.
- Padrão **MVVM** (sugestão: pacote `CommunityToolkit.Mvvm`).
- Salvamento em JSON com `System.Text.Json`.
- Executável final:
  `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

Estrutura sugerida:

```
Quadtab/
  App.xaml                 recursos globais (cores, fontes, estilos)
  MainWindow.xaml          janela e layout geral
  Themes/Light.xaml        tokens do tema claro
  Themes/Dark.xaml         tokens do tema escuro
  Controls/                GlassPanel, PostIt, PageNote, GlassBlock, TextLabel, Tray, BoardMenu
  Behaviors/GlassGlow.cs   brilho de borda que segue o mouse
  Models/                  BoardItem, Board
  ViewModels/              MainViewModel, BoardItemViewModel
  Services/StorageService.cs
  Assets/cork.png, Assets/paper-grain.png
  Assets/Fonts/ (Inter e Nunito, .ttf)
```

---

## 3. Tokens de design

### 3.1 Cores da interface

| Token | Claro | Escuro | Uso |
|---|---|---|---|
| `Bg` | `#EEF0F4` | `#0D0E12` | fundo da janela |
| `Blob1` | `#FFDDE8` | `#3A1C36` | mancha 640×520 no canto superior esquerdo |
| `Blob2` | `#D4E4FF` | `#162852` | mancha 760×620 no canto superior direito |
| `Blob3` | `#D7F4E7` | `#0E3226` | mancha 700×520 embaixo, a 35% da largura |
| `Ink` | `#0F1115` | `#F2F3F7` | texto principal |
| `Ink2` | `#4F5563` | `#B3B8C6` | texto secundário, ícones |
| `Ink3` | `#646A79` | `#959AAA` | rótulos, placeholders |
| `Glass` | branco 56% | `#1E202A` 55% | painéis de vidro |
| `Glass2` | branco 84% | `#282A36` 88% | menu e bandeja (vidro mais denso) |
| `Rim` | branco 95% | branco 42% | brilho da borda (canto superior esquerdo) |
| `Rim2` | branco 50% | branco 10% | brilho da borda (canto inferior direito) |
| `Fill` | `#767C91` 12% | branco 8% | fundo de botões no hover, campo de busca |
| `Fill2` | `#767C91` 20% | branco 15% | hover do menu, alça da bandeja |
| `Sep` | `#3C425A` 9% | branco 8% | separadores |
| `SegIndicator` | `#FFFFFF` | branco 20% | pílula do controle segmentado |
| `Accent` | `#0A84FF` | `#0A84FF` | cor de destaque (azul iOS) |
| `Danger` | `#E5312B` | `#FF5A52` | "Excluir" |
| `Success` | `#34C759` | `#34C759` | ponto "Salvo" |
| `GlowAlpha` | 0.34 | 0.16 | força do brilho interno do vidro |

- Fundo da janela: `Bg` com as três manchas em gradiente radial, que somem até transparente a 62% do raio.
- **Tema:** na primeira abertura acompanha o Windows (claro/escuro). Depois vale a última escolha do botão de tema no rodapé da lateral (salva em `estado.json`). A troca anima as cores em 0,3 s.
- Sombra padrão do vidro:
  - claro: `0 14 36` preto-azulado (`#1E243C`) a 10% + `0 1 3` a 6%
  - escuro: `0 16 40` preto a 42% + `0 1 3` a 30%

### 3.2 Cores dos post-its (8 cores)

Cada cor tem o **papel** (fundo) e a **tinta** (detalhes: rodapé, linhas, texto solto).

| Nome | Papel | Tinta |
|---|---|---|
| Amarelo | `#FFE27A` | `#8A6400` |
| Laranja | `#FFBE8C` | `#A4470E` |
| Rosa | `#FFB1C6` | `#A61F4C` |
| Roxo | `#D2BBFF` | `#5A2DB5` |
| Azul | `#A6D3FF` | `#1B5DA8` |
| Menta | `#A2EDD8` | `#0B7658` |
| Verde | `#C6EC9E` | `#3B770F` |
| Branco | `#FBFAF5` | `#2A2E39` |

As cores dos alfinetes e fitas vêm de uma lista, escolhida pelo `id % 5`: `#FF453A`, `#0A84FF`, `#30D158`, `#FF9F0A`, `#BF5AF2`.

### 3.3 Fontes

- **Inter** (interface, estilo das letras do iPhone): pesos 400, 500, 600, 700, 800.
- **Nunito** (conteúdo do quadro: post-its, páginas, textos): pesos 700, 800, 900.
- As duas são gratuitas (licença OFL). Embutir os `.ttf` no projeto.
- Na interface, usar espaçamento entre letras levemente negativo (-0.006em); nos títulos, mais negativo (valores na tabela).

| Elemento | Fonte | Tamanho | Peso | Detalhes |
|---|---|---|---|---|
| Título grande do quadro | Inter | 42 | 800 | tracking -0.04em, entrelinha 1.05 |
| Subtítulo do quadro | Inter | 15 | 500 | `Ink2` |
| Título inline na barra | Inter | 17 | 700 | tracking -0.022em |
| Marca "Quadtab" | Inter | 18 | 700 | subtítulo 12.5 / 500 / `Ink3` |
| Título de seção da lateral | Inter | 20 | 700 | tracking -0.025em |
| Linha da lateral | Inter | 15 | 500 | |
| Linha da lista "No quadro" | Inter | 14 | 500 | tipo à direita: 12 / `Ink3` |
| Botões das cápsulas | Inter | 14.5 | 600 | |
| Controle segmentado | Inter | 13.5 | 500 | selecionado: 700 |
| Linhas do menu | Inter | 15 | 500 | cabeçalho: 12.5 / 600 / `Ink3` |
| Título da bandeja | Inter | 20 | 700 | subtítulo 13.5 / `Ink2` |
| Status e dica | Inter | 13 | 500 | `Ink2` |
| Texto do post-it | Nunito | 19 | 900 | MAIÚSCULAS, entrelinha 1.26, tracking .01em, cor `#1C1812` 88% |
| Rodapé do post-it | Nunito | 10 | 800 | tracking .12em, cor da tinta a 85% |
| Título do pautado | Nunito | 13 | 900 | MAIÚSCULAS, tracking .06em, cor da tinta, entrelinha 20, margem inferior 7 |
| Itens do pautado | Nunito | 15 | 700 | entrelinha 27 |
| Título da página | Nunito | 18 | 900 | entrelinha 22, margem inferior 9 |
| Corpo da página | Nunito | 14 | 700 | entrelinha 28, cor `#33374A` |
| Título do bloco de vidro | Inter | 16.5 | 700 | tracking -0.02em |
| Corpo do bloco de vidro | Inter | 14 | 400 | entrelinha 1.7, cor `#333849` |
| Texto solto | Nunito | 50 | 900 | tracking .03em, cor da tinta |

### 3.4 Formas e espaçamentos

- Margem da janela: **14**; espaço entre barra lateral e área principal: **14**.
- Cantos arredondados:
  - barra lateral 32, moldura do quadro 32, área de cortiça 16
  - menu 24, bandeja 34, bloco de vidro 24
  - cápsulas e pílulas: totalmente redondas
  - linhas da lateral 13, linhas do menu 14
- Ícones: traço de 1.9, cantos arredondados, 19×19 (estilo SF Symbols, sem preenchimento).

### 3.5 Movimento

| Nome | Duração | Curva | Uso |
|---|---|---|---|
| Mola de toque | 0.5s | cubic-bezier(.25, 1.7, .45, 1) | ao clicar, o botão vai a 93% do tamanho e volta |
| Mola de item | 0.45s | cubic-bezier(.3, 1.5, .5, 1) | hover dos itens do quadro |
| Mola de painel | 0.55s | cubic-bezier(.3, 1.4, .5, 1) | abrir a bandeja |
| Pop do menu | 0.4s | cubic-bezier(.3, 1.45, .5, 1) | menu cresce de 86% a 100%, a partir do canto onde abriu |
| Indicador segmentado | 0.5s | cubic-bezier(.3, 1.35, .5, 1) | pílula desliza até a opção |
| Título recolhendo | 0.5s | cubic-bezier(.3, 1, .4, 1) | altura, opacidade e deslocamento |
| Lateral recolhendo | 0.45s | cubic-bezier(.3, 1.15, .4, 1) | largura 292 → 0 |

No WPF, as curvas com "passar do ponto" podem ser feitas com `BackEase` (EaseOut, Amplitude ~0.4–0.6) ou com `KeySpline`.

---

## 4. O vidro (Liquid Glass)

Este é o efeito principal. Todo painel de vidro tem quatro camadas:

1. **Fundo:** cor `Glass` (ou `Glass2`) sobre o que está atrás, desfocado (blur 26, saturação 190%).
2. **Sombra:** a sombra padrão (3.1).
3. **Borda de luz (1.3 px):** sem borda sólida. A borda é só luz, com duas partes:
   - gradiente fixo a 150°: `Rim` no canto superior esquerdo → transparente em 28% → transparente em 66% → `Rim2` no canto inferior direito;
   - mais um brilho radial de raio 190 px **centrado na posição do mouse**, com opacidade `0.2 + 0.8 × proximidade`.
4. **Brilho interno:** gradiente radial branco de raio 300 px, centrado no mouse, com opacidade `GlowAlpha × proximidade`. Ele fica atrás do conteúdo e na frente do fundo.

**Proximidade:** `prox = max(0, 1 − distância / 170)`.
- A distância é medida do mouse até a borda do painel, e vale 0 quando o mouse está dentro.
- Assim, a borda começa a brilhar **antes** do mouse encostar, como uma luz se aproximando.
- Quando o mouse sai da janela, `prox` volta a 0 em 0.3s.

**Ao clicar:** o elemento afunda (escala 0.93) e volta com a mola de toque.

**Elementos de vidro:**
- barra lateral
- cápsulas da barra de ferramentas
- botão redondo de ocultar título
- dica, status, dock e bandeja
- menu do botão direito
- blocos de vidro colados no quadro

**Como fazer no WPF** (o WPF não tem desfoque de fundo nativo):
- **Desfoque:** um `VisualBrush` da área do quadro que fica atrás do painel, com `BlurEffect` (raio ~26), recortado no formato arredondado do painel. Por cima, a cor `Glass`. Para economizar processamento, usar `BitmapCache`. Se ficar pesado, usar um fundo mais opaco (branco ~80%) sem desfoque como plano B, sem mudar o restante.
- **Borda de luz:** um `Border` extra de 1.3 px por cima. O `BorderBrush` é um `RadialGradientBrush` com `MappingMode="Absolute"` e `Center`/`GradientOrigin` atualizados pelo mouse, somado ao `LinearGradientBrush` fixo (dois `Border` empilhados).
- **Brilho interno:** um `Border` atrás do conteúdo, com `RadialGradientBrush` e `Opacity = GlowAlpha × prox`.
- **Comportamento anexado `GlassGlow`:** escuta o `MouseMove` da janela e, para cada elemento marcado como vidro, calcula a posição relativa e `prox`. Atualizar no máximo uma vez por quadro, com `CompositionTarget.Rendering`.
- **Janela:** sem a moldura padrão do Windows (`WindowChrome`), com cantos arredondados e o fundo `Bg` com as manchas.

---

## 5. Layout da janela

```
┌─────────────────────────────────────────────────────────────────────┐
│ ┌──────────────┐  ┌ barra de ferramentas ────────────────────────┐  │
│ │ BARRA        │  │ [título inline]            [⌃ título] [↶ ↷] […] [– □ ✕] │
│ │ LATERAL      │  └──────────────────────────────────────────────┘  │
│ │ (vidro)      │   Meu quadro                     [Cortiça|Branco|Lousa|Pontilhado] (^) │
│ │              │   14 itens · Cortiça · editado agora               │
│ │              │  ┌ moldura de madeira ──────────────────────────┐  │
│ │              │  │ [dica]                                       │  │
│ │              │  │            QUADRO (cortiça + itens)          │  │
│ │              │  │                                              │  │
│ │              │  │              [ dock da bandeja ]    [status] │  │
│ └──────────────┘  └──────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

Tamanho de referência: 1440×920. Mínimo sugerido: 1100×720.

### 5.1 Barra lateral (292 de largura)

No estilo da barra lateral do app Claude: lista limpa, linhas compactas (34 de altura, raio 10, hover `Fill`), ícones de traço à esquerda, rótulos de seção pequenos e cinzas (12.5/600/`Ink3`) com setinha que abre e fecha, filhos recuados 16 px e "Ver todos" em cinza. Mantém o vidro (raio 32), as cores, as fontes e as animações do Quadtab. **Não há** ícone/nome do app, títulos grandes de seção nem campo de busca fixo.

De cima para baixo:
1. **Topo:** recolher a lateral (largura 292 → 18: fica uma **faixa de vidro à mostra** com uma alcinha vertical `Fill2` de 4×44; clicar na faixa reabre. Ela nunca some de vez fora da tela cheia), **voltar** e **avançar** (histórico de quadros e páginas abertos; também Alt+← / Alt+→). O espaço livre arrasta a janela.
2. **Ações:** Novo quadro (Ctrl+N) · Procurar (Ctrl+K) · Nova página · Apagados recentemente (com contador).
3. **Quadros** (sem rótulo): só o nome, pela ordem do último aberto, até 8 e depois "Ver todos" (galeria).
   - O quadro aberto fica numa **pílula** de destaque (`Fill2`).
   - Com o mouse em cima aparecem à direita: **fixar**, **marcar como importante** (estrela laranja fica visível) e **"…"** (abrir, renomear, duplicar, fixar, importante, excluir). Botão direito abre o mesmo menu; dois cliques ou F2 renomeiam.
   - A setinha à esquerda abre os **itens do quadro** (post-its com o quadradinho da cor, páginas, textos, formas…), recuados. Clicar num item abre o quadro, se preciso, e leva a câmera até ele.
4. **Fixados ▾** · 5. **Importantes ▾:** quadros e páginas marcados, cada um com seus filhos e "Ver todos". Só aparecem quando há algo marcado.
6. **Páginas ▾:** as páginas (filhos = os títulos # da página).
7. **Cofre ▾:** a árvore da pasta `Documentos\Cofre` (pastas abrem e fecham; arquivos abrem: quadro, página ou no programa padrão). Arrastar um quadro sobre uma pasta de `Quadros` move.
- **Rodapé:** botão de tema (sol no escuro, lua no claro; troca com giro e mola) e **Ajustes** (seguir o tema do Windows, faíscas ao clicar, inspetor, contador de FPS, abrir a pasta Cofre).
- O estado aberto/fechado de cada seção e de cada linha fica salvo.

### 5.2 Barra de ferramentas

- Cápsulas de vidro com 50 de altura e padding 5. Os botões dentro têm 40 de altura, mínimo de 40 de largura, padding horizontal 13 e hover `Fill`.
- Da esquerda para a direita:
  1. **título inline** (17/700), invisível enquanto o título grande aparece. Quando o título grande some, este surge subindo 8 px e ganhando opacidade;
  2. espaço livre;
  3. **mostrar/ocultar título**: cápsula com a seta, **sempre à mostra**; a seta gira 180° (para baixo) quando o título está escondido;
  4. desfazer · refazer;
  5. "…" (mais opções; a tela cheia fica dentro dele e no F11);
  6. minimizar · maximizar · fechar.
- **Não há** botão de tela cheia nem cápsula "Bandeja" na barra (removidos de propósito). A bandeja abre pelo dock, pelo botão direito ou pelo menu.
- **Botões que abrem algo fecham no segundo clique** ("…", Ajustes, "…" das linhas da lateral, galerias da lateral). O menu guarda o botão "dono" (`ShowActions(..., owner)`).
- **O ✕ fecha o app sempre**, mesmo com Procurar, galeria ou menus abertos por cima (a janela testa o clique na área do ✕ antes de tudo). Salvar ao fechar nunca impede o fechamento: erros vão para `erros.log`.
- **Não há botões de zoom.** O zoom é pela rodinha, pela pinça e por Ctrl + / Ctrl − / Ctrl 0 (Ctrl+1 ajusta à tela). Sempre que o zoom muda, a porcentagem aparece num **balão** igual ao do contador de FPS (mesmo vidro, fonte 13/600, canto inferior esquerdo do quadro, a 30/32 px): surge com fade e leve mola, atualiza em tempo real e some 1,2 s depois que o zoom para. Com o FPS ligado, fica logo abaixo dele.

### 5.3 Título grande do quadro

- Fica entre a barra e o quadro, com padding horizontal de 10.
- **Esquerda:** "Meu quadro" (42/800) e, embaixo, "14 itens · Cortiça · editado agora".
- **Direita:**
  - (o botão de ocultar/mostrar fica na barra de ferramentas, sempre visível)
  - controle segmentado com os modelos do quadro (400×38, raio 12, fundo `Fill`). A pílula branca desliza até a opção escolhida;
- **Ocultar:** a seção recolhe (altura → 0, opacidade → 0, sobe 14 px, escala 0.98) em 0.5s, e o quadro cresce para ocupar o espaço.
- **Mostrar:** o inverso.

### 5.4 Quadro

- **Moldura:** raio 32, padding 16, imitando madeira clara.
  - Gradiente 160°: `#EAD1A6` → `#D3AD75` (36%) → `#E3C597` (62%) → `#C0965C`.
  - Por cima, listras verticais finas de veio (branco 6% e marrom 5%, a cada ~19 px).
  - Sombra grande embaixo (`0 34 60 −24`, marrom a 50%) e brilho branco de 2 px na borda de cima.
- **Superfície:** raio 16, textura `cork.png` repetida, com sombra interna (`0 4 16`, marrom escuro a 45%).
- **Quadro infinito** (Fase 1 do `PROMPT-melhorias.md`):
  - coordenadas de mundo em `double` e uma **câmera** = ponto do mundo no centro da tela + zoom;
  - a moldura contorna a área visível; a textura se repete sem fim e acompanha a câmera. Ela escala com o zoom em "níveis" (dobra ou cai pela metade, como a grade do Freeform), para nunca virar chuvisco;
  - infinito igualmente para os 4 lados, inclusive com coordenadas negativas. Não existe `ScrollViewer` nem barra de rolagem: só a câmera controla a posição. Dá para criar e escrever itens em qualquer ponto;
  - **zoom de 10% a 400%**, suave e centrado no cursor: **rodinha** (com ou sem Ctrl) ou pinça do touchpad. Botões − e + e `Ctrl +` / `Ctrl −` partem do centro da tela (×1.25); `Ctrl 0` volta a 100%;
  - **mover a câmera:** **botão esquerdo pressionado no vazio + arrastar** (livre: horizontal, vertical e diagonal, seguindo o mouse 1:1), botão do meio arrastando, Espaço + arrastar, Shift + rodinha e rolagem horizontal do touchpad. Ao soltar, o quadro desliza um pouco (inércia suave) e para;
  - **cursor:** mão aberta no vazio, mão fechada enquanto arrasta;
  - **Shift + arrastar no vazio:** seleção em caixa (retângulo `Accent` a 12% com borda de 1 px), somando à seleção atual;
  - **"Ajustar"** (clicar na %) enquadra todos os itens, centralizados, com margem de 80 px (no máximo 100%);
  - ao redimensionar a janela, o ponto do centro continua no centro;
  - ao abrir, a câmera volta para onde estava; na primeira vez usa "Ajustar". Um quadro vazio abre centrado em (0, 0);
  - pílula de vidro **"Ir para o conteúdo"** (topo, centro) aparece quando nenhum item está na tela.
- **Desempenho (meta: 120 fps com 500 itens):**
  - renderização pela placa de vídeo (`RenderMode.Default`, sem forçar software; o contador de FPS mostra "GPU" quando `RenderCapability.Tier` é 2);
  - animações a 120 fps (`Timeline.DesiredFrameRate`); câmera, arrastar e pan aplicados uma vez por quadro em `CompositionTarget.Rendering`;
  - a câmera é **uma única `MatrixTransform`** no `RenderTransform` do `Canvas` dos itens. Nada de `LayoutTransform` e nada de mudar `Canvas.Left/Top` a cada quadro;
  - **arrastando um item**, ele anda só pelo `TranslateTransform` (`DragOffset`); `X`/`Y` são gravados uma vez, ao soltar;
  - durante movimento ou zoom não se recalcula sombra, efeito nem desfoque; o brilho do vidro que segue o mouse fica pausado e volta quando a câmera para;
  - **virtualização:** só existem na tela os itens perto da área visível (quadtree, margem de meia tela). Itens que saem são guardados e reaproveitados; itens novos são criados com limite de 3 ms por quadro;
  - itens parados usam `BitmapCache` na escala do zoom (refeito quando a câmera para);
  - o vidro sobre o quadro (dica, status, dock, bandeja) mostra a textura **já desfocada e saturada** (blur 26, saturação 190%, calculada uma vez ao abrir), alinhada com a câmera: por quadro só muda a posição de um pincel. Os itens não aparecem através desses painéis;
  - **contador de FPS:** `Ctrl+Shift+F` (fps, pior quadro, tempo de cálculo e itens criados/total).
- **Modelos do quadro** (troca pelo controle segmentado ou pelo menu do botão direito):

| Modelo | Moldura | Superfície |
|---|---|---|
| Cortiça | madeira clara (acima) | `cork.png` |
| Branco | `#F5F6F8 → #D2D6DD → #EEF0F3 → #BBC1CA` (alumínio) | `#F9FAFB` com reflexo diagonal suave |
| Lousa | madeira escura `#9A6C43 → #6B4628 → #8A623B → #54361D` | verde `#2C4838` com manchas de giz (branco 6–7%) |
| Pontilhado | `#F6F6F2 → #DCDCD4 → #EDEDE7 → #CFCFC6` | `#FBFBF8` com pontos `#C2C2B8` de raio 1.3 a cada 26 px |

- Na **Lousa**, o texto solto usa a cor do papel (efeito giz) em vez da tinta.
- **Por cima do quadro**, ficam três elementos flutuantes:
  - **Dica** (canto superior esquerdo, a 30 px): pílula de vidro de 36 de altura com ícone de mouse e "Botão direito para adicionar · 2 cliques para editar".
  - **Status** (canto inferior direito, a 30/32 px): pílula de vidro com ponto verde e "Salvo · 14 itens · Cortiça".
  - **Dock da bandeja** (centro, embaixo): escondido por padrão (ver seção 7).

---

## 6. Itens do quadro

Todo item tem posição (x, y), camada (z), rotação, cor e estilo de fixação. Os itens ficam girados levemente para parecer colados à mão.

**Arrastar (botão esquerdo):**
- Clicar e arrastar move qualquer item: post-its, páginas, textos e blocos de vidro (e, quando existirem, formas, conectores — a linha inteira ou cada ponta pela bolinha —, grupos e seleções com vários itens).
- Não precisa selecionar antes; o item vem para a frente. Clicar no texto de um item também arrasta; dois cliques continuam editando.
- Com vários itens selecionados, arrastar um deles move todos juntos. Shift + clique põe ou tira um item da seleção.
- **Item bloqueado** (menu do item → Bloquear) não se move: arrastar em cima dele move a câmera.
- Enquanto arrasta, a 40 px da borda da janela a câmera anda sozinha naquela direção (mais rápido quanto mais perto) e o item vai junto. A rodinha também dá zoom durante o arraste.

**Estados comuns a todos os itens:**
- **Mouse em cima:** a rotação cai para 60% da original, o item sobe 3 px e cresce para 1.015 (mola de item).
- **Arrastando:** cursor de mão fechada, escala 1.05, rotação a 30% da original, sem animação de atraso.
- **Selecionado:** contorno de 2.5 px na cor de destaque, afastado 7 px do item, cantos com raio 6.
- **Fora da busca:** opacidade 16%.
- **Editando:** o texto vira um campo editável com fundo branco a 60%, raio 6 e anel de 2 px na cor de destaque. O campo usa a mesma fonte e o mesmo tamanho do texto original.

### 6.1 Post-it (172 × 172)

- **Papel:** a cor do papel e, por cima, nesta ordem:
  1. `paper-grain.png` (grão);
  2. **faixa adesiva** nos 26 px de cima, levemente mais escura (preto 5%);
  3. uma linha de brilho branca a 40%, de 1 px, logo abaixo da faixa;
  4. um clarão branco de 14% que some até 46% da altura;
  5. um escurecimento de 4% no pé.
- **Cantos:** 2 em cima e 3 embaixo. Sombra de contato: `0 1 1` preto 10% + `0 2 4` marrom 10%.
- **Canto levantado:** um triângulo de 36×36 no canto inferior direito, com gradiente a 315°: transparente → sombra de 12% → brilho branco de 55% → transparente. Simula a ponta do papel dobrando.
- **Sombra dupla:** duas sombras borradas sob os cantos de baixo (48% da largura × 32% da altura cada), giradas ±4° e inclinadas ±6°, com deslocamento 16 e desfoque 12, marrom (`#321900`) a 42%. Com o mouse em cima, ficam com deslocamento 24 e desfoque 16, a 36%: o papel parece descolar.
- **Texto:** centralizado, Nunito 19/900 em MAIÚSCULAS. Padding 32 em cima, 16 nas laterais e 26 embaixo.
- **Rodapé** (a 8 px de baixo e 13 dos lados): "28 OUT" à esquerda e o número "#03" à direita.
- **Rotação:** aleatória entre −4° e 4°.
- **Fixação** (escolhida no menu):
  - **Alfinete** (padrão): bolinha de 20 px no topo central, a 5 px da borda.
    - Brilho branco no alto à esquerda, cor do alfinete no centro e borda mais escura (a cor misturada com 45% de preto).
    - Sombra `3 5 4` marrom a 35%.
    - Uma "sombra da agulha" de 2×9 px girada −38°.
  - **Fita:** fita adesiva de 86×27, girada −5°, a 13 px acima do papel.
    - Listras diagonais brancas a 34% (5 px).
    - Cor base: a cor do alfinete a 42%, misturada com branco 50%.
    - **Pontas em zigue-zague**, como fita rasgada.
  - **Solto:** sem nada.
- **Pautado** (variação do post-it):
  - linhas horizontais na cor da tinta a 28%, a cada 27 px, a partir de 34 px;
  - texto alinhado à esquerda: um título em MAIÚSCULAS na cor da tinta e uma lista de itens.

### 6.2 Página (236 de largura, 300 de altura mínima)

- **Papel:** 32% da cor escolhida misturado com `#FFFEFA`, mais o grão.
- **Margem vermelha:** linha vertical em x = 30 (`#E45050` a 50%).
- **Pautas azuis:** a cada 28 px, a partir de y = 61 (`#466EBE` a 22%).
- **3 furos de caderno** na lateral esquerda (x = 9, de y = 54 até 54 px do fim): círculos de 12 px na cor `#784E22` a 60%, com sombra interna e um fio de brilho branco embaixo.
- **Orelha dobrada** de 28 px no canto superior direito: o canto é cortado e um triângulo mais escuro faz a dobra.
- **Sombra:** `0 14 12` marrom a 32% + `0 1 1` preto 12%.
- **Padding:** 30 em cima, 18 à direita, 20 embaixo e 40 à esquerda (depois da margem).
- Fixação padrão: **fita**. Rotação entre −2° e 2°.

### 6.3 Bloco de vidro (290 de largura)

- Um painel de vidro colado no quadro, com raio 24.
- **Fundo:** 34% da cor escolhida misturada com branco 42%, desfoque 14 e saturação 170%.
- **Sombra:** `0 18 30 −12` marrom a 50% + `0 2 6` a 18%.
- Tem a mesma **borda de luz e brilho interno** da seção 4.
- Fixação padrão: **alfinete**.
- **Conteúdo:** padding 30/20/18.
  - Título com uma bolinha de 10 px na cor da tinta (anel branco a 50%) e, ao lado, o texto em Inter 16.5/700.
  - Corpo em Inter 14, com entrelinha 1.7.
- Rotação entre −1.2° e 1.2°.
- O texto é sempre escuro (`#1D2130`), nos dois temas.

### 6.4 Texto solto

- Só o texto, sem papel e sem rotação.
- Nunito 50/900, cor da tinta, com duas sombras: `0 1 0` branco 35% (relevo) e `0 3 4` marrom 30%.
- Edição em uma linha.

---

## 7. Bandeja

A bandeja é o "estojo" com todos os itens prontos para colar.

### 7.1 Dock (quase escondido até o mouse chegar perto)

- Fica quase todo abaixo da borda da cortiça, deixando **~14 px do próprio dock à mostra** no centro de baixo (deslocamento de 52).
- **Sobe com a mola de painel** quando o mouse chega a menos de **80 px da borda de baixo**, na faixa central.
- **Desce 0,8 s** depois que o mouse se afasta, a não ser que a bandeja esteja aberta, algo esteja sendo arrastado dela ou o mouse esteja em cima dela. Abrir a bandeja (pelo menu ou pelo botão direito) também faz o dock subir.
- Em tela cheia funciona igual. O botão direito no quadro continua abrindo a bandeja junto ao cursor.

Cápsula de vidro de 56 de altura, centralizada na parte de baixo do quadro. Contém:

1. Botão **"Bandeja"** com ícone de bandeja e uma seta que gira 180° quando abre.
2. Um separador vertical de 24 px.
3. Atalhos rápidos:
   - **post-it:** um mini post-it amarelo de 20×20 girado −5°, que adiciona um post-it da última cor usada;
   - **página;**
   - **texto;**
   - **bloco de vidro.**

Os atalhos rápidos adicionam o item no centro da área visível.

### 7.2 Painel da bandeja (abre acima do dock)

- Vidro denso (`Glass2`), largura até 620, raio 34, padding 10/20/20.
- **Animação:** surge de baixo para cima (opacidade 0 → 1, desce 18 px, escala 0.95 → 1) com a mola de painel.
- **Conteúdo, de cima para baixo:**
  - alça de 40×5 (cor `Fill2`), centralizada;
  - "Bandeja" (20/700) e "Arraste para o quadro ou clique para adicionar no centro", com um botão redondo ✕ de 32;
  - **Post-its:** os 8 mini post-its (56×56, girados entre −5° e 5°, com faixa adesiva e mini alfinete). Com o mouse em cima, ficam retos, sobem 5 px e crescem 10%;
  - **Estilos de post-it:** 3 cartões (62 de altura, raio 18, fundo `Fill`), cada um com um mini exemplo: "Com alfinete", "Com fita" e "Pautado";
  - **Elementos:** 3 cartões (Página, Texto, Bloco de vidro), com o ícone na cor de destaque.
- **Para usar um item:**
  - **arrastar** até o quadro e soltar: o item nasce centralizado onde foi solto;
  - **clicar:** o item nasce no centro da área visível, com um pequeno desvio aleatório.
- **Abrir e fechar** a bandeja:
  - pelo botão do dock;
  - pelo **menu do botão direito** ("Abrir bandeja" / "Fechar bandeja").

---

## 8. Menu do botão direito

Menu próprio (não o menu padrão do Windows), no estilo dos menus de toque longo do iPhone.

- Vidro denso, largura 266, raio 24, padding 6.
- Abre exatamente onde o mouse clicou, com o pop do menu, e nunca sai da tela.
- **Grupos** separados por uma faixa de 7 px na cor `Sep`.
- **Linhas:** 42 de altura, raio 14, texto à esquerda e ícone à direita (`Ink2`), hover `Fill2`.
- **Bolinhas de cor:** 24 px, com borda fina. A cor atual tem anel duplo (fundo + destaque) e as bolinhas crescem 18% no hover.
- **Chips:** 30 de altura, totalmente redondos, fundo `Fill`. O selecionado tem fundo `Accent` e texto branco.
- O menu fecha ao clicar fora, ao escolher uma opção ou com Esc.

### 8.1 Clique direito no quadro vazio

1. **"Novo post-it · escolha o estilo e a cor"**
   - Chips de estilo: Alfinete · Fita · Solto · Pautado. O escolhido fica marcado e vale para os próximos post-its.
   - 8 bolinhas de cor. Clicar numa cria o post-it naquele ponto, com o estilo marcado.
2. **Página** · **Texto** · **Bloco de vidro**: criam o item no ponto clicado.
3. **Abrir bandeja / Fechar bandeja.**
4. **Modelo do quadro:** 4 miniaturas (Cortiça, Branco, Lousa, Pontilhado). A atual fica destacada.
5. **Ocultar título / Mostrar título.**

### 8.2 Clique direito num item

1. **Cor:** as 8 bolinhas, com a cor atual marcada.
2. **Estilo** (só post-its): Alfinete · Fita · Solto · Pautado.
3. Editar texto · Duplicar · Bloquear/Desbloquear · Trazer para frente · Enviar para trás.
4. **Excluir** (em vermelho, cor `Danger`).

---

## 9. Interações

| Ação | Resultado |
|---|---|
| Arrastar um item | move o item (ou todos os selecionados); ele vai para a frente e fica selecionado |
| Arrastar no vazio (botão esquerdo) | move a câmera, com inércia ao soltar |
| Shift + arrastar no vazio | seleção em caixa |
| Shift + clique num item | põe ou tira da seleção |
| Ctrl+A | seleciona tudo |
| Clicar no vazio | tira a seleção e fecha o menu |
| 2 cliques num item | edita o texto clicado (título ou corpo) |
| 2 cliques no vazio | cria um post-it da última cor usada e já abre a edição |
| Enter (post-it, texto, títulos) | termina a edição; Shift+Enter quebra a linha |
| Esc | termina a edição, fecha o menu e tira a seleção |
| Delete / Backspace | exclui os itens selecionados |
| Rodinha, Ctrl + rodinha ou pinça | zoom centrado no cursor (10% a 400%) |
| Shift + rodinha / rolagem horizontal do touchpad | move o quadro na horizontal |
| Botão do meio ou Espaço + arrastar | move o quadro (com inércia ao soltar) |
| Ctrl + / Ctrl − / Ctrl 0 | zoom a partir do centro / 100% |
| Ctrl+1 ou "…" → Ajustar o quadro à tela | "Ajustar": enquadra todos os itens |
| Ctrl+Shift+F | liga e desliga o contador de FPS |
| Ctrl+K ou Ctrl+O | Procurar: quadros, páginas e textos (itens do quadro aberto e conteúdo dos outros) |
| Alt+← / Alt+→ | voltar / avançar entre quadros e páginas abertos |
| Clicar num item da lista "No quadro" | seleciona e leva a câmera até ele, com animação |

- Itens novos de post-it e de texto já nascem em modo de edição.
- Duplicar cria a cópia 30 px abaixo e à direita.
- O quadro é infinito: itens podem ficar em qualquer posição.

---

## 10. Dados e salvamento

- **Cofre** (como o Obsidian): a pasta **`Documentos\Cofre`**, criada automaticamente, guarda **todas** as informações do app:
  - `Quadros\` — um arquivo `.quadtab` (JSON) por quadro, com subpastas;
  - `Páginas\` — cada página é um arquivo `.md` de verdade (abre em qualquer editor);
  - `Anexos\` — imagens, vídeos, arquivos, prévias de PDF e de links (sempre copiados para cá);
  - `.lixeira\` — quadros apagados (somem de vez depois de 30 dias);
  - `.quadtab\` — `estado.json` (quadro aberto, recentes, fixados, importantes, seções abertas, tela cheia, tema, faíscas, última cor/estilo de post-it e o molde do último item criado), miniaturas da galeria e `erros.log`.
- Migração: a pasta antiga `Documentos\Quadtab` vira `Documentos\Cofre` (se o Cofre ainda não existir); o antigo `%AppData%\Quadtab\quadro.json` também é importado.
- Salvamento automático ~1 s depois da última mudança (gravação atômica). O status mostra "Salvando…" e depois "Salvo".

```json
{
  "versao": 3,
  "titulo": "Meu quadro",
  "modelo": "cortica",
  "camera": { "x": 763, "y": 435, "zoom": 0.76 },
  "tituloOculto": false,
  "itens": [
    { "id": 1, "tipo": "postit", "cor": "amarelo", "x": 90, "y": 70, "z": 1, "rotacao": -4,
      "fixacao": "alfinete", "pautado": false, "titulo": "", "texto": "Crie sua vida" }
  ]
}
```

- `tipo`: `postit` | `pagina` | `bloco` | `texto` | `caixa` | `forma` | `linha` | `desenho` | `imagem` | `video` | `arquivo` | `link` | `tabela`
- Também: `largura`/`altura` (alças), `grupo`, `descricao`, `estilo` (preenchimento, borda, espessura, opacidade, fonte, tamanho, corTexto, alinhamento, negrito, italico, lista), `forma`, `linha` (de/para com item e lado, ponta, tipo reta/curva/angulo), `tracos`, `arquivo` (relativo ao cofre), `url`/`site`/`imagem`, `recorte`/`raio`, `tabela`; no quadro: `grade`, `diagrama`, `cenas`.
- `fixacao`: `alfinete` | `fita` | `nenhuma`
- `modelo`: `cortica` | `branco` | `lousa` | `pontilhado`
- `camera`: centro da tela em coordenadas do mundo + zoom (ausente = nunca aberto, usa "Ajustar"). O `zoom` solto da versão 1 é ignorado.
- `criado`: data em que o item foi colado (rodapé do post-it).
- `bloqueado`: `true` = o item não se move.

### Conteúdo inicial (primeira vez que o app abre)

| # | Tipo | Cor | x, y | Rot. | Fixação | Conteúdo |
|---|---|---|---|---|---|---|
| 1 | post-it | amarelo | 90, 70 | −4 | alfinete | Crie sua vida |
| 2 | página | branco | 330, 50 | 1.5 | fita | **Metas para 2027** — ○ Reserva de 6 meses / ○ Viajar para o Chile / ○ Ler 24 livros / ○ Aprender a nadar / ○ Correr 10 km |
| 3 | post-it | rosa | 640, 104 | 3 | fita | Aventure-se |
| 4 | bloco | azul | 890, 62 | −1 | alfinete | **Lembrete** — Pequenos passos todos os dias. Revise este quadro aos domingos e mova o que já foi feito para Conquistas. |
| 5 | post-it | verde | 1250, 90 | −2 | alfinete | Casa nova |
| 6 | post-it | azul | 110, 330 | 2 | alfinete | Liberdade financeira |
| 7 | texto | branco | 640, 392 | 0 | — | GRATIDÃO |
| 8 | post-it | roxo | 380, 450 | −3 | fita | Formatura |
| 9 | post-it pautado | laranja | 985, 330 | 4 | alfinete | **Esta semana** — Comprar fita / Revisar metas / Ligar para a vó |
| 10 | página | menta | 1200, 320 | −1.5 | fita | **Lista de leitura** — 1. O Pequeno Príncipe / 2. Sapiens / 3. Hábitos Atômicos / 4. Dom Casmurro |
| 11 | post-it | menta | 120, 620 | −2 | alfinete | Mais tempo ao ar livre |
| 12 | bloco | rosa | 600, 540 | 1 | alfinete | **Ideia** — Separar uma área do quadro só para viagens, com fotos e o orçamento de cada uma. |
| 13 | post-it | branco | 950, 630 | 3 | fita | Respirar fundo |
| 14 | texto | verde | 1160, 760 | 0 | — | Conquistas |

---

## 11. Dicas de implementação no WPF

- **Quadro:** sem `ScrollViewer`. Um `Canvas` com `MatrixTransform` (a câmera: escala = zoom, translação = centro). Os itens ficam no `Canvas` em coordenadas do mundo (`Canvas.Left`, `Canvas.Top`, `Panel.ZIndex`); mover ou dar zoom só troca a matriz (`Controls/BoardView`, `Rendering/BoardCamera`, `Rendering/QuadTree`).
- **Texturas:**
  - cortiça: `ImageBrush` com `TileMode="Tile"`, `ViewportUnits="Absolute"` e `Viewport="0,0,384,384"`;
  - grão: o mesmo esquema com 256×256, por cima da cor do papel.
- **Rotação dos itens:** `RenderTransform` com `RotateTransform`, centro no meio do item (`RenderTransformOrigin="0.5,0.5"`).
- **Sombras:**
  - `DropShadowEffect` funciona, mas pesa com muitos itens. Prefira um PNG de sombra suave (9-slice) atrás de cada item ou `BitmapCache`;
  - as "sombras duplas" dos post-its podem ser dois `Border` borrados atrás do papel, girados ±4° e inclinados com `SkewTransform` ±6°.
- **Arrastar:** `MouseLeftButtonDown` com `CaptureMouse()`; o ponteiro é guardado e aplicado no próximo quadro como `TranslateTransform` (em coordenadas do mundo). A posição é gravada ao soltar.
- **Menu:** camada dentro da própria janela (não `Popup`, que com transparência é desenhado sem a placa de vídeo). Não use o `ContextMenu` padrão.
- **Bandeja:** `Border` animado com `TranslateTransform`, `ScaleTransform` e `Opacity`. Para arrastar da bandeja para o quadro, use `DragDrop.DoDragDrop` com um `DataObject` (tipo, cor e estilo) e trate o `Drop` no quadro.
- **Edição:** nos dois cliques, troque o `TextBlock` por um `TextBox` com o mesmo estilo e dê foco com o texto selecionado.
- **Controle segmentado:** `Grid` com 4 colunas e um `Border` indicador animado por `TranslateTransform.X`.
- **Temas:** dois `ResourceDictionary` (claro e escuro) com os tokens da seção 3. Troque o dicionário conforme o tema do Windows.

## 12. Fora do escopo por enquanto

> O `PROMPT-melhorias.md` foi implementado inteiro (fases 1 a 8 + cofre). Fora do escopo agora:

- Abas múltiplas.
- Sincronização na nuvem e colaboração.
- Botões de tema e de título na barra lateral (foram removidos de propósito).

## 13. Recursos das fases 2–8 (regras atuais)

### 13.1 Adicionar (Fase 2)
- **Botão direito no vazio** abre a **bandeja junto ao cursor** (vidro denso, mola de pop). Um segundo clique direito fecha. Tudo nasce **no ponto clicado**.
- **2 cliques com o botão direito** no quadro (no vazio ou em cima de um item): fecha o que o 1º clique abriu e coloca, centrado no ponto, **outro item igual ao último criado** (mesmo tipo, cor, estilo, forma, tamanho; página cria uma página nova). Sem nada criado ainda: post-it da última cor e estilo. O molde (`ultimoItem`), a última cor (`corPostit`) e o último estilo de post-it (`estiloPostit`) ficam salvos em `estado.json`. Traços de desenho e conectores não viram molde.
- Conteúdo: estilo + 8 cores de post-it; elementos Página, Texto, Bloco, Caixa de texto, **Formas** (biblioteca com busca e categorias Básicas/Setas/Símbolos/Objetos/Natureza), **Linha**, **Seta**, **Desenhar**, **Tabela**, **Imagem**, **Arquivo**, **Link**, **Colar**; pílulas abrir/fechar bandeja e ocultar título.
- Botão direito num item: menu do item (cor, estilo, editar, duplicar, bloquear, camadas, excluir) + **Mais opções** (topo/fundo, copiar/colar estilo, descrição, agrupar, alinhar, distribuir, desagrupar).
- Barra de ferramentas: título · desfazer/refazer · **"…"** (procurar, ajustar à tela, buscar no quadro, renomear, grade, modo diagrama, inspetor, título, cenas, apresentar, tela cheia, exportar, pasta do cofre) · janela.

### 13.2 Quadros, pastas e galeria (Fase 3)
- Quadros ilimitados, em pastas (arrastar um quadro sobre uma pasta move). Renomear: dois cliques ou F2 (na lateral) e dois cliques no título grande.
- Galeria (Todos os quadros, Fixados, Importantes, Apagados recentemente): grade com miniatura, ordenar por data ou nome; restaurar ou apagar de vez (30 dias).
- **Procurar** (Ctrl+K): caixa de vidro no centro que busca em quadros, páginas e textos.

### 13.3 Lateral e tela cheia (Fase 4)
- Os itens de cada quadro aparecem recuados sob o nome dele na lateral (ver 5.1); páginas mostram seus títulos (#).
- **F11 / "…" → Tela cheia:** tela cheia no monitor inteiro. Lateral e barra somem; aparecem ao encostar o mouse na borda esquerda/de cima e somem 1 s depois de sair.

### 13.4 Edição (Fase 5)
- Item selecionado: **8 alças + girar** (Shift = proporção / 15°). Texto solto: as alças mudam o tamanho da letra.
- **Guias amarelas** (`#FFD60A`) ao arrastar: bordas e centros encaixam a 6 px (Ctrl desliga).
- Alt + arrastar = arrasta uma cópia. Ctrl+C/X/V/D, Ctrl+G / Ctrl+Shift+G (grupo), Ctrl+L (bloquear), Ctrl+] / Ctrl+[ (camada; com Shift: topo/fundo), setas empurram 1 px (Shift: 10).
- **Desfazer ilimitado:** Ctrl+Z, Ctrl+Shift+Z ou Ctrl+Y.
- **Inspetor** (painel à direita, aparece com seleção): cor, fixação, preenchimento, borda, espessura, opacidade, texto (fonte, tamanho, negrito, itálico, lista, alinhamento, cor), linha (reta/curva/ângulo, pontas), traço, imagem (cantos e recorte), rotação, camadas, alinhar/distribuir, agrupar, copiar/colar estilo, descrição (vira dica ao passar o mouse).

### 13.5 Elementos (Fase 6)
- Caixa de texto, formas, linhas e setas, **conectores** (como no Canvas do Obsidian): passar o mouse em **qualquer item** (inclusive bloqueados, imagens, links, tabelas, desenhos) mostra 4 pontos azuis; arrastar de um ponto até outro item liga os dois (com seta no fim); soltar no vazio cria uma **cópia ligada**. As pontas seguem os itens. As pontas de uma linha selecionada viram bolinhas arrastáveis (soltar sobre um item prende).
- **Direção e rótulo das linhas:** botão direito numa linha → **Direção** (Sem seta · → Uma · ↔ Duas · ⇄ Inverter) e traçado (Reta · Curva · Ângulo). **2 cliques na linha** escrevem um **rótulo** no meio dela (pílula branca 92%, Inter 13/600, `#1D2130`; vazio = some).
- **Modo diagrama** (menu "…"): desligado, os conectores somem. Criar um conector liga o modo de novo.
- **Desenho à mão:** caneta, lápis, marcador, borracha e laço, numa paleta de vidro flutuante (cores e 3 espessuras; "Pronto" volta à seleção).
- **Imagens e vídeos:** arrastar do Windows ou Ctrl+V (inclusive print). Recorte e cantos pelo inspetor. Vídeo toca no próprio quadro.
- **Arquivos:** cartão com tipo e nome; PDF mostra a 1ª página (Windows.Data.Pdf). Dois cliques abrem no programa padrão.
- **Links:** cartão com título, site e imagem (og:title/og:image, baixados uma vez para Anexos). Dois cliques abrem no navegador.
- **Tabelas:** dois cliques numa célula editam; Tab vai para a próxima.

### 13.6 Páginas (Fase 7)
- Páginas são `.md` no cofre. Dois cliques numa página do quadro abrem o **editor ao lado** (dividido ou expandido).
- Modo **Ler**: Markdown desenhado (títulos, listas, caixas de seleção clicáveis, citações, código, negrito/itálico, links). Modo **Editar**: texto puro; dois cliques no modo Ler editam; Esc volta.
- `[[Página]]` com **autocompletar** (↑↓ Enter); Ctrl+clique no Editar segue o link; link para página inexistente fica apagado e cria a página ao clicar. `[[Quadro: Nome]]` abre um quadro. `#tags` destacadas. Rodapé **"Mencionado em"** (backlinks).
- Renomear uma página atualiza os `[[links]]` das outras.
- **Ctrl+K / Ctrl+O:** Procurar (quadros, páginas e textos; sem resultado, Enter cria a página).

### 13.7 Navegação e saída (Fase 8)
- **Cenas:** salvar a vista atual; ir até uma cena com câmera animada. **Apresentar:** tela cheia, setas ← → (ou Espaço), Esc sai.
- **Ctrl+F:** busca no quadro — destaca os resultados, apaga o resto, Enter/Shift+Enter voam até o próximo/anterior.
- **Grade** opcional (menu "…").
- **Exportar** o quadro ou a seleção como PNG (2×) ou PDF.

### 13.8 Ponteiro, faíscas e balões de ajuda
- **Ponteiro próprio** em todo o app (seta da imagem de referência: preenchimento branco, contorno preto arredondado e sombra suave). Mãos aberta/fechada, texto e redimensionar no mesmo estilo. Desenhados no tamanho exato da escala da tela (nítidos em 100%, 125%, 150% e 200%) e refeitos ao trocar de monitor (`Rendering/BoardCursors`).
- **Faíscas a cada clique** (qualquer botão, qualquer lugar): 8 a 12 tracinhos saem da ponta do ponteiro em todas as direções, encolhem e somem em ~400 ms; cores = variações claras do `Accent`. Camada transparente por cima de tudo, que não recebe cliques; no máximo 3 explosões ao mesmo tempo, com partículas reaproveitadas (`Controls/SparkLayer`). Desligadas se "efeitos de animação" estiver desligado no Windows ou em Ajustes.
- **Balão de ajuda:** com o mouse parado **3 s** em cima de qualquer botão ou ícone clicável, aparece uma frase simples dizendo o que ele faz (ex.: "Cria um quadro novo · Ctrl+N"; o atalho fica apagado, em `Ink3`). Vidro denso, Inter 13, ao lado do botão (abaixo, na barra de cima), sempre dentro da janela, com fade e leve mola. Some na hora ao sair do botão ou clicar. O texto vem do `ToolTip` de cada botão (o balão padrão do Windows não aparece); toda linha de menu tem o seu (`ActionItem.Tip` ou `ActionTips`). As descrições dos itens do quadro usam o mesmo balão, em 0,8 s (`Controls/HelpTips`).
