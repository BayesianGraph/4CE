using Microsoft.AspNetCore.Http;
using System.Collections.Generic;

namespace i2b2_csv_loader.Models
{
    public class Files
    {        
        public IFormFile File { get; set; }
        public List<FileProperties> FileProperties { get; set; }        
    }
    public class FileProperties
    {
        public string ProjectID { get; set; }
        public string FileID { get; set; }
        public string SortOrder { get; set; }
        public string ColumnName { get; set; }
        public string DataType { get; set; }
        public string MinValue { get; set; }
        public string MaxValue { get; set; }
        public string CodeType { get; set; }
        public string ValueList { get; set; }

    }
}
