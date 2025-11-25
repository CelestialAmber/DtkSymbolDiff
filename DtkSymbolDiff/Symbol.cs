using System;

namespace DtkSymbolDiff
{
    internal class Symbol
    {
        public string name;
        public uint address;
        public int size;
        public bool isDataSymbol;

        public Symbol(string name, uint address, int size, bool isDataSymbol)
        {
            this.name = name;
            this.address = address;
            this.size = size;
            this.isDataSymbol = isDataSymbol;
        }

        public float CalculateSizeSimilarity(Symbol other)
        {
            return Math.Min(size, other.size) / Math.Max(size, other.size);
        }

        public new string ToString()
        {
            return string.Format("{0} ({1}, size 0x{2:X})", name, size);
        }
    }
}
