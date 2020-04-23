using System.IO;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using i2b2_csv_loader.Models;
using Microsoft.AspNetCore.Http;
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
using System.Globalization;

namespace i2b2_csv_loader.Controllers
{

    public class HomeController : Controller
    {
        private List<Models.Files> _files = new List<Models.Files>();
        private List<Models.ProjectFiles> _projectfiles = new List<ProjectFiles>();
        private readonly IConfiguration _configuration;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }
        // GET: Home

        public ActionResult Index()
        {

            ViewBag.Projects = new SelectList(GetProjects(), "ProjectID", "ProjectName");
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
            if (!((ResponseModel)((Microsoft.AspNetCore.Mvc.JsonResult)ValidateForm(form)).Value).valid)
            {
                return Json((ResponseModel)ValidateForm(form));
            }



            //validate each file, store the physical files at the end after all have passed validation
            string file_msg = "";
            foreach (var file in files)
            {
                rm = ValidateFile(file, form);
                if (file_msg != "")
                {
                    rm.messages.Add(file_msg);
                    file_msg = "";
                }
            }







            //if (rm.messages.Count == 0)
            //{


            //    {
            //        long uploadsize = 0;
            //        foreach (Models.Files file in _files)
            //        {

            //            uploadsize = await BurnFileAsync(file); //Writes it to disk/dropbox/google docs.

            //            //if (!this.SaveFile(file.FileProperties)) //writes it to DB
            //            //{
            //            //   // rm.messages.Add($"{file.FileProperties.file_name} was not saved to the database.");
            //            //}

            //        }

            //        string message = $"{files.Count} file(s) /{uploadsize} bytes uploaded successfully!";
            //        rm.messages.Add(message);
            //        rm.valid = true;
            //    }
            //    else
            //    {
            //        rm.messages.Add("Site ID Version is not valid.");
            //    }
            //}

            return Json(rm);

        }

        [HttpPost]
        public IActionResult ValidateBatchHeader([FromBody] BatchHead batch)
        {
            return ValidateForm(batch);
        }
        [HttpPost]
        [Route("Home/GetFileIDs/{projectid?}")]
        public IActionResult GetFileIDs(string projectid)
        {

            return Json(GetProjectFiles(projectid));

        }

        //Validation 
        private IActionResult ValidateForm(BatchHead batch)
        {
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };

            if (batch.PersonsName.Trim() == "")
                rm.messages.Add("Name field required.");

            if (!RegexUtilities.IsValidEmail(batch.Email))
                rm.messages.Add("Email is not valid.");

            if (batch.SiteID.Trim() == "")
                rm.messages.Add("SiteID field required.");

            if (batch.ProjectID.Trim() == "")
                rm.messages.Add("Please select project from dropdown.");

            rm.valid = (rm.messages.Count == 0 ? true : false);


