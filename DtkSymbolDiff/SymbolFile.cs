using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DtkSymbolDiff
{
    internal class SymbolFile
    {

        public List<Symbol> symbols;
        public string name;
        public bool loaded = false;

        public SymbolFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("Error: could not find file \"{0}\"", path);
                return;
            }

            symbols = ReadSymbolFile(path);

            if (symbols == null) return;
            
            loaded = true;
            name = Path.GetFileName(path);
        }

        public int totalSymbols
        {
            get
            {
                return symbols.Count;
            }
        }


        List<Symbol> ReadSymbolFile(string path)
        {
            StreamReader sr = new StreamReader(path);
            List<Symbol> symbols = new List<Symbol>();
            Regex lineRegex = new Regex(@"^(\S+) = .+:(0x[0-9A-F]{8});\s*//\s*type:(\S+)(?:.+size:(0x[0-9A-F]+)|).*$");

            int curLineNum = 1;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine().Trim();

                //Ignore empty lines and comment lines
                if (line == "" || line.StartsWith("//"))
                {
                    curLineNum++;
                    continue;
                }

                Match match = lineRegex.Match(line);

                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    uint address = Convert.ToUInt32(match.Groups[2].Value, 16);
                    int size = 0;
                    string type = match.Groups[3].Value;
                    bool isDataType = type != "function";

                    /* Size is allowed to be optional, so only parse it if the line has it. Otherwise, the size
                    is just set to 0. */
                    if (match.Groups[4].Success) size = Convert.ToInt32(match.Groups[4].Value, 16);

                    Symbol symbol = new Symbol(name, address, size, isDataType);
                    symbols.Add(symbol);
                }
                else
                {
                    Console.WriteLine("Error: invalid line in file \"{0}\"", path);
                    Console.WriteLine("Line {0}: \"{1}\"", curLineNum, line);
                    return null;
                }

                curLineNum++;
            }

            return symbols;
        }
    }
}
