using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Models
{
    public class DailyCountsModel
    {
        public string siteid { get; set; }
        public DateTime date { get; set; }
        public int new_positive_cases { get; set; }
        public int patients_in_icu { get; set; }
        public int new_deaths { get; set; }

    }
}
