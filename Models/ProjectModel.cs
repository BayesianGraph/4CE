using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class ProjectModel
    {
        public string ProjectID { get; set; }
        public string ProjectName {get;set;}
        public int SortOrder { get; set; }
        public byte IsActive { get; set; }
        public string SchemaName { get; set; }
        public string FilePath { get; set; }

    }
}
