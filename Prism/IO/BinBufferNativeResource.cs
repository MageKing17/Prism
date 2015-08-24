﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prism.IO
{
    public class BinBufferNativeResource : BinBufferResource
    {
        IntPtr address;
        int size = 0, maxSize = 0;
        int setMaxSize = -1;
        bool allocated = false;

        public override int Position
        {
            get;
            set;
        }
        public override bool IsEmpty
        {
            get
            {
                return size == 0;
            }
        }
        public override int Size
        {
            get
            {
                return size;
            }
        }
        public override int BufferSize
        {
            get
            {
                return maxSize = setMaxSize == -1 ? Math.Max(maxSize, size) : setMaxSize;
            }
        }

        public BinBufferNativeResource(IntPtr addr, bool isMallocated = false)
        {
            address = addr;
            allocated = isMallocated;
        }
        public BinBufferNativeResource(IntPtr addr, int size, bool isMallocated = false)
            : this(addr, isMallocated)
        {
            setMaxSize = size;
        }
        public BinBufferNativeResource(int size)
            : this(Marshal.AllocHGlobal(size), size, true)
        {

        }

        public override void Clear(bool wipeData = false)
        {
            if (setMaxSize == -1)
                maxSize = Math.Max(size, maxSize);

            size = Position = 0;
        }

        public override void Write(Union v, int s)
        {
            if (setMaxSize > -1 && Position + s > setMaxSize)
                throw new EndOfStreamException();
            if (Position + s > size)
                size++;

            for (int i = 0; i < s; i++)
                Marshal.WriteByte(address, Position + i, v[i]);

            Position += s;
        }

        public override void WriteByte(byte value)
        {
            if (setMaxSize > -1 && Position + 1 > setMaxSize)
                throw new EndOfStreamException();
            if (Position + 1 > size)
                size++;

            Marshal.WriteByte(address, Position++, value);
        }
        public override void Write(byte[] data, int startIndex, int count)
        {
            if (setMaxSize > -1 && Position + count > setMaxSize)
                throw new EndOfStreamException();
            if (Position + count > size)
                size = Position + count;

            Marshal.Copy(data, startIndex, address + Position, count);
            Position += count;
        }

        public override Union ReadUnion(int s)
        {
            if (setMaxSize > -1 && Position + s > setMaxSize)
                throw new EndOfStreamException();
            if (Position + s > size)
                size = Position + s;

            var u = new Union();

            for (int i = 0; i < s; i++)
                u[i] = Marshal.ReadByte(address, Position + i);

            Position += s;

            return u;
        }

        public override byte ReadByte()
        {
            if (setMaxSize > -1 && Position + 1 > setMaxSize)
                throw new EndOfStreamException();
            if (Position + 1 > size)
                size++;

            return Marshal.ReadByte(address, Position++);
        }
        public override int Read(byte[] data, int startIndex, int count)
        {
            if (setMaxSize > -1 && Position + count > setMaxSize)
                throw new EndOfStreamException();
            if (Position + count > size)
                size = Position + count;

            Marshal.Copy(address + Position, data, startIndex, count);

            Position += count;

            return count;
        }

        public override Stream AsStream()
        {
            throw new NotSupportedException();
        }
        public override byte[] AsByteArray()
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (allocated)
                Marshal.FreeHGlobal(address);
        }
    }
}
