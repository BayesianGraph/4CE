using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class DiagnosesModel
    {
        public string siteid { get; set; }
        public string icd_code { get; set; }
        public int icd_version { get; set; }
        public int num_patients { get; set; }


    }
}
