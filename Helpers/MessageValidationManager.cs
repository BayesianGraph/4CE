using i2b2_csv_loader.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace i2b2_csv_loader.Helpers
{
    public static class MessageValidationManager
    {
        public static void Check(ref List<ValidateDataModel> messages, string msg)
        {
            if (!messages.Exists(x => x.error == msg))
                messages.Add(new ValidateDataModel { error = msg });               
        }
    }
}
