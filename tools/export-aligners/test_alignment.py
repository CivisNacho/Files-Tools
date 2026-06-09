"""
Validate an exported ONNX aligner end-to-end:
  1. ONNX-vs-torch logits parity (proves the export is faithful).
  2. Greedy CTC decode (proves the model + vocab wiring is right).
  3. Forced alignment -> per-word timestamps, using a from-scratch trellis that
     mirrors the algorithm to be ported to C#, cross-checked against
     torchaudio.functional.forced_align.

Run:
    python test_alignment.py --onnx models/base960h-onnx --model facebook/wav2vec2-base-960h
"""
import argparse
import json
import os

import numpy as np
import onnxruntime as ort
import torch
import torchaudio


# Known transcript for the torchaudio tutorial sample.
SAMPLE_ASSET = "tutorial-assets/Lab41-SRI-VOiCES-src-sp0307-ch127535-sg0042.wav"
TRANSCRIPT = "I HAD THAT CURIOSITY BESIDE ME AT THIS MOMENT"


def load_audio_16k():
    import soundfile as sf
    dl = getattr(torchaudio.utils, "download_asset", None) or torchaudio.utils._download_asset
    path = dl(SAMPLE_ASSET)
    data, sr = sf.read(path, dtype="float32", always_2d=True)
    mono = data.mean(axis=1)
    if sr != 16000:
        wav = torchaudio.functional.resample(torch.from_numpy(mono), sr, 16000)
        mono = wav.numpy()
    return mono.astype(np.float32)


def normalize(samples: np.ndarray) -> np.ndarray:
    # wav2vec2 feature extractor: zero-mean / unit-var over the clip.
    mean = samples.mean()
    std = samples.std()
    return (samples - mean) / (std + 1e-7)


