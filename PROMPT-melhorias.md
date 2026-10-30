# Prompt para o Claude Code — Melhorias do Quadtab

Leia primeiro o `CLAUDE.md` desta pasta e siga tudo o que está nele, principalmente:
- a seção 1, sobre como trabalhar comigo;
- todos os tokens de design (cores, fontes, vidro, post-its, animações).

O visual atual (vidro estilo iPhone, quadro de cortiça, post-its detalhados) **continua igual**. As mudanças abaixo ampliam o app sem trocar a identidade dele.

**Objetivo:** o Quadtab precisa ser um app **totalmente usável no dia a dia**, não um protótipo.
- O **design, a aparência e a interatividade** devem seguir os conceitos do **Apple Freeform**.
- O jeito de fazer **anotações e páginas** deve ser parecido com o **Obsidian**.

Ao terminar cada fase, **atualize o `CLAUDE.md`** com o que mudou. A seção "Fora do escopo" deixa de valer para os itens desta lista.

---

## 1. Rodar a 120 fps

O app deve ficar fluido a **120 quadros por segundo** ao arrastar, dar zoom, mover o quadro e animar. Isso vale para monitores de 120 Hz ou mais; em monitores de 60 Hz, o limite é 60.

- O WPF limita as animações a cerca de 60 fps quando nada é configurado. Ajuste a taxa padrão das animações para 120 no início do app (`Timeline.DesiredFrameRateProperty.OverrideMetadata`).
- Movimentos manuais (arrastar, pan, zoom) devem ser atualizados no `CompositionTarget.Rendering`, uma vez por quadro, nunca mais de uma vez.
- Meta: cada quadro em **até 8 ms**, com **500 itens** no quadro.
  - **Virtualização:** só criar e desenhar os itens que estão visíveis na tela, com uma margem, usando um índice espacial (quadtree).
  - Sem `DropShadowEffect` por item: usar sombras pré-desenhadas (PNG 9-slice) e `BitmapCache` nos itens parados.
  - O desfoque do vidro sobre o quadro **não pode ser recalculado a cada quadro** durante pan e zoom. Use uma versão em baixa resolução e atualize quando o movimento parar.
- Crie um **contador de FPS** que liga e desliga com `Ctrl+Shift+F`, para medir.
- Se, mesmo assim, o WPF não chegar na meta, **me pergunte antes** de trocar a camada do quadro por outra tecnologia (por exemplo, SkiaSharp com GPU).

## 2. Quadro infinito

O quadro não tem mais tamanho fixo (hoje é 1500×900). Ele é **infinito** para todos os lados, como no Freeform.

- **Coordenadas de mundo** em `double`, com uma câmera (posição + zoom).
- A textura do modelo (cortiça, branco, lousa, pontilhado) **se repete infinitamente** e acompanha o movimento da câmera.
- A moldura de madeira passa a contornar a janela do quadro (a área visível), não o conteúdo.
- **Mover o quadro:**
  - rodinha do mouse (vertical) e Shift + rodinha (horizontal);
  - arrastar com o botão do meio;
  - segurar Espaço + arrastar;
  - dois dedos no touchpad.
  - O movimento tem leve inércia ao soltar.
- **Zoom:**
  - de 10% a 400%;
  - `Ctrl` + rodinha ou pinça no touchpad, com o zoom **centrado no cursor**;
  - animação suave (sem saltos);
  - atalhos `Ctrl +`, `Ctrl −` e `Ctrl 0` (100%).
- Botão **"Ir para o conteúdo"** quando a câmera estiver longe de todos os itens.

## 3. Conteúdo centralizado ao diminuir o zoom

- Os botões de zoom (− e +) e os atalhos ampliam e reduzem **a partir do centro da tela**. O que está no meio continua no meio.
- **"Ajustar"** enquadra todos os itens do quadro, **centralizados**, com margem de 80 px.
- Ao **redimensionar, minimizar e restaurar a janela, ou entrar e sair da tela cheia**, o ponto do quadro que estava no centro continua no centro.
- Um quadro novo abre com a câmera centralizada no ponto (0, 0).
- Ao abrir um quadro existente, a câmera volta para onde estava da última vez. Se for a primeira vez, usa "Ajustar".

## 4. Quadros ilimitados

Hoje existe só "Meu quadro". O usuário deve poder criar **quantos quadros quiser**.

- **Seção "Quadros" da barra lateral:**
  - lista de quadros com **pastas** (como no Freeform);
  - criar quadro (`Ctrl+N`) e criar pasta;
  - renomear (dois cliques ou `F2`), duplicar e mover entre pastas arrastando;
  - favoritar ⭐.
- **Grupos fixos no topo:** Todos os quadros · Recentes · Favoritos · **Apagados recentemente**. Um quadro apagado fica 30 dias na lixeira e pode ser recuperado.
- **Galeria "Todos os quadros":** grade de cartões de vidro com miniatura, nome e data de edição, ordenável por nome ou data.
- **Busca da lateral:** procura em todos os quadros (nomes e textos) e no quadro atual.
- **Ícone de cada quadro:** o mini quadro de cortiça do ícone do app, na cor do modelo do quadro.
- O título grande e o título inline mostram o nome do quadro aberto. Dois cliques no título grande o renomeiam.

