using System;
using System.IO;

namespace TLS
{
    public class PackedStreamDataEventArgs : EventArgs
    {
        private readonly MemoryStream _data;

        public MemoryStream MemoryStream => _data;

        public PackedStreamDataEventArgs(MemoryStream data)
        {
            _data = data;
        }
    }
}
