using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class ResponseModel
    {
        public bool valid { get; set; }
        public List<string> messages { get; set; }
            
    }
}
