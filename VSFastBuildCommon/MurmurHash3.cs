namespace MurmurHash
{
    public static class MurmurHash3
    {
        public const ulong C1 = 0x87c37b91114253d5UL;
        public const ulong C2 = 0x4cf5ad432745937fUL;
        public const int BufferSize = 16;

        public class State
        {
            public ulong h1_;
            public ulong h2_;
            public long length_;
            public int remaining_;
            public byte[] buffer_ = new byte[BufferSize];
        }

        public static byte[] ComputeHash(byte[] buffer)
        {
            State state = new State();
            Update(state, buffer);
            return Finalize(state);
        }

        public static byte[] ComputeHash(byte[] buffer, long offset, long length)
        {
            State state = new State();
            Update(state, buffer, offset, length);
            return Finalize(state);
        }

        public static void Update(State state, byte[] buffer)
        {
            Update(state, buffer, 0, buffer.LongLength);
        }

        public static void Update(State state, byte[] buffer, long offset, long length)
        {
            System.Diagnostics.Debug.Assert(buffer != null);
            ulong h1 = state.h1_;
            ulong h2 = state.h2_;
            ulong k1;
            ulong k2;
            long o = offset;
            long bo = state.remaining_;
            long end = offset + length;
            while (o < end) {
                long l = end-o;
                long r = BufferSize - state.remaining_;
                if (l < r)
                {
                    System.Array.Copy(buffer, o, state.buffer_, state.remaining_, l);
                    state.remaining_ += (int)l;
                    o += l;
                    System.Diagnostics.Debug.Assert(state.remaining_<BufferSize);
                    break;
                }
                System.Array.Copy(buffer, o, state.buffer_, state.remaining_, r);
                state.remaining_ += (int)r;
                o += r;
                System.Diagnostics.Debug.Assert(BufferSize==state.remaining_);
                k1 = ToUlong(state.buffer_, 0);
                k2 = ToUlong(state.buffer_, 8);
                k1 *= C1;
                k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                k1 *= C2;
                h1 ^= k1;

                h1 = (h1 << 27) | (h1 >> (64 - 27)); // ROTL64(h1, 27);
                h1 += h2;
                h1 = h1 * 5 + 0x52dce729;

                k2 *= C2;
                k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                k2 *= C1;
                h2 ^= k2;

                h2 = (h2 << 31) | (h2 >> (64 - 31)); // ROTL64(h2, 31);
                h2 += h1;
                h2 = h2 * 5 + 0x38495ab5;
                state.remaining_ = 0;
            }
            state.h1_ = h1;
            state.h2_ = h2;
            state.length_ += length;
        }

        public static ulong ToUlong(byte[] buffer, int offset)
        {
            return ((ulong)buffer[offset + 0] << 0) |
                   ((ulong)buffer[offset + 1] << 8) |
                   ((ulong)buffer[offset + 2] << 16) |
                   ((ulong)buffer[offset + 3] << 24) |
                   ((ulong)buffer[offset + 4] << 32) |
                   ((ulong)buffer[offset + 5] << 40) |
                   ((ulong)buffer[offset + 6] << 48) |
                   ((ulong)buffer[offset + 7] << 56);
        }

        public static byte[] ComputeHash(System.IO.Stream stream)
        {
            State state = new State();
            state = Update(state, stream);
            return Finalize(state);
        }

        public static State Update(State state, System.IO.Stream stream)
        {
            System.Diagnostics.Debug.Assert(stream != null);
            System.Diagnostics.Debug.Assert(state.remaining_ < BufferSize);
            ulong h1 = state.h1_;
            ulong h2 = state.h2_;
            long length = 0;
            for (; ; )
            {
                int l = BufferSize - (int)state.remaining_;
                int readSize = stream.Read(state.buffer_, state.remaining_, l);
                state.remaining_ += readSize;
                length += readSize;
                if (BufferSize <= state.remaining_)
                {
                    ulong k1 = ToUlong(state.buffer_, 0);
                    ulong k2 = ToUlong(state.buffer_, 8);

                    k1 *= C1;
                    k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                    k1 *= C2;
                    h1 ^= k1;

                    h1 = (h1 << 27) | (h1 >> (64 - 27)); // ROTL64(h1, 27);
                    h1 += h2;
                    h1 = h1 * 5 + 0x52dce729;

                    k2 *= C2;
                    k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                    k2 *= C1;
                    h2 ^= k2;

                    h2 = (h2 << 31) | (h2 >> (64 - 31)); // ROTL64(h2, 31);
                    h2 += h1;
                    h2 = h2 * 5 + 0x38495ab5;
                    state.remaining_ = 0;
                }
                else
                {
                    System.Diagnostics.Debug.Assert(state.remaining_ < BufferSize);
                    break;
                }
            }

            state.h1_ = h1;
            state.h2_ = h2;
            state.length_ += length;
            return state;
        }

        public static byte[] Finalize(State state)
        {
            ulong h1 = state.h1_;
            ulong h2 = state.h2_;

            if (0 < state.remaining_) {
                ulong k1 = 0;
                ulong k2 = 0;
                switch (state.remaining_)
                {
                    case 15:
                        k2 ^= (ulong)state.buffer_[14] << 48;
                        goto case 14;
                    case 14:
                        k2 ^= (ulong)state.buffer_[13] << 40;
                        goto case 13;
                    case 13:
                        k2 ^= (ulong)state.buffer_[12] << 32;
                        goto case 12;
                    case 12:
                        k2 ^= (ulong)state.buffer_[11] << 24;
                        goto case 11;
                    case 11:
                        k2 ^= (ulong)state.buffer_[10] << 16;
                        goto case 10;
                    case 10:
                        k2 ^= (ulong)state.buffer_[9] << 8;
                        goto case 9;
                    case 9:
                        k2 ^= state.buffer_[8];
                        k2 *= C2;
                        k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                        k2 *= C1;
                        h2 ^= k2;
                        goto case 8;
                    case 8:
                        k1 ^= (ulong)state.buffer_[7] << 56;
                        goto case 7;
                    case 7:
                        k1 ^= (ulong)state.buffer_[6] << 48;
                        goto case 6;
                    case 6:
                        k1 ^= (ulong)state.buffer_[5] << 40;
                        goto case 5;
                    case 5:
                        k1 ^= (ulong)state.buffer_[4] << 32;
                        goto case 4;
                    case 4:
                        k1 ^= (ulong)state.buffer_[3] << 24;
                        goto case 3;
                    case 3:
                        k1 ^= (ulong)state.buffer_[2] << 16;
                        goto case 2;
                    case 2:
                        k1 ^= (ulong)state.buffer_[1] << 8;
                        goto case 1;
                    case 1:
                        k1 ^= state.buffer_[0];
                        k1 *= C1;
                        k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                        k1 *= C2;
                        h1 ^= k1;
                        break;
                }
            }

            // finalization
            h1 ^= (ulong)state.length_;
            h2 ^= (ulong)state.length_;

            h1 += h2;
            h2 += h1;

            h1 = FMix64(h1);
            h2 = FMix64(h2);

            h1 += h2;
            h2 += h1;

            byte[] ret = new byte[16];
            Reverse(ret, 0, h1);
            Reverse(ret, 8, h2);
            return ret;
        }

        private static ulong FMix64(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccd;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53;
            k ^= k >> 33;
            return k;
        }

        private static void Reverse(byte[] bytes, int offset, ulong value)
        {
            bytes[offset + 0] = (byte)((value & 0xFF00000000000000UL) >> 56);
            bytes[offset + 1] = (byte)((value & 0x00FF000000000000UL) >> 48);
            bytes[offset + 2] = (byte)((value & 0x0000FF0000000000UL) >> 40);
            bytes[offset + 3] = (byte)((value & 0x000000FF00000000UL) >> 32);
            bytes[offset + 4] = (byte)((value & 0x00000000FF000000UL) >> 24);
            bytes[offset + 5] = (byte)((value & 0x0000000000FF0000UL) >> 16);
            bytes[offset + 6] = (byte)((value & 0x000000000000FF00UL) >> 8);
            bytes[offset + 7] = (byte)((value & 0x00000000000000FFUL) >> 0);
        }

        private static ulong Reverse(ulong value)
        {
            return (value & 0x00000000000000FFUL) << 56 | (value & 0x000000000000FF00UL) << 40 |
                    (value & 0x0000000000FF0000UL) << 24 | (value & 0x00000000FF000000UL) << 8 |
                    (value & 0x000000FF00000000UL) >> 8 | (value & 0x0000FF0000000000UL) >> 24 |
                    (value & 0x00FF000000000000UL) >> 40 | (value & 0xFF00000000000000UL) >> 56;
        }
    }
}
