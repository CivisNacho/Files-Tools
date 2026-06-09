"""
Dynamic int8 quantization of an exported aligner ONNX, to shrink the shipped/downloaded
model. Quantizes MatMul/Gemm weights (the transformer bulk); conv front-end stays fp32.

Run:
    python quantize.py --in models/base960h-onnx --out models/base960h-int8
"""
import argparse
import json
import os
import shutil

from onnxruntime.quantization import quantize_dynamic, QuantType


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="src", required=True)
    ap.add_argument("--out", dest="dst", required=True)
    args = ap.parse_args()

    os.makedirs(args.dst, exist_ok=True)
    src_model = os.path.join(args.src, "model.onnx")
    dst_model = os.path.join(args.dst, "model.onnx")

    # Quantize only the transformer matmuls; the conv feature-extractor front-end is left
    # fp32 (the dynamo exporter doesn't emit its conv weights as plain initializers, and it is
    # a small fraction of the weights anyway).
    quantize_dynamic(src_model, dst_model, weight_type=QuantType.QInt8,
                     op_types_to_quantize=["MatMul"])

    # Carry vocab/meta across unchanged.
    for name in ("vocab.json", "meta.json"):
        shutil.copyfile(os.path.join(args.src, name), os.path.join(args.dst, name))

    def dirsize(d):
        return sum(os.path.getsize(os.path.join(d, f)) for f in os.listdir(d)
                   if f.startswith("model.onnx"))

    print(f"[ok] {dirsize(args.src)/1e6:.1f} MB -> {dirsize(args.dst)/1e6:.1f} MB  ({args.dst})")


if __name__ == "__main__":
    main()
