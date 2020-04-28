using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Helpers
{
    public static class DateValidation
    {
        public static bool IsValidDate(string value)
        {
            DateTime tempDate;

            
            bool validDate = DateTime.TryParseExact(value, "yyyy-mm-dd", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.None, out tempDate);
            if (validDate)
                return true;
            else
                return false;
        }

        public static bool DateRange(string date, string max, string min)
        {
            DateTime minDate = (min==null ? DateTime.Now.AddYears(-1000): DateTime.Parse(min));
            DateTime maxDate = (max==null ? DateTime.Now.AddYears(1000):DateTime.Parse(max));
            DateTime dt;

            return (DateTime.TryParse(date, out dt)
                            && dt <= maxDate
                            && dt >= minDate);
        }
    }
}