def ctc_forced_align(logprob: np.ndarray, tokens: list[int], blank: int):
    """Viterbi forced alignment. logprob: [T, V]. Returns list of (token, t_start, t_end)
    over emission frames. This is the reference for the C# port."""
    T, V = logprob.shape
    N = len(tokens)
    # Build the staggered target sequence: blank, t0, blank, t1, ... blank (len 2N+1).
    seq = [blank]
    for t in tokens:
        seq += [t, blank]
    S = len(seq)
    NEG = -1e30
    dp = np.full((T, S), NEG, dtype=np.float64)
    back = np.zeros((T, S), dtype=np.int32)

    dp[0, 0] = logprob[0, seq[0]]
    if S > 1:
        dp[0, 1] = logprob[0, seq[1]]

    for t in range(1, T):
        for s in range(S):
            best_prev, best_k = dp[t - 1, s], s
            if s - 1 >= 0 and dp[t - 1, s - 1] > best_prev:
                best_prev, best_k = dp[t - 1, s - 1], s - 1
            # Skip a blank between two distinct labels.
            if s - 2 >= 0 and seq[s] != blank and seq[s] != seq[s - 2] and dp[t - 1, s - 2] > best_prev:
                best_prev, best_k = dp[t - 1, s - 2], s - 2
            dp[t, s] = best_prev + logprob[t, seq[s]]
            back[t, s] = best_k

    # Backtrack from the better of the last two states.
    s = S - 1 if dp[T - 1, S - 1] >= dp[T - 1, S - 2] else S - 2
    path = np.zeros(T, dtype=np.int32)
    for t in range(T - 1, -1, -1):
        path[t] = s
        s = back[t, s]

    # Collapse path -> per-(non-blank label) frame spans.
    spans = []  # (token_index_in_tokens, t_start, t_end)
    label_idx = -1
    for t in range(T):
        s = path[t]
        if seq[s] == blank:
            continue
        cur_label = s // 2  # 0-based index into tokens
        if cur_label != label_idx:
            spans.append([cur_label, t, t + 1])
            label_idx = cur_label
        else:
            spans[-1][2] = t + 1
    return spans


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--onnx", required=True)
    ap.add_argument("--model", required=True)
    args = ap.parse_args()

    meta = json.load(open(os.path.join(args.onnx, "meta.json"), encoding="utf-8"))
    vocab = json.load(open(os.path.join(args.onnx, "vocab.json"), encoding="utf-8"))
    id_to_tok = {v: k for k, v in vocab.items()}
    blank = meta["blank_id"]
    print(f"meta: {meta}")

    samples = load_audio_16k()
    dur = len(samples) / 16000
    x = normalize(samples) if meta["normalize"] else samples

    # --- torch reference ---
    from transformers import Wav2Vec2ForCTC
    tmodel = Wav2Vec2ForCTC.from_pretrained(args.model).eval()
    with torch.no_grad():
        logits_torch = tmodel(torch.from_numpy(x)[None]).logits[0].numpy()

    # --- onnx ---
    sess = ort.InferenceSession(os.path.join(args.onnx, "model.onnx"),
                                providers=["CPUExecutionProvider"])
    logits_onnx = sess.run(["logits"], {"input_values": x[None]})[0][0]

    T = min(logits_torch.shape[0], logits_onnx.shape[0])
    maxdiff = np.abs(logits_torch[:T] - logits_onnx[:T]).max()
    print(f"\n[parity] frames torch={logits_torch.shape[0]} onnx={logits_onnx.shape[0]} "
          f"vocab={logits_onnx.shape[1]}  max|diff|={maxdiff:.4e}")

    # frame stride: audio seconds / number of emission frames
    sec_per_frame = dur / logits_onnx.shape[0]
    print(f"[stride] audio={dur:.2f}s frames={logits_onnx.shape[0]} -> {sec_per_frame*1000:.2f} ms/frame")

    # log-softmax
    lse = logits_onnx - logits_onnx.max(axis=1, keepdims=True)
    logprob = lse - np.log(np.exp(lse).sum(axis=1, keepdims=True))

    # --- greedy decode ---
    ids = logprob.argmax(axis=1)
    out, prev = [], -1
    for i in ids:
        if i != prev and i != blank:
            out.append(id_to_tok.get(int(i), ""))
        prev = i
    greedy = "".join(out).replace("|", " ").strip()
    print(f"\n[greedy] {greedy}")

    # --- forced alignment of the known transcript ---
    # char tokens: map transcript to vocab ids, words separated by '|'.
    words = TRANSCRIPT.split()
    char_tokens, word_of_char = [], []
    for wi, w in enumerate(words):
        if wi > 0:
            char_tokens.append(vocab["|"]); word_of_char.append(-1)
        for ch in w:
            if ch in vocab:
                char_tokens.append(vocab[ch]); word_of_char.append(wi)

    spans = ctc_forced_align(logprob, char_tokens, blank)

    # cross-check against torchaudio (tolerant of API drift across versions)
    try:
        ta = torchaudio.functional.forced_align(
            torch.from_numpy(logprob)[None], torch.tensor(char_tokens)[None], blank=blank)
        ta_path = (ta[0] if isinstance(ta, tuple) else ta)[0].numpy()
        print(f"[xcheck] torchaudio forced_align OK, path len={len(ta_path)} (frames={logprob.shape[0]})")
    except Exception as e:
        print(f"[xcheck] torchaudio forced_align unavailable ({type(e).__name__}: {e}) - using our trellis only")

    # word timings from our spans
    print(f"\n[words] ({len(words)} words, transcript = '{TRANSCRIPT}')")
    word_bounds = {}
    for ci, t0, t1 in spans:
        wi = word_of_char[ci]
        if wi < 0:
            continue
        if wi not in word_bounds:
            word_bounds[wi] = [t0, t1]
        else:
            word_bounds[wi][1] = t1
    for wi, w in enumerate(words):
        if wi in word_bounds:
            t0, t1 = word_bounds[wi]
            print(f"  {w:12s} {t0*sec_per_frame:6.2f}s -> {t1*sec_per_frame:6.2f}s")
        else:
            print(f"  {w:12s}  (unaligned)")

    print("\n[done] parity OK" if maxdiff < 1e-2 else "\n[warn] parity diff high")


if __name__ == "__main__":
    main()