## 5. "No quadro" como lista alternante (estilo Notion)

A seção "No quadro" vira uma **lista em árvore que abre e fecha**, como os toggles do Notion.

- **Cabeçalho "No quadro":** setinha ▸ à esquerda. Clicar gira a seta 90° e abre ou fecha a seção, com animação de altura em mola (0.35s).
- **Dentro, grupos por tipo,** cada um com sua própria setinha e contador:
  - Post-its (12)
  - Páginas (3)
  - Blocos de vidro
  - Textos
  - Formas e linhas
  - Desenhos
  - Imagens e arquivos
- **Páginas** podem ser abertas na própria lista (setinha) e mostram os títulos internos (`#`, `##`) como sub-itens.
- **Clicar num item:** seleciona o item e leva a câmera até ele, com animação.
- **Arrastar um item da lista:** muda a ordem de camada.
- O estado aberto ou fechado de cada grupo fica salvo.
- O mesmo estilo de toggle vale para as **pastas de quadros** na seção "Quadros".

## 6. Tela cheia

- `F11`, ou um botão de tela cheia na barra de ferramentas, alterna a tela cheia.
- **Em tela cheia:**
  - a janela ocupa a tela toda, sem bordas;
  - a barra lateral e a barra de ferramentas se **escondem sozinhas**. Elas reaparecem deslizando quando o mouse encosta na borda esquerda ou de cima, e somem de novo 1 s depois que o mouse sai;
  - o dock da bandeja e o status continuam flutuando.
- `Esc` ou `F11` saem da tela cheia.

## 7. Botão direito abre e fecha a bandeja

**Muda o comportamento do botão direito no quadro vazio.**

- **Primeiro clique direito no vazio:** abre a **bandeja** como um painel de vidro **junto ao cursor**, com a mesma animação de mola, crescendo a partir do ponto clicado.
- **Segundo clique direito** (em qualquer lugar): **fecha** a bandeja. `Esc` e clique fora também fecham.
- Os itens escolhidos na bandeja (clicar ou arrastar) nascem **no ponto onde foi o clique direito**.
- **Na bandeja que abre pelo botão direito**, ficam, nesta ordem:
  1. post-its: as 8 cores e os estilos (alfinete, fita, solto, pautado);
  2. página, texto e bloco de vidro;
  3. as ferramentas novas do Freeform (formas, linha, caneta, imagem, arquivo, link, tabela).
- **Clique direito num item** continua abrindo o menu do item (cor, estilo, editar, duplicar, camadas, excluir), mais as opções novas: agrupar, bloquear, alinhar, adicionar descrição e copiar estilo.
- As opções do quadro que estavam no menu do vazio (modelo do quadro, mostrar ou ocultar título) passam para um botão **"…" na barra de ferramentas**.

## 8. Conceitos do Apple Freeform (design e interatividade)

Tudo com a aparência do Quadtab: vidro, cantos arredondados, molas e ícones de traço.

**Novos tipos de item:**
- **Caixa de texto:** livre, com fonte, tamanho, cor, alinhamento, negrito, itálico e listas.
- **Formas:** biblioteca com busca e categorias (básicas, setas, símbolos, objetos, natureza…). Cada forma tem preenchimento, borda, opacidade e texto dentro.
- **Linhas, setas e conectores:**
  - pontos de conexão (bolinhas azuis em cima, embaixo e dos lados) aparecem ao passar o mouse;
  - arrastar de um ponto até outro item **liga os dois**, e a linha acompanha quando os itens se movem;
  - tipos de linha: reta, curva e em ângulo;
  - pontas de seta configuráveis;
  - **segurar e arrastar a seta de um item** duplica o item já ligado.
- **Modo diagrama:** um botão liga e desliga a exibição dos conectores.
- **Desenho à mão:** caneta, lápis, marcador, borracha e laço. A espessura e a cor ficam numa paleta de vidro que flutua. Os traços são suavizados.
- **Imagens e vídeos:** arrastar arquivos do Windows ou colar com `Ctrl+V`. Imagens podem ser recortadas e ter cantos arredondados.
- **Arquivos** (PDF e outros): cartão com ícone, nome e prévia da 1ª página do PDF. Dois cliques abrem o arquivo.
- **Links:** colar uma URL cria um cartão com título, site e imagem de prévia.
- **Tabelas:** linhas e colunas editáveis, com redimensionamento.

