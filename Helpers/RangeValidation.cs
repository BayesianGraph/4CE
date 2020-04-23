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
            int parsedmax;
            int parsedmin;
            bool maxtest = false;
            bool mintest = false;
            bool rtn = false;

            if (int.TryParse(max, out parsedmax))
            { maxtest = true; }
            if (int.TryParse(min, out parsedmin))
            { mintest = true; }
            
            if (maxtest && mintest)
                if (value >= parsedmin && value <= parsedmax) { rtn = true; }

            if(maxtest && !mintest)
                if (value <= parsedmax) { rtn = true; }

            if (!maxtest && mintest)
                if (value >= parsedmin) { rtn = true; }

            if (!maxtest && !mintest)
                rtn = true;

            return rtn;

        }
        public static bool FloatRanges(float value, string max, string min)
        {
            float parsedmax;
            float parsedmin;
            bool maxtest = false;
            bool mintest = false;
            bool rtn = false;

            if (float.TryParse(max, out parsedmax))
            { maxtest = true; }
            if (float.TryParse(min, out parsedmin))
            { mintest = true; }

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
