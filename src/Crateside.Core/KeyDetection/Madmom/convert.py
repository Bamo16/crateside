"""Convert madmom's key_cnn.pkl to key-cnn.onnx, folding batch norm into each convolution.

Needs madmom (0.17.dev0 was used), onnx and numpy<2; madmom does not install on Windows, so run it in
a Linux container:

    python3 convert.py key-cnn.onnx
"""
import sys
import numpy as np
import onnx
from onnx import helper, TensorProto, numpy_helper
from madmom.models import KEY_CNN
from madmom.ml.nn import NeuralNetwork
from madmom.ml.nn.layers import PadLayer, ConvolutionalLayer, BatchNormLayer, MaxPoolLayer, AverageLayer

net = NeuralNetwork.load(KEY_CNN[0])
nodes, inits = [], []
cur = "spectrogram"
pad = 0
i = 0
layers = net.layers
k = 0
while k < len(layers):
    l = layers[k]
    if isinstance(l, PadLayer):
        pad = l.width
        k += 1
        continue
    if isinstance(l, ConvolutionalLayer):
        bn = layers[k + 1]
        assert isinstance(bn, BatchNormLayer)
        w = l.weights.astype(np.float64)              # (cin, cout, kh, kw), true convolution
        w = np.transpose(w, (1, 0, 2, 3))[:, :, ::-1, ::-1]  # ONNX Conv correlates
        scale = bn.gamma * bn.inv_std                 # per output channel
        w = w * scale[:, None, None, None]
        b = (np.broadcast_to(l.bias, scale.shape) - bn.mean) * scale + bn.beta
        wn, bnm = f"w{i}", f"b{i}"
        inits += [numpy_helper.from_array(np.ascontiguousarray(w).astype(np.float32), wn),
                  numpy_helper.from_array(b.astype(np.float32), bnm)]
        kh, kw = w.shape[2:]
        nodes.append(helper.make_node("Conv", [cur, wn, bnm], [f"c{i}"], kernel_shape=[kh, kw], pads=[pad, pad, pad, pad]))
        nodes.append(helper.make_node("Elu", [f"c{i}"], [f"e{i}"], alpha=1.0))
        cur = f"e{i}"
        pad = 0
        i += 1
        k += 2
        continue
    if isinstance(l, MaxPoolLayer):
        assert tuple(l.size) == (2, 2) and tuple(l.stride) == (2, 2)
        nodes.append(helper.make_node("MaxPool", [cur], [f"p{i}"], kernel_shape=[2, 2], strides=[2, 2]))
        cur = f"p{i}"
        i += 1
        k += 1
        continue
    if isinstance(l, AverageLayer):
        nodes.append(helper.make_node("ReduceMean", [cur], ["mean"], axes=[2, 3], keepdims=0))
        nodes.append(helper.make_node("Softmax", ["mean"], ["probabilities"], axis=1))
        k += 1
        continue
    raise SystemExit(f"unexpected layer {type(l).__name__}")

graph = helper.make_graph(
    nodes, "madmom_key_cnn",
    [helper.make_tensor_value_info("spectrogram", TensorProto.FLOAT, [1, 1, "frames", 105])],
    [helper.make_tensor_value_info("probabilities", TensorProto.FLOAT, [1, 24])],
    inits,
)
model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 13)], producer_name="crateside")
model.ir_version = 8
model.doc_string = "madmom 2018 key CNN (Korzeniowski & Widmer, ISMIR 2018), converted from key_cnn.pkl. CC BY-NC-SA 4.0."
onnx.checker.check_model(model)
onnx.save(model, sys.argv[1])
print("saved", sys.argv[1])
