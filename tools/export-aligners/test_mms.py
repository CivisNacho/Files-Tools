"""
Validate the exported MMS_FA ONNX aligner (multilingual / >=8GB tier).

Differs from base-960h: reference model is torchaudio's MMS_FA bundle (no transformers),
the vocab is a 28-token romanized lowercase set with blank '-' at id 0 and NO word
separator (word gaps are absorbed by blanks), and the model already emits log-probs.

Run:
    python test_mms.py --onnx models/mms-fa-onnx
"""
import argparse
import json
import os

import numpy as np
import onnxruntime as ort
import torch
import torchaudio

from test_alignment import load_audio_16k, normalize, ctc_forced_align, TRANSCRIPT


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--onnx", required=True)
    args = ap.parse_args()

    meta = json.load(open(os.path.join(args.onnx, "meta.json"), encoding="utf-8"))
    vocab = json.load(open(os.path.join(args.onnx, "vocab.json"), encoding="utf-8"))
    id_to_tok = {v: k for k, v in vocab.items()}
    blank = meta["blank_id"]
    print(f"meta: {meta}")

    samples = load_audio_16k()
    dur = len(samples) / 16000
    x = normalize(samples) if meta["normalize"] else samples

    # --- torchaudio reference ---
    bundle = torchaudio.pipelines.MMS_FA
    ref = bundle.get_model(with_star=False).eval()
    with torch.no_grad():
        emis_ref, _ = ref(torch.from_numpy(x)[None])
    logprob_ref = emis_ref[0].numpy()

    # --- onnx ---
    sess = ort.InferenceSession(os.path.join(args.onnx, "model.onnx"),
                                providers=["CPUExecutionProvider"])
    logprob_onnx = sess.run(["logits"], {"input_values": x[None]})[0][0]

    T = min(logprob_ref.shape[0], logprob_onnx.shape[0])
    maxdiff = np.abs(logprob_ref[:T] - logprob_onnx[:T]).max()
    print(f"\n[parity] frames ref={logprob_ref.shape[0]} onnx={logprob_onnx.shape[0]} "
          f"vocab={logprob_onnx.shape[1]}  max|diff|={maxdiff:.4e}")

    sec_per_frame = dur / logprob_onnx.shape[0]
    print(f"[stride] audio={dur:.2f}s frames={logprob_onnx.shape[0]} -> {sec_per_frame*1000:.2f} ms/frame")

    logprob = logprob_onnx  # already log-prob per meta

    # --- greedy decode (romanized) ---
    ids = logprob.argmax(axis=1)
    out, prev = [], -1
    for i in ids:
        if i != prev and i != blank:
            out.append(id_to_tok.get(int(i), ""))
        prev = i
    print(f"\n[greedy] {''.join(out)}")

    # --- forced alignment of romanized transcript (lowercase; drop non-vocab chars) ---
    words = TRANSCRIPT.lower().split()
    char_tokens, word_of_char = [], []
    for wi, w in enumerate(words):
        for ch in w:
            if ch in vocab and ch != "-":
                char_tokens.append(vocab[ch]); word_of_char.append(wi)

    spans = ctc_forced_align(logprob, char_tokens, blank)

    word_bounds = {}
    for ci, t0, t1 in spans:
        wi = word_of_char[ci]
        word_bounds.setdefault(wi, [t0, t1])
        word_bounds[wi][1] = t1

    print(f"\n[words] ({len(words)} words, transcript = '{TRANSCRIPT}')")
    for wi, w in enumerate(words):
        if wi in word_bounds:
            t0, t1 = word_bounds[wi]
            print(f"  {w:12s} {t0*sec_per_frame:6.2f}s -> {t1*sec_per_frame:6.2f}s")
        else:
            print(f"  {w:12s}  (unaligned)")

    print("\n[done] parity OK" if maxdiff < 1e-2 else "\n[warn] parity diff high")


if __name__ == "__main__":
    main()
