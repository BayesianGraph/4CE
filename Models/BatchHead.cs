using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class BatchHead
    {
        public string PersonsName { get; set; }
        public string Email { get; set; }
        public string SiteID { get; set; }
        public string Comments { get; set; }  
        public string Version { get; set; }

    }
}
