"""SCUNet (Zhang et al., "Practical Blind Image Denoising via Swin-Conv-UNet and Data Synthesis").

Vendored from https://github.com/cszn/SCUNet/blob/52e440a80a655b01e0b41e9dd9bfe599bc11625e/models/network_scunet.py
under the Apache License 2.0 (Copyright Kai Zhang), changed for inference and ONNX export (PB36):

- einops' rearrange is written out as reshape and permute, and timm's DropPath and trunc_normal_
  are gone (the drop path rate is 0 and the weights are loaded), so neither package is needed;
- the replication padding to a multiple of 64 moved out of forward(): it was computed from the
  traced size, which fixed the exported graph to one size. Callers pad (the app's tiler does);
- attention stays within five dimensions and uses matmul instead of einsum, and the shifted-window
  mask is built from band labels instead of a 6-D tensor: ONNX Runtime's DirectML provider
  crashed on upstream's graph. The arithmetic is the same (scripts/tests/test_export.py);
- the debug print and the thop import are dropped.

Parameter names are unchanged, so the published state dicts load with strict=True.
"""

from __future__ import annotations

import torch
from torch import nn


class WMSA(nn.Module):
    """Window (or shifted-window) multi-head self-attention, on b h w c."""

    def __init__(self, input_dim: int, output_dim: int, head_dim: int, window_size: int, type: str) -> None:
        super().__init__()
        self.input_dim = input_dim
        self.output_dim = output_dim
        self.head_dim = head_dim
        self.scale = self.head_dim**-0.5
        self.n_heads = input_dim // head_dim
        self.window_size = window_size
        self.type = type
        self.embedding_layer = nn.Linear(self.input_dim, 3 * self.input_dim, bias=True)
        self.relative_position_params = nn.Parameter(torch.zeros(self.n_heads, 2 * window_size - 1, 2 * window_size - 1))
        self.linear = nn.Linear(self.input_dim, self.output_dim)
        cord = torch.tensor([[i, j] for i in range(window_size) for j in range(window_size)])
        relation = cord[:, None, :] - cord[None, :, :] + window_size - 1
        self.register_buffer("relation", relation, persistent=False)

    def partition(self, x: torch.Tensor) -> torch.Tensor:
        """b (w1 p1) (w2 p2) c -> (b w1 w2) (p1 p2) c, through tensors of at most five dimensions:
        ONNX Runtime's DirectML provider crashed (an access violation in Run) on upstream's 6-D form."""
        b, height, width, c = x.shape
        p = self.window_size
        x = x.reshape(b * (height // p), p, width // p, p, c).permute(0, 2, 1, 3, 4)
        return x.reshape(-1, p * p, c)

    def unpartition(self, x: torch.Tensor, b: int, height: int, width: int) -> torch.Tensor:
        """The inverse of partition()."""
        p = self.window_size
        x = x.reshape(b * (height // p), width // p, p, p, -1).permute(0, 2, 1, 3, 4)
        return x.reshape(b, height, width, -1)

    def generate_mask(self, height: int, width: int) -> torch.Tensor:
        """The SW-MSA mask, (w1 w2) (p1 p2) (p1 p2), True where attention must not reach.

        Upstream builds it as a 6-D tensor; this labels the three bands the cyclic shift makes
        along each axis, as the Swin Transformer reference does, and masks pairs with different
        labels: the same mask (scripts/tests/test_export.py holds the two together)."""
        p, shift = self.window_size, self.window_size // 2
        device = self.relative_position_params.device
        rows = torch.arange(height, device=device)
        cols = torch.arange(width, device=device)
        band_rows = (rows >= height - p).long() + (rows >= height - shift).long()
        band_cols = (cols >= width - p).long() + (cols >= width - shift).long()
        # Integer labels: a float cast here is one the FP16 converter rewrites into a type clash.
        labels = (band_rows[:, None] * 3 + band_cols[None, :])[None, :, :, None]
        windows = self.partition(labels)[:, :, 0]
        return windows[:, :, None] != windows[:, None, :]

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        p = self.window_size
        if self.type != "W":
            x = torch.roll(x, shifts=(-(p // 2), -(p // 2)), dims=(1, 2))
        b, height, width, _ = x.shape
        windows = self.partition(x)
        n = windows.shape[0]
        qkv = self.embedding_layer(windows)
        # n np (three h c) -> three (n h) np c, as batched matrices rather than einsum.
        qkv = qkv.reshape(n, p * p, 3, self.n_heads, self.head_dim).permute(2, 0, 3, 1, 4)
        q, k, v = qkv[0], qkv[1], qkv[2]
        sim = torch.matmul(q, k.transpose(-2, -1)) * self.scale
        sim = sim + self.relative_embedding()[None, :, :, :]
        if self.type != "W":
            mask = self.generate_mask(height, width).repeat(b, 1, 1)
            sim = sim.masked_fill(mask[:, None, :, :], float("-inf"))
        probs = nn.functional.softmax(sim, dim=-1)
        output = torch.matmul(probs, v).permute(0, 2, 1, 3).reshape(n, p * p, self.n_heads * self.head_dim)
        output = self.unpartition(self.linear(output), b, height, width)
        if self.type != "W":
            output = torch.roll(output, shifts=(p // 2, p // 2), dims=(1, 2))
        return output

    def relative_embedding(self) -> torch.Tensor:
        return self.relative_position_params[:, self.relation[:, :, 0], self.relation[:, :, 1]]


class Block(nn.Module):
    """A Swin Transformer block."""

    def __init__(self, input_dim: int, output_dim: int, head_dim: int, window_size: int, type: str = "W", input_resolution: int = 256) -> None:
        super().__init__()
        self.type = "W" if input_resolution <= window_size else type
        self.ln1 = nn.LayerNorm(input_dim)
        self.msa = WMSA(input_dim, input_dim, head_dim, window_size, self.type)
        self.ln2 = nn.LayerNorm(input_dim)
        self.mlp = nn.Sequential(nn.Linear(input_dim, 4 * input_dim), nn.GELU(), nn.Linear(4 * input_dim, output_dim))

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        x = x + self.msa(self.ln1(x))
        return x + self.mlp(self.ln2(x))


class ConvTransBlock(nn.Module):
    """Half the channels through a residual conv block, half through a Swin block, then fused."""

    def __init__(self, conv_dim: int, trans_dim: int, head_dim: int, window_size: int, type: str = "W", input_resolution: int = 256) -> None:
        super().__init__()
        self.conv_dim = conv_dim
        self.trans_dim = trans_dim
        type = "W" if input_resolution <= window_size else type
        self.trans_block = Block(trans_dim, trans_dim, head_dim, window_size, type, input_resolution)
        self.conv1_1 = nn.Conv2d(conv_dim + trans_dim, conv_dim + trans_dim, 1, 1, 0, bias=True)
        self.conv1_2 = nn.Conv2d(conv_dim + trans_dim, conv_dim + trans_dim, 1, 1, 0, bias=True)
        self.conv_block = nn.Sequential(
            nn.Conv2d(conv_dim, conv_dim, 3, 1, 1, bias=False),
            nn.ReLU(True),
            nn.Conv2d(conv_dim, conv_dim, 3, 1, 1, bias=False),
        )

    def forward(self, x: torch.Tensor) -> torch.Tensor:
        conv_x, trans_x = torch.split(self.conv1_1(x), (self.conv_dim, self.trans_dim), dim=1)
        conv_x = self.conv_block(conv_x) + conv_x
        trans_x = self.trans_block(trans_x.permute(0, 2, 3, 1)).permute(0, 3, 1, 2)
        return x + self.conv1_2(torch.cat((conv_x, trans_x), dim=1))


class SCUNet(nn.Module):
    """The denoiser. Height and width must be multiples of 64; values RGB in [0, 1]."""

    def __init__(self, in_nc: int = 3, config: tuple[int, ...] = (4, 4, 4, 4, 4, 4, 4), dim: int = 64, input_resolution: int = 256) -> None:
        super().__init__()
        head_dim, window = 32, 8

        def stage(channels: int, count: int, resolution: int) -> list[nn.Module]:
            return [ConvTransBlock(channels, channels, head_dim, window, "W" if not i % 2 else "SW", resolution) for i in range(count)]

        self.m_head = nn.Sequential(nn.Conv2d(in_nc, dim, 3, 1, 1, bias=False))
        self.m_down1 = nn.Sequential(*stage(dim // 2, config[0], input_resolution), nn.Conv2d(dim, 2 * dim, 2, 2, 0, bias=False))
        self.m_down2 = nn.Sequential(*stage(dim, config[1], input_resolution // 2), nn.Conv2d(2 * dim, 4 * dim, 2, 2, 0, bias=False))
        self.m_down3 = nn.Sequential(*stage(2 * dim, config[2], input_resolution // 4), nn.Conv2d(4 * dim, 8 * dim, 2, 2, 0, bias=False))
        self.m_body = nn.Sequential(*stage(4 * dim, config[3], input_resolution // 8))
        self.m_up3 = nn.Sequential(nn.ConvTranspose2d(8 * dim, 4 * dim, 2, 2, 0, bias=False), *stage(2 * dim, config[4], input_resolution // 4))
        self.m_up2 = nn.Sequential(nn.ConvTranspose2d(4 * dim, 2 * dim, 2, 2, 0, bias=False), *stage(dim, config[5], input_resolution // 2))
        self.m_up1 = nn.Sequential(nn.ConvTranspose2d(2 * dim, dim, 2, 2, 0, bias=False), *stage(dim // 2, config[6], input_resolution))
        self.m_tail = nn.Sequential(nn.Conv2d(dim, in_nc, 3, 1, 1, bias=False))

    def forward(self, x0: torch.Tensor) -> torch.Tensor:
        x1 = self.m_head(x0)
        x2 = self.m_down1(x1)
        x3 = self.m_down2(x2)
        x4 = self.m_down3(x3)
        x = self.m_body(x4)
        x = self.m_up3(x + x4)
        x = self.m_up2(x + x3)
        x = self.m_up1(x + x2)
        return self.m_tail(x + x1)
