
namespace DtkSymbolDiff
{
    internal struct Options
    {
        public bool includeDataSymbols; 
        public bool useSymbolSizeThreshold; //Allows symbols to still match as long at their sizes are relatively close
        public bool printDifferentSizeSymbols; //Toggles printing a list of symbols that have the same name but a different size

        public Options()
        {

            useSymbolSizeThreshold = false;
            printDifferentSizeSymbols = true;
            includeDataSymbols = true;
        }
    }
}
