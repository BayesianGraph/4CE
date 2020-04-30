using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Helpers
{
    public static class RangeValidation
    {


        public static bool IntRanges(int value, string max, string min)
        {

            int parsedmax = 0;
            int parsedmin = 0;
            bool maxtest = false;
            bool mintest = false;
            bool rtn = false;
            try
            {
                if (int.TryParse(max, out parsedmax))
                { maxtest = true; }
                if (int.TryParse(min, out parsedmin))
                { mintest = true; }
            }
            catch { }
            if (maxtest && mintest)
                if (value >= parsedmin && value <= parsedmax) { rtn = true; }

            if (maxtest && !mintest)
                if (value <= parsedmax) { rtn = true; }

            if (!maxtest && mintest)
                if (value >= parsedmin) { rtn = true; }

            if (!maxtest && !mintest)
                rtn = true;

            return rtn;

        }
        public static bool FloatRanges(float value, string max, string min)
        {
            float parsedmax = 0;
            float parsedmin = 0;
            bool maxtest = false;
            bool mintest = false;
            bool rtn = false;
            try
            {
                if (float.TryParse(max, out parsedmax))
                { maxtest = true; }
                if (float.TryParse(min, out parsedmin))
                { mintest = true; }
            }
            catch { }

            if (maxtest && mintest)
                if (value >= parsedmin && value <= parsedmax) { rtn = true; }

            if (maxtest && !mintest)
                if (value <= parsedmax) { rtn = true; }

            if (!maxtest && mintest)
                if (value >= parsedmin) { rtn = true; }

            if (!maxtest && !mintest)
                rtn = true;

            return rtn;

        }

    }
}
