using System;
using System.IO;

namespace DtkSymbolDiff
{

    internal class Program
    {

        static void Main(string[] args)
        {
            if(args.Length < 2)
            {
                PrintHelp();
                return;
            }

            Options options = new Options();

            string outputPath = "out.txt";

            //Parse options
            for(int i = 2; i < args.Length; i++)
            {
                string option = args[i];

                switch (option) {
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length)
                        {
                            outputPath = args[i + 1];
                            i++;
                        }
                        else
                        {
                            Console.WriteLine("Error: no output path given");
                            return;
                        }
                        break;
                    case "--threshold":
                    case "-t":
                        options.useSymbolSizeThreshold = true;
                        break;
                    case "--nodatasymbols":
                    case "-nd":
                        options.includeDataSymbols = false;
                        break;
                    case "--help":
                    case "-h":
                        PrintHelp();
                        return;
                    default:
                        Console.WriteLine("Invalid option \"{0}\"", option);
                        return;
                }
            }

            //Read the symbol files
            SymbolFileDiffer differ = new SymbolFileDiffer(args[0], args[1], options);

            //File reading/parsing failed, return
            if(!differ.filesLoaded) return;

            //Return if one of the lists is empty
            if (!differ.file1.hasSymbols || !differ.file2.hasSymbols)
            {
                Console.WriteLine("One of the symbol files is empty");
                return;
            }

            Console.WriteLine("Comparing symbol files");

            //Diff the two symbol files
            differ.DiffFiles();

            Console.WriteLine("Finished!");


            //Write matches to the output file
            StreamWriter sw = new StreamWriter(outputPath, false);
            differ.sw = sw;
            differ.PrintDiff();
        }

        static void PrintHelp()
        {
            Console.WriteLine("Usage: DtkSymbolDiff <symbol file 1> <symbol file 2> [options]");
            Console.WriteLine("Options:");
            Console.WriteLine("--output/-o <path>: Specify output path");
            Console.WriteLine("--threshold/-t: Allow symbols to still match if the sizes are within a threshold");
            Console.WriteLine("--nodatasymbols/-nd: Don't include data symbols in the diff");
            Console.WriteLine("--help/-h: Print this message");
        }
   
    }
}
