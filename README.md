# Quadtab

Quadro visual para Windows: um espaço infinito onde você cola notas, post-its, textos, formas e imagens, arrasta tudo livremente e liga as ideias com linhas, como num mapa mental.

Tudo fica em arquivos comuns no seu computador, na pasta `Documentos\Cofre`. As notas são arquivos `.md`, que abrem em qualquer editor.

## Baixar

Baixe o `Quadtab.exe` na [página de versões](https://github.com/Vitorgabrielqzs/quadtab/releases/latest) e abra. Não precisa instalar nem ter o .NET no computador.

Funciona no Windows 10 (versão 1809 ou mais nova) e no Windows 11, em 64 bits.

## O que ele faz

- **Quadro infinito:** zoom de 10% a 400% e movimento livre em todas as direções, sem barras de rolagem.
- **Modelos de quadro:** Liso, Cortiça, Branco, Lousa e Pontilhado, cada um com sua moldura.
- **Elementos:** notas, post-its, blocos, textos, caixas de texto, formas, tabelas, desenho à mão, imagens, vídeos, arquivos e links.
- **Linhas e setas:** ligam quaisquer itens, inclusive outras linhas, com traçado reta, curva ou ângulo, e podem ser cor sólida ou barbante.
- **Notas em Markdown:** edição direto no quadro ou em um editor ao lado, com links `[[entre notas]]`, `#tags` e menções.
- **Vários quadros:** pastas, favoritos, galeria com miniaturas e lixeira que guarda os apagados por 30 dias.
- **Busca:** por quadros, notas e textos.
- **Apresentação e exportação:** cenas com câmera animada, modo apresentação e exportação em PNG ou PDF.
- **Visual Liquid Glass:** painéis de vidro com brilho que segue o mouse, tema claro e escuro.

## Atalhos principais

| Atalho | Ação |
|---|---|
| 2 cliques no vazio | Cria outro item igual ao último |
| 2 cliques em um item | Edita o texto |
| Botão direito | Abre a bandeja ou o menu do item |
| Arrastar no vazio | Move o quadro |
| Shift + arrastar no vazio | Seleciona em caixa |
| Rodinha do mouse | Zoom |
| Ctrl+N | Novo quadro |
| Ctrl+K | Procurar |
| Ctrl+F | Buscar no quadro |
| Ctrl+Z / Ctrl+Shift+Z | Desfazer / refazer |
| F11 | Tela cheia |

## Onde ficam os dados

```
Documentos\Cofre\
  Quadros\    um arquivo .quadtab (JSON) por quadro
  Notas\      cada nota é um arquivo .md
  Anexos\     imagens, vídeos, arquivos e prévias
  .lixeira\   itens apagados
  .quadtab\   preferências, miniaturas e registro de erros
```

O salvamento é automático, cerca de 1 segundo depois de cada mudança.

## Compilar o código

Precisa do [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) no Windows.

Para rodar em modo de desenvolvimento:

```bash
dotnet run --project Quadtab
```

Para gerar um único `.exe`:

```bash
dotnet publish Quadtab -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

O arquivo sai em `Quadtab\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\`.

## Tecnologia

C#, .NET 10 e WPF no padrão MVVM, com [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) e `System.Text.Json`.

```
Quadtab/
  Assets/       fontes, texturas, ícone e shader
  Behaviors/    comportamentos da interface
  Controls/     quadro, itens, lateral, bandeja e menus
  Models/       dados dos quadros e dos itens
  Rendering/    câmera, virtualização, superfícies e cursores
  Services/     salvamento, mídia, exportação e tema
  Themes/       cores e estilos dos temas claro e escuro
  ViewModels/   lógica das telas
```

As fontes [Inter](https://github.com/rsms/inter) e [Nunito](https://github.com/googlefonts/nunito) são embutidas sob a licença SIL OFL. Os textos das licenças estão em `Quadtab/Assets/Fonts`.
