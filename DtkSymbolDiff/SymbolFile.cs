using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace DtkSymbolDiff
{
    internal class Section
    {
        public List<Symbol> symbols;
        public string name;

        static string[] codeSectionNames = { ".init", ".text" };

        public Section()
        {
            symbols = new List<Symbol>();
            name = "";
        }

        public bool IsCodeSection()
        {
            return codeSectionNames.Contains(name);
        }
    }


    internal class SymbolFile
    {

        public List<Section> sections;
        public string name;
        public bool loaded = false;
        public bool hasSymbols = false;

        public SymbolFile(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine("Error: could not find file \"{0}\"", path);
                return;
            }

            sections = ReadSymbolFile(path);

            if (sections == null) return;

            if(sections.Count > 0) hasSymbols = true;

            loaded = true;
            name = Path.GetFileName(path);
        }

        public Section GetSection(string name)
        {
            foreach(Section section in sections)
            {
                if (section.name == name) return section;
            }

            return null;
        }

        public bool ContainsSection(string name)
        {
            return GetSection(name) != null;
        }

        List<Section> ReadSymbolFile(string path)
        {
            string[] lines = File.ReadAllLines(path);
            Regex lineRegex = new Regex(@"^(\S+) = ([.\w]+):(0x[0-9A-F]{8});\s*//\s*type:(\S+)(?:.+size:(0x[0-9A-F]+)|).*$");
            
            int curLine = 1;
            string curSectionName = "";
            List<Symbol> symbols = new List<Symbol>();
            List<int> sectionStartIndices = new List<int>();

            for(int i = 0; i < lines.Length; i++) {
                string line = lines[i].Trim();

                //Ignore empty lines and comment lines
                if (line == "" || line.StartsWith("//"))
                {
                    curLine++;
                    continue;
                }

                Match match = lineRegex.Match(line);

                if (match.Success)
                {
                    string name = match.Groups[1].Value;
                    string sectionName = match.Groups[2].Value;
                    uint address = Convert.ToUInt32(match.Groups[3].Value, 16);
                    string type = match.Groups[4].Value;
                    int size = 0;

                    //If this is a new section, add the index to the list.
                    if(sectionName != curSectionName)
                    {
                        int index = symbols.Count;
                        sectionStartIndices.Add(index);
                        curSectionName = sectionName;
                    }

                    /* Size is allowed to be optional, so only parse it if the line has it. Otherwise, the size
                    is just set to 0. */
                    if (match.Groups[5].Success) size = Convert.ToInt32(match.Groups[5].Value, 16);

                    Symbol symbol = new Symbol(name, sectionName, address, size);
                    symbols.Add(symbol);
                }
                else
                {
                    Console.WriteLine("Error: invalid line in file \"{0}\"", path);
                    Console.WriteLine("Line {0}: \"{1}\"", curLine, line);
                    return null;
                }

                curLine++;
            }

            List<Section> sections = new List<Section>();

            int totalSections = sectionStartIndices.Count;

            //Add the symbols to each separate section
            for(int i = 0; i < totalSections; i++)
            {
                Section section = new Section();
                int startIndex = sectionStartIndices[i];
                int endIndex = i == totalSections - 1 ? symbols.Count : sectionStartIndices[i + 1];

                section.name = symbols[startIndex].section;

                for(int j = startIndex; j < endIndex; j++)
                {
                    section.symbols.Add(symbols[j]);
                }

                sections.Add(section);
            }

            return sections;
        }
    }
}
