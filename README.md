# PhotoBoost

PhotoBoost é um aplicativo para Windows que melhora fotos com modelos de IA rodando no próprio
computador, sem enviar nada para a nuvem. Ele faz quatro operações:

| Operação | Modelo |
| --- | --- |
| Ampliar 2× e 4× | Real-ESRGAN x2plus e x4plus |
| Restaurar rostos | YuNet (detecção) e GFPGAN v1.4 (restauração) |
| Remover o fundo | RMBG-1.4, com a borda refinada pelo ViTMatte |
| Remover ruído | SCUNet (real, PSNR) |

A interface é WPF. A inferência usa o ONNX Runtime do Windows ML, na GPU por DirectML ou na CPU,
e o resultado pode ser comparado com o original, desfeito e exportado em PNG, JPEG ou WebP.

## Requisitos

- **Windows 10 versão 1903 (19H1) ou posterior, ou Windows 11, x64.** O Windows ML não roda em
  versões anteriores, e não há build para ARM, x86, macOS ou Linux.
- **GPU com DirectX 12** para rodar na placa de vídeo. Sem ela, ou se a GPU falhar no teste de
  inicialização, tudo roda na CPU, mais devagar, e a barra de status diz o motivo.
- **Memória:** o tamanho de cada bloco (tile) é escolhido pela memória livre, a de vídeo na GPU
  ou a RAM na CPU, de 128 a 1024 px. Para o Real-ESRGAN x4, o modelo mais pesado, a conta dá
  cerca de 1,1 GB para blocos de 128 px, 3,1 GB para 256 px e 6,2 GB para 384 px. Com menos
  memória o bloco só fica menor, e uma falta de memória no meio da execução tenta de novo com
  blocos menores e depois na CPU.
- **Espaço em disco:** cerca de 610 MB para os modelos que acompanham o instalador.

## Instalar

O instalador (`PhotoBoost-Setup-<versão>.exe`) instala só para o usuário atual, sem pedir
administrador, e já traz os modelos em FP16, então o aplicativo funciona offline desde a primeira
vez que abre.

## Rodar a partir do código

É preciso o **.NET SDK 10.0.100** ou um feature band mais novo (`global.json`).

```
dotnet build PhotoBoost.slnx
.\run.cmd
```

`.\run.cmd` compila em Debug e abre um menu de cenários de teste manual, cada um com uma imagem.
Com argumentos, abre o aplicativo direto com eles, por exemplo
`.\run.cmd --open foto.jpg --provider cpu`. As opções de linha de comando estão em
[docs/COMMAND-LINE.md](docs/COMMAND-LINE.md). Chame-o como `.\run.cmd`: um `run.cmd` sem o
caminho pode encontrar outro no PATH antes deste.

### Onde ficam os modelos

O aplicativo procura cada modelo nesta ordem:

1. a pasta `models` ao lado do executável, que o instalador preenche;
2. `%LOCALAPPDATA%\PhotoBoost\models`, para onde vão os downloads.

`--models <pasta>` troca as duas por uma só. O `run.cmd` usa `.cache\models` quando essa pasta
existe, que é onde o script de exportação grava. Nenhum modelo fica no repositório.

## Gerar os modelos

Os modelos são baixados das fontes originais e convertidos para ONNX por dois scripts em Python.
Testado com **Python 3.13**. O PyTorch da CPU basta: a exportação não precisa de CUDA nem de GPU.

```
python -m venv .venv
.venv\Scripts\python -m pip install torch==2.14.1 --index-url https://download.pytorch.org/whl/cpu
.venv\Scripts\python -m pip install -r scripts\requirements.txt
```

1. **Baixar os pesos** (cerca de 1 GB, em `.cache\weights`):

   ```
   .venv\Scripts\python scripts\download_models.py
   ```

   Usa só a biblioteca padrão. Cada arquivo é conferido pelo tamanho e pelo SHA-256 registrados em
   `scripts\model-sources.json`, um download interrompido continua de onde parou, e o que já está
   certo não é baixado de novo.

