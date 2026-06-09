# wav2vec2 / MMS forced-alignment ONNX export (dev tooling)

Dev-side tooling that converts the CTC acoustic models into ONNX for the .NET
`Wav2Vec2AlignmentService`. **Nothing here ships to end users** — only the produced
`model.onnx` + `vocab.json` + `meta.json` per model are hosted/bundled. The runtime is
pure .NET (ONNX Runtime); no Python or PyTorch on the user's machine.

## Models

| Tier | Model | Vocab | Notes |
|------|-------|-------|-------|
| `< 8 GB` RAM | `facebook/wav2vec2-base-960h` | 32 chars, blank=pad=0, word-delim `\|`=4 | English-only, has a CTC head. Logits (apply log-softmax). |
| `>= 8 GB` RAM | torchaudio `MMS_FA` (`mms-300m` FA) | 28 romanized lowercase chars, blank `-`=0, no separator | 1000+ languages. **Emits log-probs already.** Non-Latin transcripts must be uroman-romanized before tokenizing. |

`xlsr-53` was rejected: it is pretrained-only (no CTC head / vocab) and cannot forced-align.

## Setup (one time)

```powershell
# Python 3.11 (installed via: winget install --id Python.Python.3.11 --scope user)
python -m venv .venv
.\.venv\Scripts\python -m pip install --upgrade pip
.\.venv\Scripts\python -m pip install --index-url https://download.pytorch.org/whl/cpu torch torchaudio
.\.venv\Scripts\python -m pip install transformers optimum onnx onnxruntime onnxscript soundfile numpy
```

## Export + quantize + test

```powershell
$env:PYTHONUTF8="1"   # torch's verbose export prints emoji; Windows console needs UTF-8

# base-960h (< 8 GB tier)
.\.venv\Scripts\python export_aligner.py --model facebook/wav2vec2-base-960h --out models/base960h-onnx
.\.venv\Scripts\python quantize.py --in models/base960h-onnx --out models/base960h-int8
.\.venv\Scripts\python test_alignment.py --onnx models/base960h-int8 --model facebook/wav2vec2-base-960h

# MMS_FA (>= 8 GB tier)
.\.venv\Scripts\python export_aligner.py --mms-fa --out models/mms-fa-onnx
.\.venv\Scripts\python quantize.py --in models/mms-fa-onnx --out models/mms-fa-int8
.\.venv\Scripts\python test_mms.py --onnx models/mms-fa-int8
```

## Validated locally (CPU, torchaudio VOiCES sample, transcript known)

| Model | fp32 size | int8 size | fp32 parity max|diff| | Greedy decode | Word alignment |
|-------|-----------|-----------|----------------------|---------------|----------------|
| base-960h | 379 MB | **123 MB** | 1.9e-3 | letter-perfect | correct, monotonic |
| MMS_FA | 1265 MB | **357 MB** | 1.9e-3 | correct (romanized) | matches base within ~20 ms |

Stride is **20 ms/frame** (≈49 Hz) for both — the constant the C# frame→time mapping uses.

### Note on int8 "parity diff high"

After int8 quantization the raw-logit `max|diff|` jumps to ~7–8 (the final MatMul projection
is quantized, shifting absolute logit magnitudes). This is expected and **harmless**: forced
alignment depends only on the per-frame argmax / relative ordering, which is preserved — int8
word timings are identical to fp32 in testing. The `test_*.py` threshold message is tuned for
fp32 export verification, not int8.

## Notes / gotchas

- `torch.onnx.export` uses the dynamo path (needs `onnxscript`). Weights export as external
  `model.onnx.data`; int8 quantization (MatMul-only) consolidates and shrinks ~3×.
- The MMS export prints a non-fatal `RuntimeError` from onnxscript's opset version-converter
  during the optimize pass, then still writes a valid model (parity confirms faithfulness).
- Quantization is **MatMul-only**; the conv feature-extractor front-end isn't emitted as plain
  initializers by the dynamo exporter, so it stays fp32 (a small fraction of the weights).
- torchaudio 2.11 routes `load()` through torchcodec; tests read the wav via `soundfile`.

## What the C# side must replicate (porting target)

1. Decode/resample audio to 16 kHz mono float; per-clip zero-mean/unit-var normalize
   (when `meta.normalize`).
2. ONNX Runtime `Run` → logits `[1, frames, vocab]`; apply log-softmax unless
   `meta.logits_are_logprob`.
3. Tokenize the Whisper transcript per `meta.tokenizer_kind` (hf-char: uppercase + space→`|`;
   mms-roman: uroman-romanize, lowercase, drop non-vocab chars, no separator).
4. CTC forced alignment (Viterbi trellis) — see `ctc_forced_align` in `test_alignment.py`,
   the reference for the C# port. Collapse the path to per-token frame spans → merge to word
   spans → multiply by stride for timestamps.
