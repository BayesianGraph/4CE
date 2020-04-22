using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class LabsModel
    {
        public string siteid { get; set; }
        public string loinc { get; set; }
        public int days_since_positive { get; set; }
        public int num_patients { get; set; }
        public float mean_value { get; set; }
        public float stdev_value { get; set; }

    }
}
