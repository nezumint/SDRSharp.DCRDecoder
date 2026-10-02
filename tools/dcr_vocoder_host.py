"""Persistent binary-only pipe helper. 1-byte commands: R reset, D + 7 bytes decode.
Responses: R acknowledgment or 320 little-endian PCM bytes. Startup: DCR1.
Never logs audio, input frames or privacy codes. Runtime errors go to stderr.
"""
import sys
import struct
from blip25_vocoder import Rate, Vocoder

def read_exact(size):
    data = bytearray()
    while len(data) < size:
        part = sys.stdin.buffer.read(size - len(data))
        if not part:
            raise EOFError('Truncated request')
        data.extend(part)
    return bytes(data)

def main():
    voc = Vocoder(Rate.HALF_RATE_2450X2450)
    out = sys.stdout.buffer
    out.write(b'DCR1'); out.flush()
    while True:
        cmd = sys.stdin.buffer.read(1)
        if not cmd:
            return
        if cmd == b'R':
            voc.reset()
            out.write(b'R')
        elif cmd == b'D':
            pcm = voc.decode_bits(read_exact(7))
            if len(pcm) != 160:
                raise RuntimeError('Vocoder did not return 160 PCM samples')
            out.write(struct.pack('<160h', *(max(-32768, min(32767, int(x))) for x in pcm)))
        else:
            raise ValueError('Unknown command')
        out.flush()

if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(str(exc), file=sys.stderr)
        sys.exit(1)
