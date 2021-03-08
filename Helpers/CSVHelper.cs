using System.IO;
using System.Collections.Generic;



using i2b2_csv_loader.Models;
using Microsoft.AspNetCore.Http;

using Microsoft.Extensions.Configuration;
using CsvHelper;
using System;
using System.Collections;
using System.Linq;

namespace i2b2_csv_loader.Helpers
{
    public static class CSVHelper
    {
        public static List<string[]> ReadFormFile(IFormFile file)
        {

            bool loop = true;
            List<string[]> records = new List<string[]>();
            using (TextReader reader = new StreamReader(file.OpenReadStream()))
            using (var pcsv = new CsvParser(reader, GetConfig()))
            {
                while (loop)
                {
                    pcsv.Read();
                    string[] row = pcsv.Record;
                    if (row == null) { loop = false; }
                    else
                        records.Add(row);
                }
            }



            return records;
        }


        public static List<string> ParseLine(CsvHelper.IReaderRow line, List<FileProperties> properties)
        {
            //strip out all spaces and rebuild list
            List<string> _line = new List<string>();

            foreach (var f in properties)
            {
                _line.Add(line.GetField(f.ColumnName));
            }


            return _line;
        }


        private static CsvHelper.Configuration.CsvConfiguration GetConfig()
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.CurrentCulture)
            {
                HasHeaderRecord = false,
                AllowComments = false,
                Delimiter = ","

            };
            return config;
        }

    }
}
