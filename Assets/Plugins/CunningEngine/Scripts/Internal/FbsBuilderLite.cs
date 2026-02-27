using System;
using System.Text;

namespace CunningEngine.FlatBuffersLite
{
    // Minimal FlatBuffers builder (enough for our schema; little-endian; no vtable dedup).
    public sealed class FbsBuilderLite
    {
        byte[] _buf; int _space, _minAlign; int[] _vt; int _objStart; int _vecCount;
        public FbsBuilderLite(int initialSize = 1024) { _buf = new byte[Math.Max(256, initialSize)]; _space = _buf.Length; _minAlign = 1; }
        int Offset => _buf.Length - _space;
        void Grow(int size) { var n = _buf.Length; while (n - _space + size > n) n <<= 1; var b = new byte[n]; Buffer.BlockCopy(_buf, _space, b, n - Offset, Offset); _space += n - _buf.Length; _buf = b; }
        void Prep(int align, int size) { if (align > _minAlign) _minAlign = align; var p = ((~(Offset + size) + 1) & (align - 1)); if (_space < p + size) Grow(p + size); Pad(p); }
        void Pad(int size) { while (size-- > 0) _buf[--_space] = 0; }
        public void PutByte(byte v) { Prep(1, 1); _buf[--_space] = v; }
        public void PutBool(bool v) => PutByte((byte)(v ? 1 : 0));
        public void PutShort(short v) { Prep(2, 2); _buf[--_space] = (byte)(v >> 8); _buf[--_space] = (byte)v; }
        public void PutUShort(ushort v) => PutShort(unchecked((short)v));
        public void PutInt(int v) { Prep(4, 4); _buf[--_space] = (byte)(v >> 24); _buf[--_space] = (byte)(v >> 16); _buf[--_space] = (byte)(v >> 8); _buf[--_space] = (byte)v; }
        public void PutUInt(uint v) => PutInt(unchecked((int)v));
        public void PutFloat(float v) { var b = BitConverter.GetBytes(v); Prep(4, 4); _buf[--_space] = b[3]; _buf[--_space] = b[2]; _buf[--_space] = b[1]; _buf[--_space] = b[0]; }
        public void PutOffset(int off) => PutInt(Offset - off + 4);

        public int CreateString(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            Prep(4, bytes.Length + 1);
            for (int i = bytes.Length - 1; i >= 0; --i) _buf[--_space] = bytes[i];
            _buf[--_space] = 0;
            PutInt(bytes.Length);
            return Offset;
        }

        public void StartTable(int fields) { _vt = new int[fields]; _objStart = Offset; }
        public void AddFieldOffset(int f, int off) { if (off == 0) return; Prep(4, 4); PutOffset(off); _vt[f] = Offset; }
        public void AddFieldInt(int f, int v, int d = 0) { if (v == d) return; Prep(4, 4); PutInt(v); _vt[f] = Offset; }
        public void AddFieldUInt(int f, uint v, uint d = 0) { if (v == d) return; Prep(4, 4); PutUInt(v); _vt[f] = Offset; }
        public void AddFieldFloat(int f, float v, float d = 0f) { if (Math.Abs(v - d) < 0f) return; Prep(4, 4); PutFloat(v); _vt[f] = Offset; }
        public void AddFieldByte(int f, byte v, byte d = 0) { if (v == d) return; Prep(1, 1); PutByte(v); _vt[f] = Offset; }
        public void AddFieldBool(int f, bool v, bool d = false) { if (v == d) return; Prep(1, 1); PutBool(v); _vt[f] = Offset; }
        public void AddFieldStruct(int f, Action putStruct) { putStruct(); _vt[f] = Offset; }

        public int EndTable()
        {
            Prep(2, 2); PutUShort(0);
            var objEnd = Offset;
            for (int i = _vt.Length - 1; i >= 0; --i) PutUShort(_vt[i] == 0 ? (ushort)0 : (ushort)(objEnd - _vt[i]));
            var vtsz = (ushort)((_vt.Length + 2) * 2);
            PutUShort(vtsz);
            PutUShort((ushort)(objEnd - _objStart));
            var vtoff = Offset;
            _space = _buf.Length - _objStart;
            PutInt(vtoff - _objStart);
            _space = _buf.Length - vtoff;
            return _objStart;
        }

        public void StartVector(int elemSize, int count, int align) { _vecCount = count; Prep(4, elemSize * count); Prep(align, elemSize * count); }
        public int EndVector() { PutInt(_vecCount); return Offset; }

        public byte[] Finish(int rootTable)
        {
            Prep(_minAlign, 4);
            PutOffset(rootTable);
            var outBytes = new byte[Offset];
            Buffer.BlockCopy(_buf, _space, outBytes, 0, outBytes.Length);
            return outBytes;
        }
    }
}