2. **Exportar para ONNX** (em `.cache\models`):

   ```
   .venv\Scripts\python scripts\export-and-quantize.py
   .venv\Scripts\python scripts\export-and-quantize.py --only gfpgan-v1.4
   ```

   Para cada modelo grava `fp32.onnx`, `fp16.onnx` e `int8.onnx` com um `meta.json` que o
   aplicativo lê. Cada variante é comparada com o PyTorch e com a FP32 na imagem de teste do
   próprio modelo; uma exportação que difere além da tolerância, ou um INT8 abaixo do seu piso de
   PSNR, faz o script falhar em vez de gravar um modelo errado. O ViTMatte não tem FP16 e o YuNet
   só tem FP32.

## Gerar o instalador

```
build\build-installer.cmd
```

Publica em Release (win-x64, self-contained), junta as licenças dos pacotes, copia o conjunto FP16
de `.cache\models` e compila o instalador com o Inno Setup 6. Falta de algum modelo interrompe o
build. `build\update-release.cmd <versão>` define a versão, roda os testes e chama o
`build-installer.cmd`.

## Testes

```
run-tests.cmd
```

Compila a solução com avisos tratados como erros e os analisadores de desempenho ligados, roda
cada aplicativo de teste em `tests\`, os casos de interface do winwright (`cases\*.cases.json`,
pulados com o motivo numa máquina onde a tela não pode ser observada) e os testes em Python de
`scripts\tests`. O resultado só é verde se tudo rodou: uma suíte ausente ou sem Python no PATH
conta como falha. Os testes que usam os modelos reais rodam quando `.cache\models` existe e são
pulados, com o motivo, quando não.

## Provedores de execução

Por padrão (`--provider auto`) a inferência roda no DirectML, na GPU com mais memória de vídeo,
e cai para a CPU se ela não rodar. `--provider winml` usa, quando o Windows ML os instalou, o
TensorRT-RTX (NVIDIA) ou o OpenVINO (Intel). O TensorRT-RTX processa cada bloco em FP16 cerca de
três vezes mais rápido, mas monta o motor por 10 a 17 s sempre que uma sessão é criada, por isso
não é o padrão.

## FP16 e FP32 medidos

O instalador traz o conjunto FP16; o FP32 é o de alta qualidade. A tabela abaixo saiu de
`tools\PhotoBoost.Bench`, que roda cada operação pelos mesmos pipelines do aplicativo, na foto de
teste do próprio modelo, nas duas variantes, na CPU e no DirectML:

```
dotnet run --project tools\PhotoBoost.Bench -c Release -- --out bench.md
```

"1ª execução" inclui carregar a sessão; "Execução" é a mais rápida de duas execuções seguintes.
O pico de memória é amostrado a cada 10 ms a partir de antes de carregar a sessão: memória de
vídeo no DirectML, memória privada do processo na CPU. PSNR e SSIM comparam o resultado FP16 com
o FP32 no mesmo provedor (só o canal alfa na remoção de fundo). Ampliação e remoção de ruído
usam blocos de 256 px.

Medido em 2026-10-20: Intel Core i7-14700; NVIDIA GeForce RTX 4060, 8 GB.

| Operação | Provedor | Variante | Arquivo | 1ª execução | Execução | Pico de memória | PSNR vs FP32 | SSIM vs FP32 |
| --- | --- | --- | --: | --: | --: | --: | --: | --: |
| Ampliar 4× | CPU | FP16 | 32.2 MB | 7.53 s | 5.73 s | 1143 MB | 70.9 dB | 1.0000 |
| Ampliar 4× | CPU | FP32 | 63.9 MB | 2.92 s | 2.06 s | 1151 MB | — | — |
| Ampliar 4× | DirectML | FP16 | 32.2 MB | 1.99 s | 0.21 s | 413 MB | 60.1 dB | 0.9999 |
| Ampliar 4× | DirectML | FP32 | 63.9 MB | 1.11 s | 0.16 s | 520 MB | — | — |
| Ampliar 2× | CPU | FP16 | 32.2 MB | 3.08 s | 0.92 s | 191 MB | 71.9 dB | 1.0000 |
| Ampliar 2× | CPU | FP32 | 64.0 MB | 1.41 s | 0.52 s | 282 MB | — | — |
| Ampliar 2× | DirectML | FP16 | 32.2 MB | 2.02 s | 0.06 s | 146 MB | 59.7 dB | 1.0000 |
| Ampliar 2× | DirectML | FP32 | 64.0 MB | 1.17 s | 0.04 s | 225 MB | — | — |
| Restaurar rostos | CPU | FP16 | 162.5 MB | 3.77 s | 2.66 s | 1974 MB | 76.8 dB | 1.0000 |
| Restaurar rostos | CPU | FP32 | 324.6 MB | 1.98 s | 1.54 s | 1446 MB | — | — |
| Restaurar rostos | DirectML | FP16 | 162.5 MB | 1.19 s | 0.08 s | 1306 MB | 12.7 dB | 0.6177 |
| Restaurar rostos | DirectML | FP32 | 324.6 MB | 1.10 s | 0.13 s | 1551 MB | — | — |
| Remover fundo | CPU | FP16 | 84.1 MB | 0.90 s | 0.54 s | 1256 MB | 84.8 dB | 1.0000 |
| Remover fundo | CPU | FP32 | 168.0 MB | 0.84 s | 0.54 s | 2322 MB | — | — |
| Remover fundo | DirectML | FP16 | 84.1 MB | 0.89 s | 0.09 s | 412 MB | 74.8 dB | 1.0000 |
| Remover fundo | DirectML | FP32 | 168.0 MB | 0.69 s | 0.07 s | 861 MB | — | — |
| Remover ruído | CPU | FP16 | 35.6 MB | 5.63 s | 3.57 s | 958 MB | 82.6 dB | 1.0000 |
| Remover ruído | CPU | FP32 | 69.5 MB | 6.06 s | 3.82 s | 578 MB | — | — |
| Remover ruído | DirectML | FP16 | 35.6 MB | 10.16 s | 2.59 s | 314 MB | 66.0 dB | 1.0000 |
| Remover ruído | DirectML | FP32 | 69.5 MB | 8.03 s | 1.41 s | 573 MB | — | — |

O que a tabela mostra:

- **O FP16 tem metade do tamanho e, fora um caso, a mesma imagem:** de 59,7 a 84,8 dB contra o
  FP32, SSIM de 0,9999 ou mais.
- **A exceção é o GFPGAN em FP16 no DirectML, que sai errado** (12,7 dB): os rostos viram um
  quadrado liso. Na CPU o mesmo arquivo está certo. Por isso o instalador traz o GFPGAN em FP32,
  e numa GPU o aplicativo restaura rostos com ele mesmo no modo padrão. A linha da tabela foi
  medida antes dessa troca; medida de novo, ela mostra o FP32 que o aplicativo usa.
- **Na CPU o FP32 é mais rápido** na ampliação e na restauração de rostos (2,8× na ampliação 4×);
  é o que o download do conjunto FP32 resolve. No DirectML as duas variantes têm tempos
  próximos, fora a remoção de ruído, e o FP16 usa menos memória de vídeo.

## Licenças dos modelos

| Modelo | Licença |
| --- | --- |
| Real-ESRGAN x4plus e x2plus | BSD-3-Clause |
| GFPGAN v1.4 | Apache-2.0, com partes sob licenças **não comerciais** (StyleGAN2, DFDNet) |
| YuNet 2023mar | MIT |
| RMBG-1.4 | Licença da BRIA, **uso não comercial**; uso comercial exige licença da BRIA |
| ViTMatte small | Apache-2.0 (código MIT) |
| SCUNet | Apache-2.0 |

Como o GFPGAN v1.4 e o RMBG-1.4 só podem ser usados sem fins comerciais, confira cada licença
antes de usar um resultado comercialmente. O painel Sobre do aplicativo lista cada modelo
instalado com a licença do seu `meta.json`, e a pasta `licenses` da instalação traz as licenças
dos pacotes de terceiros. As fontes e os links de cada licença estão em
`scripts\model-sources.json`.
