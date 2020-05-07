using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Helpers
{
    public static class FileNameManager
    {
        public static string FileName(string filename)
        {
            if (filename.Split("\\").Count() > 1)
                filename = filename.Split("\\")[filename.Split("\\").Count() - 1];

            return filename;

        }
    }
}
