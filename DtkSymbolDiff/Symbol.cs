using System;

namespace DtkSymbolDiff
{
    internal struct Symbol
    {
        public string name;
        public string section;
        public uint address;
        public int size;
        public bool isAuto;

        public Symbol(string name, string section, uint address, int size)
        {
            this.name = name;
            this.section = section;
            this.address = address;
            this.size = size;

            //Precalculate for efficiency
            isAuto = IsAutoSymbol();
        }

        static string[] autoPrefixes = {
            "fn_",
            "dtor_",
            "lbl_",
            "jumptable_",
            "gap_",
            "pad"
        };

        public bool IsAutoSymbol()
        {
            foreach (string prefix in autoPrefixes)
            {
                if (name.StartsWith(prefix)) return true;
            }

            return false;
        }

        public float CalculateSizeSimilarity(Symbol other)
        {
            return (float)Math.Min(size, other.size) / (float)Math.Max(size, other.size);
        }

        public new string ToString()
        {
            return string.Format("{0} (0x{1:X8}, size 0x{2:X})", name, address, size);
        }
    }
}