**Edição:**
- **Seleção:** clique, Shift+clique e **retângulo de seleção** arrastando no vazio. `Ctrl+A` seleciona tudo.
- **Alças de seleção:** 8 alças para redimensionar e uma alça de **girar** acima do item. Shift mantém a proporção e gira em passos de 15°.
- **Guias inteligentes:** linhas de alinhamento amarelas no estilo Apple (bordas e centros) e **encaixe** ao mover.
- **Alinhar e distribuir** vários itens.
- **Agrupar e desagrupar** (`Ctrl+G` / `Ctrl+Shift+G`) e **bloquear** (o item não se move).
- **Camadas:** trazer para frente, enviar para trás, para o topo e para o fundo.
- **Área de transferência:** copiar, recortar, colar e duplicar (`Ctrl+D`, ou Alt + arrastar). Também **copiar estilo e colar estilo**.
- **Desfazer e refazer** sem limite em cada quadro (`Ctrl+Z` / `Ctrl+Shift+Z`).
- **Inspetor de estilo:** painel de vidro que aparece à direita quando algo está selecionado, com cor, borda, opacidade, fonte e tamanho do item.
- **Descrição do item:** texto alternativo, que aparece ao passar o mouse.

**Navegação e apresentação:**
- **Cenas:** salvar vistas da câmera com nome, navegar entre elas e **apresentar** em tela cheia, passando com as setas do teclado e com animação de câmera entre as cenas.
- **Buscar no quadro** (`Ctrl+F`): destaca os resultados e leva a câmera até eles.

**Aparência do quadro:**
- Os 4 modelos atuais e mais uma opção de **mostrar grade**.

**Exportar:**
- O quadro inteiro, ou só a seleção, como **PDF** ou **PNG**.

**Fora do escopo:**
- Colaboração online (o app é local).

## 9. Páginas e anotações (estilo Obsidian)

Do Obsidian, vem **só o jeito de anotar**: páginas, links e organização das notas.

- **Cada página é um arquivo Markdown (`.md`) de verdade**, salvo na pasta do cofre. Pode ser aberta em outros programas.
- **Abrir a página:** dois cliques numa página do quadro, ou um clique na lista lateral.
  - A página abre num **editor ao lado do quadro** (painel de vidro que divide a tela), ou ocupando a área toda (botão de expandir).
  - No quadro, a página continua aparecendo como folha de caderno, com o começo do texto.
- **Editor com pré-visualização ao vivo**, como no Obsidian: o Markdown vira formatação enquanto se digita. Suporta:
  - títulos (`#`, `##`, `###`), negrito, itálico, riscado e destaque;
  - listas e **caixas de seleção** (`- [ ]`) que se marcam com clique;
  - citações, blocos de código, linhas divisórias e tabelas;
  - imagens coladas.
- **Links entre páginas:**
  - `[[Nome da página]]`, com sugestões automáticas ao digitar `[[`;
  - `Ctrl+clique` no link abre a página;
  - links para **quadros** também funcionam (`[[Quadro: Viagens]]`);
  - se a página não existir, o link aparece apagado, e clicar cria a página.
- **Painel de menções** (backlinks), no fim da página: mostra quais páginas e quadros citam esta página.
- **Tags** com `#tag`. Clicar numa tag busca todas as páginas com ela.
- **Abrir rápido** (`Ctrl+O`): caixa de vidro no centro da tela que busca páginas e quadros pelo nome.
- As páginas aparecem também na lateral, numa seção **"Páginas"** com pastas e toggles (mesmo estilo da seção 5).

## 10. Salvamento e organização dos arquivos

**Pasta do cofre:** `Documentos\Quadtab\`. Pode ser trocada nos Ajustes.

```
Quadtab/
  Quadros/             um arquivo .quadtab (JSON) por quadro, em subpastas iguais às pastas do app
  Páginas/             arquivos .md
  Anexos/              imagens, PDFs e outros arquivos colados
  .lixeira/            itens apagados (30 dias)
  .quadtab/estado.json estado da interface (quadro aberto, câmera, grupos abertos/fechados, tela cheia)
```

- **Salvamento automático** 1 s depois da última mudança. Primeiro grava um arquivo temporário e depois substitui o original, para nunca corromper o arquivo.
- O status mostra **"Salvando…"** e depois **"Salvo"**.

## 11. Ordem de construção

Faça em fases. **Cada fase deve terminar com o app funcionando e usável.** Ao fim de cada uma, me mostre o que mudou.

1. Quadro infinito, câmera, zoom centralizado e virtualização, com o contador de FPS (itens 1, 2 e 3).
2. Bandeja no botão direito e a nova barra com "…" (item 7).
3. Quadros ilimitados, pastas, lixeira e galeria (item 4).
4. "No quadro" alternante e tela cheia (itens 5 e 6).
5. Seleção múltipla, alças, guias, desfazer/refazer e inspetor (item 8, edição).
6. Formas, linhas e conectores, desenho, imagens, arquivos, links e tabelas (item 8, itens novos).
7. Páginas em Markdown, links, menções e "Abrir rápido" (item 9).
8. Cenas, busca no quadro e exportar (item 8, navegação e exportação).

**Regra de design para tudo o que for novo:** use só os tokens do `CLAUDE.md`.
- Painéis flutuantes são de vidro, com a borda de luz que segue o mouse.
- Botões afundam com mola ao clicar.
- Ícones de traço de 1.9.
- Textos da interface em Inter; conteúdo do quadro em Nunito.
- Nenhum fundo amarelo ou dourado na interface.
