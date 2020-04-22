using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class DemographicsModel
    {
        public string siteid { get; set; }
        public string sex { get; set; }
        public int total_patients { get; set; }
        public int age_0to2 { get; set; }
        public int age_3to5 { get; set; }
        public int age_6to11 { get; set; }
        public int age_12to17 { get; set; }
        public int age_18to25 { get; set; }
        public int age_26to49 { get; set; }
        public int age_50to69 { get; set; }
        public int age_70to79 { get; set; }
        public int age_80plus { get; set; }


    }
}
