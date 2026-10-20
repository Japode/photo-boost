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
- **Espaço em disco:** cerca de 450 MB para os modelos que acompanham o instalador.

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
