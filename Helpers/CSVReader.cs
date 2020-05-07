using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Helpers
{
    public static class CSVReader
    {
        public static List<string> ReadFormFile(IFormFile file)
        {
            List<string> filedata = new List<string>();
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                List<string> _filedata = new List<string>();
                string buffer = "";
                while (reader.Peek() >= 0)
                    filedata.Add(reader.ReadLine().Replace("\"",""));
                
                filedata.ForEach(delegate(string s){
                    //rip out any spaces and rebuild the whole row with , as the delimiter
                    foreach (var c in s.Split(","))
                    {
                        buffer += $"{c.Trim()},";
                    }
                    _filedata.Add(buffer.Remove(buffer.Length-1));
                    buffer = "";

                });

                return _filedata;
            }
        }

        public static List<string> ParseLine(string line)
        {
            //strip out all spaces and rebuild list
            List<string> _line = new List<string>();
            line.Split(",").ToList().ForEach(delegate (string s)
            {
                _line.Add(s.Trim());
            });


            return _line;
        }
    }
}
