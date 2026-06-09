"""
Export a wav2vec2 / MMS CTC model to ONNX for use by the .NET Wav2Vec2AlignmentService.

This is DEV-SIDE tooling. The resulting .onnx + vocab.json + meta.json are the only
artifacts shipped/hosted; no Python or torch runs on the end user's machine.

Usage:
    python export_aligner.py --model facebook/wav2vec2-base-960h --out models/base960h-onnx
    python export_aligner.py --mms-fa --out models/mms-fa-onnx

Outputs in <out>/:
    model.onnx     float32 CTC acoustic model: input_values[B,T] -> logits[B,T',V]
    vocab.json     token -> id map (the CTC vocabulary)
    meta.json      sample_rate, normalize, blank/pad/word-delimiter ids, frame stride
"""
import argparse
import json
import os

import numpy as np
import torch


def export_hf_ctc(model_id: str, out_dir: str):
    """Export a HuggingFace Wav2Vec2ForCTC checkpoint (e.g. base-960h)."""
    from transformers import Wav2Vec2ForCTC, Wav2Vec2Processor

    processor = Wav2Vec2Processor.from_pretrained(model_id)
    model = Wav2Vec2ForCTC.from_pretrained(model_id)
    model.eval()

    vocab = processor.tokenizer.get_vocab()  # token -> id
    normalize = bool(getattr(processor.feature_extractor, "do_normalize", True))
    pad_id = processor.tokenizer.pad_token_id
    # CTC blank == pad for wav2vec2.
    blank_id = pad_id
    word_delim = vocab.get("|", None)
    return _finish_export(model, out_dir, vocab, normalize, blank_id, pad_id, word_delim,
                          tokenizer_kind="hf-char")


def export_mms_fa(out_dir: str):
    """Export torchaudio's MMS_FA forced-alignment model (multilingual, romanized vocab)."""
    import torchaudio

    bundle = torchaudio.pipelines.MMS_FA
    model = bundle.get_model(with_star=False)
    model.eval()

    # bundle.get_dict() -> token -> id (includes <blank>/'-' and '<star>' depending on flags)
    token_dict = bundle.get_dict(star=None)  # no star token to keep vocab clean
    vocab = {tok: idx for tok, idx in token_dict.items()}
    # torchaudio MMS_FA: blank is index 0 ('-'); word boundary is whitespace mapped separately.
    blank_id = 0
    word_delim = vocab.get("|", None)

    class Wrapper(torch.nn.Module):
        def __init__(self, m):
            super().__init__()
            self.m = m

        def forward(self, input_values):
            # torchaudio MMS model returns (emissions, lengths); emissions are already log-probs.
            emissions, _ = self.m(input_values)
            return emissions

    return _finish_export(Wrapper(model), out_dir, vocab, normalize=True,
                          blank_id=blank_id, pad_id=blank_id, word_delim=word_delim,
                          tokenizer_kind="mms-roman", already_logprob=True)


def _finish_export(model, out_dir, vocab, normalize, blank_id, pad_id, word_delim,
                   tokenizer_kind, already_logprob=False):
    os.makedirs(out_dir, exist_ok=True)
    onnx_path = os.path.join(out_dir, "model.onnx")

    # 1 second of dummy 16kHz audio as the export trace input.
    dummy = torch.zeros(1, 16000, dtype=torch.float32)

    torch.onnx.export(
        model,
        (dummy,),
        onnx_path,
        input_names=["input_values"],
        output_names=["logits"],
        dynamic_axes={"input_values": {0: "batch", 1: "samples"},
                      "logits": {0: "batch", 1: "frames"}},
        opset_version=17,
        do_constant_folding=True,
    )

    with open(os.path.join(out_dir, "vocab.json"), "w", encoding="utf-8") as f:
        json.dump(vocab, f, ensure_ascii=False, indent=0)

    meta = {
        "sample_rate": 16000,
        "normalize": normalize,
        "blank_id": blank_id,
        "pad_id": pad_id,
        "word_delimiter_id": word_delim,
        "tokenizer_kind": tokenizer_kind,
        "logits_are_logprob": already_logprob,
        "vocab_size": len(vocab),
    }
    with open(os.path.join(out_dir, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)

    size_mb = os.path.getsize(onnx_path) / (1024 * 1024)
    print(f"[ok] exported {tokenizer_kind} -> {onnx_path} ({size_mb:.1f} MB), vocab={len(vocab)}")
    return onnx_path


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", help="HF model id (Wav2Vec2ForCTC), e.g. facebook/wav2vec2-base-960h")
    ap.add_argument("--mms-fa", action="store_true", help="Export torchaudio MMS_FA aligner")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    if args.mms_fa:
        export_mms_fa(args.out)
    elif args.model:
        export_hf_ctc(args.model, args.out)
    else:
        ap.error("provide --model <id> or --mms-fa")


if __name__ == "__main__":
    main()
