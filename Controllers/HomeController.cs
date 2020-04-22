using System.IO;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using i2b2_csv_loader.Models;
using Microsoft.AspNetCore.Http;
using CsvHelper;
using System.Linq;
using i2b2_csv_loader.Helpers;
using Dapper;
using System.Data;
using System.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System;
using Dropbox.Api.Files;
using System.Threading.Tasks;
using Dropbox.Api;
using System.Text;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace i2b2_csv_loader.Controllers
{

    public class HomeController : Controller
    {
        private List<Models.Files> _files = new List<Models.Files>();

        private readonly IConfiguration _configuration;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        // GET: Home

        public ActionResult Index()
        {

            ViewBag.Projects = new SelectList(GetProjects(),"ProjectID","ProjectName"); 
            //ViewBag.Files = 

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadFilesAsync()
        {
            var files = Request.Form.Files;
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };
            BatchHead form = new BatchHead();

            try
            {
                form = JsonSerializer.Deserialize<BatchHead>(Request.Form["batchHeader"].ToString());
            }
            catch (JsonException)
            {
                // throw ex;

            }

            //validate each file, store the physical files at the end after all have passed validation
            string file_msg = "";
            foreach (var file in files)
            {
                file_msg = ValidateFile(file, form.SiteID);
                if (file_msg != "")
                {
                    rm.messages.Add(file_msg);
                    file_msg = "";
                }
            }

            if (rm.messages.Count == 0)
            {
                form.Version = this.AddInstance(form);
                if (form.Version.All(char.IsDigit))
                {
                    long uploadsize = 0;
                    foreach (Models.Files file in _files)
                    {
                        file.FileProperties.site_id = form.SiteID;
                        file.FileProperties.version = form.Version;
                        uploadsize = await BurnFileAsync(file); //Writes it to disk/dropbox/google docs.
                        rm.messages.Add(file.FileProperties.file_name);
                        if (!this.SaveFile(file.FileProperties)) //writes it to DB
                        {
                            rm.messages.Add($"{file.FileProperties.file_name} was not saved to the database.");
                        }

                    }

                    string message = $"{files.Count} file(s) /{uploadsize} bytes uploaded successfully!";
                    rm.messages.Add(message);
                    rm.valid = true;
                }
                else
                {
                    rm.messages.Add("Site ID Version is not valid.");
                }
            }

            return Json(rm);

        }
        [HttpPost]
        public IActionResult ValidateFile()
        {
            var file = Request.Form.Files[0];
            string file_validation = "";
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };

            file_validation = ValidateFile(file, "");
            if (file_validation == "")
            {
                rm.valid = true;
                rm.size = file.Length;
                return Json(rm);
            }

            file_validation = file_validation.Replace("You can ignore missing fields by setting MissingFieldFound to null.", "");

            rm.valid = false;
            rm.size = 0;
            rm.messages.Add(file_validation);
            return Json(rm);

        }
        [HttpPost]
        public IActionResult ValidateBatchHeader([FromBody] BatchHead batch)
        {
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };

            if (batch.PersonsName.Trim() == "")
                rm.messages.Add("Name field required.");

            if (!RegexUtilities.IsValidEmail(batch.Email))
                rm.messages.Add("Email is not valid.");

            if (batch.SiteID.Trim() == "")
                rm.messages.Add("SiteID field required.");

            rm.valid = (rm.messages.Count == 0 ? true : false);


            return Json(rm);
        }
        [HttpPost]
        [Route("Home/GetFileIDs/{projectid?}")]
        public IActionResult GetFileIDs(string projectid)
        {

            return Json(GetProjectFiles(projectid));

        }
        private string ValidateFile(IFormFile file, string siteid)
        {
            Models.Files files = new Files
            {
                File = file,
                FileProperties = new FileProperties()
            };
            files.FileProperties.file_name = file.Name;
            files.FileProperties.file_size = file.Length.ToString();

            string message;
            switch (file.Name.Split('-')[0].ToLower())
            {
                case "diagnoses":
                    message = ValidateFileTypeFormat<DiagnosesModel>(file, 4, siteid);

                    if (message == "")
                    {
                        files.FileProperties.file_type = "Diagnoses";
                        _files.Add(files);
                    }
                    break;
                case "dailycounts":
                    message = ValidateFileTypeFormat<DailyCountsModel>(file, 5, siteid);
                    if (message == "")
                    {
                        files.FileProperties.file_type = "DailyCount";
                        _files.Add(files);
                    }
                    break;
                case "demographics":
                    message = ValidateFileTypeFormat<DemographicsModel>(file, 12, siteid);
                    if (message == "")
                    {
                        files.FileProperties.file_type = "Demographics";
                        _files.Add(files);
                    }
                    break;
                case "labs":
                    message = ValidateFileTypeFormat<LabsModel>(file, 6, siteid);
                    if (message == "")
                    {
                        files.FileProperties.file_type = "Labs";
                        _files.Add(files);
                    }
                    break;
                default:
                    message = $"{file.Name} is not a reconized file type. <br/>Valid CSV file types are: <ul><li>Diagnoses</li><li>DailyCount</li><li>Demographics</li><li>Labs</li></ul>";
                    break;

            }


            return message;

        }

        //DropBox
        private async Task<long> BurnFileAsync(Models.Files file)
        {  

        
            DateTime dt = DateTime.Now;
            string folder_date = $"{dt.Month}-{dt.Day}-{dt.Year}";

            long size = 0;            

            using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            {
                //var full = await dbx.Users.GetCurrentAccountAsync();
                //Console.WriteLine("{0} - {1}", full.Name.DisplayName, full.Email);

                string directory = $"/uploadedfiles/{file.FileProperties.site_id}";
                string directory_sub = $"/{file.FileProperties.site_id}_{Convert.ToInt32(file.FileProperties.version):D6}_{folder_date}";

                try
                {
                    await CreateFolder(dbx, directory);
                    await CreateFolder(dbx, directory + directory_sub);
                    MemoryStream stream = new MemoryStream();
                    await file.File.CopyToAsync(stream);
                    await Upload(dbx, directory + directory_sub, file.FileProperties.file_name, stream);
                }
                catch (ApiException<Dropbox.Api.Files.GetMetadataError> e)
                {
                    if (e.ErrorResponse.IsPath && e.ErrorResponse.AsPath.Value.IsNotFound)
                    {
                        Console.WriteLine("Nothing found at path.");
                    }
                    else
                    {
                        // different issue; handle as desired
                        Console.WriteLine(e);
                    }
                }

            }
            
            return size;
        }        
        private static async Task<FolderMetadata> CreateFolder(DropboxClient client, string path)
        {
            Console.WriteLine("--- Creating Folder ---");
            var folderArg = new CreateFolderArg(path, false);
            if (!FolderExists(client, path))
            {
                var folder = await client.Files.CreateFolderV2Async(folderArg);
                Console.WriteLine("Folder: " + path + " created!");
                return folder.Metadata;
            }
            else
                return new FolderMetadata();  //just a blank one.
        }
        private static bool FolderExists(DropboxClient cl, string path)
        {
            try
            {

                var folders = cl.Files.ListFolderAsync(path);
                var result = folders.Result;
                return true;
            }
            catch
            {
                return false;
            }
        }
        async Task Upload(DropboxClient dbx, string folder, string file, MemoryStream mem)
        {

            var updated = await dbx.Files.UploadAsync(
                folder + "/" + file,
                WriteMode.Overwrite.Instance,
                body: mem);
            Console.WriteLine("Saved {0}/{1} rev {2}", folder, file, updated.Rev);

        }



        
        private string CheckColsExist(CsvReader csv, int cols)
        {
            var message = "";
            for (int i = 0; i < cols; i++)
            {
                try
                {
                    if (csv[i].Length > 0) { }
                }
                catch (CsvHelperException ex)
                { message = $"field missing, expected {cols} fields total. {ex.Message.Replace("You can ignore missing fields by setting MissingFieldFound to null.", "")}."; }

            }
            return message;
        }
     
        
        
        //CSV Helper Lib
        private CsvHelper.Configuration.CsvConfiguration GetConfig()
        {
            var config = new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.CurrentCulture)
            {
                HasHeaderRecord = false,
                AllowComments = false,
                Delimiter = ","

            };
            return config;
        }
        private string ValidateFileTypeFormat<T>(IFormFile file, int fieldcount, string siteid)
        {
            var config = GetConfig();
            List<T> result = new List<T>();
            string message = "";
            using (TextReader reader = new StreamReader(file.OpenReadStream()))
            {
                var csv = new CsvReader(reader, config);

                while (csv.Read())
                {
                    message = CheckColsExist(csv, fieldcount);

                    if (message != "") return message;
                    try
                    {
                        if ((csv[0].ToString().ToLower() == siteid.ToLower()) || siteid == "")
                        {
                            var row = csv.GetRecord<T>();

                            result.Add(row);
                        }
                        else { return $"Site ID col does not map to {siteid}."; }
                    }
                    catch (CsvHelper.ReaderException e)
                    {
                        return ProcessValidationMessage(file.Name, e);
                    }
                }
            }


            return "";
        }


        private string ProcessValidationMessage(string filename, CsvHelperException e)
        {
            var message = "";
            if (e.InnerException != null)
                message = $"{e.InnerException.Message}";
            else
            {
                message = $"{e.Message.Replace("MemberType", "Expects ")}";
                message = (message.Contains("System.Int32") ? message.Replace("System.Int32", "Number Value") : message);

            }

            return $"{filename} failed because of {message.Replace("\r\n", "</br>")}";

        }

        //DataIO
        public string AddInstance(Models.BatchHead head)
        {
            string version = "";
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@site_id", head.SiteID, dbType: DbType.String);
                    p.Add("@person_name", head.PersonsName, dbType: DbType.String);
                    p.Add("@email", head.Email, dbType: DbType.String);
                    p.Add("@comments", head.Comments, dbType: DbType.String);

                    version = db.Query<string>("sp_add_instance", p, commandType: CommandType.StoredProcedure).Single().ToString();

                }
            }
            catch (SqlException ex)
            {
                return ex.Message;
            }

            return version;
        }
        public bool SaveFile(Models.FileProperties file)
        {
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@site_id", file.site_id, dbType: DbType.String);
                    p.Add("@file_name", file.file_name, dbType: DbType.String);
                    p.Add("@file_type", file.file_type, dbType: DbType.String);
                    p.Add("@file_size", file.file_size, dbType: DbType.String);
                    p.Add("@version", file.version, dbType: DbType.String);
                    db.Execute("sp_add_file", p, commandType: CommandType.StoredProcedure);

                }
            }
            catch (SqlException)
            {
                return false;
            }
            return true;
        }
        public List<ProjectModel> GetProjects()
        {
            List<ProjectModel> pm = new List<ProjectModel>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();                    
                   

                    pm = db.Query<ProjectModel>("dbo.uspGetProjects",null, commandType: CommandType.StoredProcedure).ToList();
                    
                }
            }
            catch (SqlException)
            {
                return pm;
            }
            return pm;
        }

        public List<ProjectFiles> GetProjectFiles(string projectid)
        {
            List<ProjectFiles> fns = new List<ProjectFiles>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();                    
                    p.Add("@ProjectID",projectid, dbType: DbType.String);                    
                    fns = db.Query<ProjectFiles>("dbo.uspGetProjectFiles", p, commandType: CommandType.StoredProcedure).ToList();
                }
            }
            catch (SqlException)
            {
                return fns;
            }
            return fns;

        }
    }
}