            return Json(rm);
        }
        private ResponseModel ValidateFile(IFormFile datafile, BatchHead form)
        {
            Models.Files file = new Files
            {
                File = datafile,
                FileProperties = new List<FileProperties>()
            };
            _projectfiles = GetProjectFiles(form.ProjectID);

            string message = "";

            string filename = datafile.Name.Split("-")[0].ToLower();

            //Check that the name of the file exists in a given project
            if (!_projectfiles.Exists(f => f.FileID.ToLower() == filename))
            {
                message = $"{datafile.Name} has the incorrect name. It must start with one of the following words: {ConvertToFileString(_projectfiles)}";
            }


            file.FileProperties = GetFileProperties(form.ProjectID, filename);

            //Raw text data in lists of srings
            List<string> data = CSVReader.ReadFormFile(datafile);
            List<string> colheaders = data[0].Split(",").ToList();
            //remove the col headers from the data;
            data.Remove(data[0]);

            //check the number of cols are a match with what comes back from the DB file properties
            if (!(colheaders.Count() == file.FileProperties.Count()))
            {
                message = $"{datafile.Name} contains {data[0].Split(",").Count()} columns, but {file.FileProperties.Count()} where expected.";
            }
            //check the col names match with what comes back from teh DB file properties
            foreach (string col in colheaders)
            {
                if (!file.FileProperties.Exists(x => x.ColumnName.ToLower() == col.ToLower()))
                {
                    message = $"{datafile.Name} contains incorrect column headers. They must be {data[0]}.";
                }

            }

            

            //check siteid in file with what was provided in the form post to the API
            foreach (var s in data)
            {                //first col of every line should be the siteid
                if (!(s.Split(",")[0].ToLower() == form.SiteID.ToLower()))
                {
                    message = $"The siteid values in {datafile.Name} do not match the Siteid in the form.";
                }

                var row = s.Split(",").ToList();
                int colcnt = 0;
                FileProperties fcp;
                foreach (string c in row)
                {
                    //every property of a column from the database
                    fcp = file.FileProperties.Find(x => x.ColumnName == colheaders[colcnt]);

                    //validate no nulls
                    if (c.Trim() == "" || c.Trim().ToLower() == "(null)" || c.Trim().ToLower() == "null" || c.Trim().ToLower() == "na" || c.Trim().ToLower() == "n/a" || c.Trim().ToLower() == "n.a.")
                        message = $"{datafile.Name} contains missing or null values in column {colheaders[colcnt]}. Use -2 to indicate missing data.";

                    //validate datatypes are ok  
                    //validate ranges in fields in each datatype test as well.  Max and Min can be
                    //date, int, etc..
                    switch (fcp.DataType.ToLower())
                    {
                        case "string":

                            if (fcp.ValueList != null)
                            {
                                if(!fcp.ValueList.Split("|").ToList().Exists(x => x==c))
                                    message = $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are not found in the list {fcp.ValueList.Replace("|",", ")}";
                            }


                            break;
                        case "date":
                            if (!Helpers.DateValidation.IsValidDate(c) == true)
                                message = $"The dates in column {fcp.ColumnName} in {datafile.FileName } must be in the format YYYY-MM-DD";
                            else if (!Helpers.DateValidation.DateRange(c, fcp.MaxValue, fcp.MinValue))
                                message = $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] {c}";

                            break;
                        case "int":
                            int parsedResult;
                            if (!int.TryParse(c, out parsedResult))
                            {
                                message = $"{datafile.Name} contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}";
                            }
                            else if (!Helpers.RangeValidation.IntRanges(parsedResult, fcp.MaxValue, fcp.MaxValue))
                            { message = $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] valid ranges."; }


                            break;
                        case "real":
                            float f;
                            if (!float.TryParse(c, out f))
                                message = $"{datafile.Name} contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}";
                            else if (!Helpers.RangeValidation.FloatRanges(f, fcp.MaxValue, fcp.MaxValue))
                            { message = $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] {c}"; }


                            break;

                    }








                    colcnt++;
                }








            }




            //If 
            _files.Add(file);

            return message;

        }

        //DropBox
        private async Task<long> BurnFileAsync(Models.Files file)
        {

            //DateTime dt = DateTime.Now;
            //string folder_date = $"{dt.Month}-{dt.Day}-{dt.Year}";

            long size = 0;

            //using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            //{
            //    //var full = await dbx.Users.GetCurrentAccountAsync();
            //    //Console.WriteLine("{0} - {1}", full.Name.DisplayName, full.Email);

            //    string directory = $"/uploadedfiles/{file.FileProperties.site_id}";
            //    string directory_sub = $"/{file.FileProperties.site_id}_{Convert.ToInt32(file.FileProperties.version):D6}_{folder_date}";

            //    try
            //    {
            //        await CreateFolder(dbx, directory);
            //        await CreateFolder(dbx, directory + directory_sub);
            //        MemoryStream stream = new MemoryStream();
            //        await file.File.CopyToAsync(stream);
            //        size = file.File.Length;
            //        await Upload(dbx, directory + directory_sub, file.FileProperties.file_name, stream);
            //    }
            //    catch (ApiException<Dropbox.Api.Files.GetMetadataError> e)
            //    {
            //        if (e.ErrorResponse.IsPath && e.ErrorResponse.AsPath.Value.IsNotFound)
            //        {
            //            Console.WriteLine("Nothing found at path.");
            //        }
            //        else
            //        {
            //            // different issue; handle as desired
            //            Console.WriteLine(e);
            //        }
            //    }

            //}

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



        private string ConvertToFileString(List<ProjectFiles> pf)
        {
            string rtn = "";

            foreach (var f in pf)
            {
                rtn += $"{f.FileID}, ";

            }
            return rtn.Substring(0, rtn.Length - 2);

        }





        //DataIO
        //public string AddInstance(Models.BatchHead head)
        //{
        //    string version = "";
        //    try
        //    {
        //        using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
        //        {
        //            db.Open();
        //            var p = new DynamicParameters();
        //            p.Add("@site_id", head.SiteID, dbType: DbType.String);
        //            p.Add("@person_name", head.PersonsName, dbType: DbType.String);
        //            p.Add("@email", head.Email, dbType: DbType.String);
        //            p.Add("@comments", head.Comments, dbType: DbType.String);

        //            version = db.Query<string>("sp_add_instance", p, commandType: CommandType.StoredProcedure).Single().ToString();

        //        }
        //    }
        //    catch (SqlException ex)
        //    {
        //        return ex.Message;
        //    }

        //    return version;
        //}
        //public bool SaveFile(Models.FileProperties file)
        //{
        //    try
        //    {
        //        using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
        //        {
        //            db.Open();
        //            var p = new DynamicParameters();
        //            p.Add("@site_id", file.site_id, dbType: DbType.String);
        //            p.Add("@file_name", file.file_name, dbType: DbType.String);
        //            p.Add("@file_type", file.file_type, dbType: DbType.String);
        //            p.Add("@file_size", file.file_size, dbType: DbType.String);
        //            p.Add("@version", file.version, dbType: DbType.String);
        //            db.Execute("sp_add_file", p, commandType: CommandType.StoredProcedure);

        //        }
        //    }
        //    catch (SqlException)
        //    {
        //        return false;
        //    }
        //    return true;
        //}
        public List<ProjectModel> GetProjects()
        {
            List<ProjectModel> pm = new List<ProjectModel>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();

                    pm = db.Query<ProjectModel>("dbo.uspGetProjects", null, commandType: CommandType.StoredProcedure).ToList();

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
                    p.Add("@ProjectID", projectid, dbType: DbType.String);
                    fns = db.Query<ProjectFiles>("dbo.uspGetProjectFiles", p, commandType: CommandType.StoredProcedure).ToList();
                }
            }
            catch (SqlException)
            {
                return fns;
            }
            return fns;

        }
        public List<FileProperties> GetFileProperties(string projectid, string fileid)
        {

            List<FileProperties> fp = new List<FileProperties>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@ProjectID", projectid, dbType: DbType.String);
                    p.Add("@FileID", fileid, dbType: DbType.String);
                    fp = db.Query<FileProperties>("dbo.uspGetProjectFileColumns", p, commandType: CommandType.StoredProcedure).ToList();
                }
            }
            catch (SqlException)
            {
                return fp;
            }
            return fp;




        }

    }
}