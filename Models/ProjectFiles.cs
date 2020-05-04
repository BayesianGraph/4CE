using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class ProjectFiles
    {
        public string ProjectID { get; set; }
        public string FileID { get; set; }
        public int NumColumns { get; set; }
        public int SortOrder { get; set; }
        public string NullCode { get; set; }
        public string MaskCode { get; set; }

    }
}
