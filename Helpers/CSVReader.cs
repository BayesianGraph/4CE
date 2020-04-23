using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                while (reader.Peek() >= 0)
                    filedata.Add(reader.ReadLine());
                
                return  filedata;
            }
        }
    }
}
