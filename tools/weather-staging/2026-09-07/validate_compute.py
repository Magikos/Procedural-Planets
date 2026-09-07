"""Compile the actual staged rain kernel with Windows D3DCompiler, without Unity."""
import ctypes
from pathlib import Path
import re

root = Path(__file__).resolve().parent
compiler = ctypes.WinDLL("d3dcompiler_47.dll")
compile_shader = compiler.D3DCompile
compile_shader.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p,
    ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
    ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
compile_shader.restype = ctypes.c_long


def blob_bytes(blob):
    table = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(table[3])(blob)
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(table[4])(blob)
    return ctypes.string_at(pointer, size)


def release(blob):
    if blob:
        table = ctypes.cast(blob, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
        ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(table[2])(blob)


for relative, entry in (
    ("Assets/Resources/RainParticleUpdate.compute", "RainUpdate"),
    ("Assets/Graphics/Shaders/WeatherEvolution.compute", "CSEvolveWeather"),
    ("Assets/Graphics/Shaders/WeatherEvolution.compute", "CSInitWeather"),
):
    source = root / "candidate" / relative
    text = source.read_text(encoding="utf-8-sig")
    def include(match):
        path = Path(relative).parent / match.group(1)
        staged = root / "candidate" / path
        return (staged if staged.exists() else root.parents[2] / path).read_text(encoding="utf-8-sig")
    text = re.sub(r'#include "([^"]+)"', include, text)
    data = text.encode()
    code, errors = ctypes.c_void_p(), ctypes.c_void_p()
    try:
        result = compile_shader(data, len(data), str(source).encode(), None, None,
            entry.encode(), b"cs_5_0", 1 << 11, 0, ctypes.byref(code), ctypes.byref(errors))
        diagnostics = blob_bytes(errors).decode(errors="replace").rstrip("\0") if errors else ""
        if diagnostics:
            print(diagnostics)
        if result < 0:
            raise SystemExit(f"{entry} compile failed: HRESULT 0x{result & 0xffffffff:08x}")
        print(f"{entry} cs_5_0 compiled: {len(blob_bytes(code))} bytes. GPU execution remains queued.")
    finally:
        release(code)
        release(errors)
