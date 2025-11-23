using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;

namespace DtkSymbolDiff
{

    internal class Program
    {

        static void Main(string[] args)
        {
            if(args.Length < 3)
            {
                Console.WriteLine("Usage: DtkSymbolDiff <symbol file 1> <symbol file 2> <output filename>");
                return;
            }

            //Read the symbol files
            SymbolFileDiffer differ = new SymbolFileDiffer(args[0], args[1]);

            //File reading/parsing failed, return
            if(!differ.filesLoaded) return;

            //Return if one of the lists is empty
            if (differ.symbolList1.Count == 0 || differ.symbolList2.Count == 0)
            {
                Console.WriteLine("One of the symbol files is empty");
                return;
            }

            Console.WriteLine("Comparing symbol files");

            //Diff the two symbol files
            differ.DiffFiles();

            Console.WriteLine("Finished!");


            //Write matches to the output file
            StreamWriter sw = new StreamWriter(args[2], false);
            differ.PrintMatches(sw);
            sw.Flush();
        }
   
    }
}
