using Microsoft.AspNetCore.Http;

namespace i2b2_csv_loader.Models
{
    public class Files
    {        
        public IFormFile File { get; set; }
        public FileProperties FileProperties { get; set; }        
    }
    public class FileProperties
    {
        public string version { get; set; }
        public string file_name { get; set; }
        public string file_type { get; set; }
        public string file_size { get; set; }
        public string site_id { get; set; }        

    }
}
