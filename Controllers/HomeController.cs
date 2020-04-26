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

        #region "GET Method"
        public ActionResult Index()
        {

            ViewBag.Projects = new SelectList(GetProjects(), "ProjectID", "ProjectName");
            //ViewBag.Files = 

            return View();
        }
        #endregion
        #region "POST Methods"
        
        [HttpPost]
        public async Task<IActionResult> UploadFilesAsync()
        {
            var files = Request.Form.Files;
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };
            BatchHead form = new BatchHead();

            //check that the json format is correct
            try
            {
                form = JsonSerializer.Deserialize<BatchHead>(Request.Form["batchHeader"].ToString());

            }
            catch (JsonException)
            {
                rm.messages.Add("Form data is not correct. Please check Name, Email, SiteID and Project fields.");
                return Json(rm);

            }
            //check the form data is correct.
            if (!((ResponseModel)((Microsoft.AspNetCore.Mvc.JsonResult)ValidateForm(form)).Value).valid)
            {
                rm = (ResponseModel)ValidateForm(form);
                return Json(rm);
            }


            //validate each file, store the physical files at the end after all have passed validation
            List<string> tmpmessages;
            foreach (var file in files)
            {
                if (!file.Name.Contains(".csv"))
                    rm.messages.Add("Not a valid file format. Must be .csv.");
                else
                {
                    tmpmessages = new List<string>();
                    tmpmessages = ValidateFile(file, form);

                    foreach (string s in tmpmessages)
                        rm.messages.Add(s);
                }
            }

            //if there are errors in the files then return to the client and do not start upload
            if (rm.messages.Count() != 0) { return Json(rm); }

            System.Guid UploadID = StartUpload(form);

            foreach (var file in _files)
            {
                UploadFileDataToDatabase(UploadID, file);
            }
            tmpmessages = new List<string>();
            tmpmessages = ValidateData(UploadID);

            foreach (string s in tmpmessages)
                rm.messages.Add(s);
            if (rm.messages.Count() != 0) { return Json(rm); }

            bool test = false;
            Task<bool> saveToArchiveTask = SaveToArchive(UploadID);
            test = await saveToArchiveTask;
            if (!test)
            {
                rm.messages.Add("Failure Saving File to Archive");
                return Json(rm);
            }

            Task<bool> saveToLatestTask = SaveToLatest(UploadID);
            test = await saveToLatestTask;
            if (!test)
            {
                rm.messages.Add("Failure Saving File to Latest");
                return Json(rm);
            }

            rm.valid = true;// do the upload junk and write to dropbox
            rm.messages.Add($"{files.Count()} file{(files.Count() > 1 ? "s are" : " is")} valid and uploaded.");

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
        #endregion
        #region "Validation"
        private IActionResult ValidateForm(BatchHead batch)
        {
            ResponseModel rm = new ResponseModel() { messages = new List<string>() };

            if (batch.PersonsName.Trim() == "")
                rm.messages.Add("Name field required.");

            if (!RegexUtilities.IsValidEmail(batch.Email))
                rm.messages.Add("Email is not valid.");

            if (batch.SiteID.Trim() == "")
                rm.messages.Add("SiteID field required.");

            //           if (batch.ProjectID.Trim() == "")
            //               rm.messages.Add("Please select project from dropdown.");

            rm.valid = (rm.messages.Count == 0 ? true : false);


            return Json(rm);
        }
        private List<string> ValidateFile(IFormFile datafile, BatchHead form)
        {
            string fileID = datafile.Name.Split("-")[0].ToLower();

            Models.Files file = new Files
            {
                File = datafile,
                FileProperties = new List<FileProperties>(),
                FileID = fileID,
                Valid = false
            };
                        

            _projectfiles = GetProjectFiles(form.ProjectID);

            List<string> messages = new List<string>();

            file.FileProperties = GetFileProperties(form.ProjectID, fileID);

            //Check that the name of the file exists in a given project
            if (!_projectfiles.Exists(f => f.FileID.ToLower() == fileID) || file.FileProperties.Count() == 0)
            {
                MessageValidationManager.Check(ref messages, $"{datafile.Name} has the incorrect name. It must start with one of the following words: {ConvertToFileString(_projectfiles)}");
            }



            //Raw text data in lists of srings
            List<string> data = CSVReader.ReadFormFile(datafile);
            List<string> colheaders = CSVReader.ParseLine(data[0]);
            string colheadermsg = data[0];

            //remove the col headers from the data;
            data.Remove(data[0]);

            //check the number of cols are a match with what comes back from the DB file properties
            if (!(colheaders.Count() == file.FileProperties.Count()) && file.FileProperties.Count() != 0)
            {
                MessageValidationManager.Check(ref messages, $"{datafile.Name} contains {CSVReader.ParseLine(data[0]).Count()} columns, but {file.FileProperties.Count()} where expected.");
            }


            //check the col names match with what comes back from teh DB file properties
            foreach (string col in colheaders)
            {
                if (!file.FileProperties.Exists(x => x.ColumnName.ToLower() == col.ToLower()))
                    MessageValidationManager.Check(ref messages, $"{datafile.Name} contains incorrect column headers. They must be {colheadermsg}.");

            }


            //check siteid in file with what was provided in the form post to the API
            foreach (var s in data)
            {                //first col of every line should be the siteid
                if (!(CSVReader.ParseLine(s)[0].ToLower() == form.SiteID.ToLower()))
                    MessageValidationManager.Check(ref messages, $"The siteid values in {datafile.Name} do not match the Siteid in the form.");


                var row = CSVReader.ParseLine(s);
                int colcnt = 0;
                FileProperties fcp = new FileProperties();

                if(file.FileProperties.Count()!=colheaders.Count())
                    MessageValidationManager.Check(ref messages, $"{datafile.Name} contains too many columns. Expected {file.FileProperties.Count()} and you supplied {colheaders.Count()}.");
                else                
                foreach (string c in row)
                {
                    try
                    {                        //every property of a column from the database
                        fcp = file.FileProperties.Find(x => x.ColumnName == colheaders[colcnt]);
                    }
                    catch
                    {
                            try
                            {
                                MessageValidationManager.Check(ref messages, $"{datafile.Name} contains missing column {colheaders[colcnt]}. Use -2 with correct column header to indicate missing data.");
                            }
                            catch
                            {
                                MessageValidationManager.Check(ref messages, $"{datafile.Name} contains too many columns. Expects {file.FileProperties.Count()}");
                            }
                    }

                    if (fcp != null)
                    {

                        //validate no nulls
                        if (c.Trim() == "" || c.Trim().ToLower() == "(null)" || c.Trim().ToLower() == "null" || c.Trim().ToLower() == "na" || c.Trim().ToLower() == "n/a" || c.Trim().ToLower() == "n.a.")
                            MessageValidationManager.Check(ref messages, $"{datafile.Name} contains missing or null values in column {colheaders[colcnt]}. Use -2 to indicate missing data.");

                        //validate datatypes are ok  
                        //validate ranges in fields in each datatype test as well.  Max and Min can be
                        //date, int, etc..
                        switch (fcp.DataType.ToLower())
                        {
                            case "string":

                                if (fcp.ValueList != null)
                                {
                                    if (!fcp.ValueList.Split("|").ToList().Exists(x => x == c))
                                        MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are not found in the list {fcp.ValueList.Replace("|", ", ")}");
                                }


                                break;
                            case "date":
                                
                                if (!Helpers.DateValidation.IsValidDate(c) == true)
                                {
                                    MessageValidationManager.Check(ref messages, $"The dates in column {fcp.ColumnName} in {datafile.FileName } must be in the format YYYY-MM-DD");

                                }
                                else if (!Helpers.DateValidation.DateRange(c, fcp.MaxValue, fcp.MinValue))
                                {
                                    MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] {c}");
                                }
                                break;
                            case "int":
                                int parsedResult;
                                if (!int.TryParse(c, out parsedResult))
                                {
                                    MessageValidationManager.Check(ref messages, $"{datafile.Name} contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}");
                                }
                                else if (!Helpers.RangeValidation.IntRanges(parsedResult, fcp.MaxValue, fcp.MinValue))
                                { MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] valid ranges."); }


                                break;
                            case "real":
                                float f;
                                if (!float.TryParse(c, out f))
                                    MessageValidationManager.Check(ref messages, $"{datafile.Name} contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}");
                                else if (!Helpers.RangeValidation.FloatRanges(f, fcp.MaxValue, fcp.MinValue))
                                { MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in {datafile.FileName} that are [below|above] {c}"); }


                                break;

                        }

                    }

                    colcnt++;
                }


            }

            if (messages.Count() == 0)
            {
                file.Valid = true;
                _files.Add(file);
            }


            return messages;

        }
        public List<string> ValidateData(System.Guid UploadID)
        {
            List<string> retVal = new List<string>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@UploadID", UploadID, dbType: DbType.Guid);
                    p.Add("@Status", "", dbType: DbType.String, ParameterDirection.Output);
                    List<ValidateDataModel> l = new List<ValidateDataModel>();
                    l = db.Query<ValidateDataModel>("[dbo].[uspValidateData]", p, commandType: CommandType.StoredProcedure).ToList();
                    foreach (ValidateDataModel v in l)
                    {
                        retVal.Add(v.error);
                    }
                }
            }
            catch (Exception e)
            {
                return null;
            }
            return retVal;
        }
        #endregion
        #region "DropBox"
        async Task<bool> SaveToArchive(System.Guid UploadID)
        {
            using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            {
                string directory = $"/archive";

                try
                {
                    await CreateFolder(dbx, directory);
                    foreach (Models.Files file in _files)
                    {
                        MemoryStream stream = new MemoryStream();
                        await file.File.CopyToAsync(stream);
                        await Upload(dbx, directory, file.ArchiveFileName, stream);
                    }
                }
                catch (ApiException<Dropbox.Api.Files.GetMetadataError> e)
                {
                    if (e.ErrorResponse.IsPath && e.ErrorResponse.AsPath.Value.IsNotFound)
                    {
                        Console.WriteLine("Nothing found at path.");
                        return false;
                    }
                    else
                    {
                        // different issue; handle as desired
                        Console.WriteLine(e.Message);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);

                }

            }
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@UploadID", UploadID, dbType: DbType.Guid);
                    p.Add("@Status", "", dbType: DbType.String, ParameterDirection.Output);
                    List<ValidateDataModel> l = new List<ValidateDataModel>();
                    db.Execute("[uspConfirmSaved]", p, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception e)
            {
                return false;
            }
            return true;
        }
        async Task<bool> SaveToLatest(System.Guid UploadID)
        {
            using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            {
                string directory = $"/latest";

                try
                {
                    await CreateFolder(dbx, directory);
                    foreach (Models.Files file in _files)
                    {
                        MemoryStream stream = new MemoryStream();
                        await file.File.CopyToAsync(stream);
                        stream.Position = 0;
                        await Upload(dbx, directory, file.LatestFileName, stream);
                    }
                }
                catch (ApiException<Dropbox.Api.Files.GetMetadataError> e)
                {
                    if (e.ErrorResponse.IsPath && e.ErrorResponse.AsPath.Value.IsNotFound)
                    {
                        Console.WriteLine("Nothing found at path.");
                        return false;
                    }
                    else
                    {
                        // different issue; handle as desired
                        Console.WriteLine(e);
                        return false;
                    }
                }
                catch (Exception ex) { Console.WriteLine(ex.Message); }

            }
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@UploadID", UploadID, dbType: DbType.Guid);
                    List<ValidateDataModel> l = new List<ValidateDataModel>();
                    db.Execute("[dbo].[uspFinishUpload]", p, commandType: CommandType.StoredProcedure);

                }
            }
            catch (Exception e)
            {
                return false;
            }
            return true;
        }
        private static async Task<FolderMetadata> CreateFolder(DropboxClient client, string path)
        {
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
            try
            {
                mem.Position = 0;
                var updated = await dbx.Files.UploadAsync(
                    folder + "/" + file,
                    WriteMode.Overwrite.Instance,
                    body: mem);
                Console.WriteLine("Saved {0}/{1} rev {2}", folder, file, updated.Rev);
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
            }
        }
        #endregion

        private string ConvertToFileString(List<ProjectFiles> pf)
        {
            string rtn = "";

            foreach (var f in pf)
            {
                rtn += $"{f.FileID}, ";

            }
            return rtn.Substring(0, rtn.Length - 2);

        }
        public System.Guid StartUpload(BatchHead form)
        {
            System.Guid uploadID;
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@ProjectID", form.ProjectID, dbType: DbType.String);
                    p.Add("@SiteID", form.SiteID, dbType: DbType.String);
                    p.Add("@PersonName", form.PersonsName, dbType: DbType.String);
                    p.Add("@Email", form.Email, dbType: DbType.String);

                    int i = 1;
                    foreach (Models.Files file in _files)
                    {
                        p.Add("@OriginalFileName" + i, file.File.Name, dbType: DbType.String);
                        p.Add("@FileID" + i, file.FileID, dbType: DbType.String);
                        i++;
                        if (i >= 9) break;
                    }
                    p.Add("Status", "", dbType: DbType.String, ParameterDirection.Output);
                    List<StartUploadResults> l = new List<StartUploadResults>();
                    l = db.Query<StartUploadResults>("[dbo].[uspStartUpload]", p, commandType: CommandType.StoredProcedure).ToList();
                    StartUploadResults r = l.First();
                    if (r.ArchiveFileName1 != null)
                    {
                        _files[0].ArchiveFileName = r.ArchiveFileName1;
                        _files[0].LatestFileName = r.LatestFileName1;
                    }
                    if (r.ArchiveFileName2 != null)
                    {
                        _files[1].ArchiveFileName = r.ArchiveFileName2;
                        _files[1].LatestFileName = r.LatestFileName2;
                    }
                    if (r.ArchiveFileName3 != null)
                    {
                        _files[2].ArchiveFileName = r.ArchiveFileName3;
                        _files[2].LatestFileName = r.LatestFileName3;
                    }
                    if (r.ArchiveFileName4 != null)
                    {
                        _files[3].ArchiveFileName = r.ArchiveFileName4;
                        _files[3].LatestFileName = r.LatestFileName4;
                    }
                    if (r.ArchiveFileName5 != null)
                    {
                        _files[4].ArchiveFileName = r.ArchiveFileName5;
                        _files[4].LatestFileName = r.LatestFileName5;
                    }
                    if (r.ArchiveFileName6 != null)
                    {
                        _files[5].ArchiveFileName = r.ArchiveFileName6;
                        _files[5].LatestFileName = r.LatestFileName6;
                    }
                    if (r.ArchiveFileName7 != null)
                    {
                        _files[6].ArchiveFileName = r.ArchiveFileName7;
                        _files[6].LatestFileName = r.LatestFileName7;
                    }
                    if (r.ArchiveFileName8 != null)
                    {
                        _files[7].ArchiveFileName = r.ArchiveFileName8;
                        _files[7].LatestFileName = r.LatestFileName8;
                    }
                    if (r.ArchiveFileName9 != null)
                    {
                        _files[8].ArchiveFileName = r.ArchiveFileName9;
                        _files[8].LatestFileName = r.LatestFileName9;
                    }
                    uploadID = r.UploadID;
                }
            }
            catch (Exception e)
            {
                return new System.Guid();
            }
            return uploadID;
        }
        public bool UploadFileDataToDatabase(System.Guid UploadID, Files dataFile)
        {
            List<string> lines = CSVReader.ReadFormFile(dataFile.File);
            lines.Remove(lines[0]);
            int i = 1;
            foreach (string line in lines)
            {
                UploadLineDataToDatabase(UploadID, dataFile.FileID, i, line);
                i++;
            }
            return true;
        }
        public bool UploadLineDataToDatabase(System.Guid UploadID, string fileID, int lineNum, string line)
        {
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    var p = new DynamicParameters();
                    p.Add("@UploadID", UploadID, dbType: DbType.Guid);
                    p.Add("@FileID", fileID, dbType: DbType.String);
                    p.Add("@LineNumber", lineNum, dbType: DbType.Int32);

                    List<string> cols = CSVReader.ParseLine(line);
                    int i = 1;
                    foreach (string col in cols)
                    {
                        p.Add("@Col" + i, col, dbType: DbType.String);
                        i++;
                        if (i >= 19) break;
                    }
                    p.Add("Status", "", dbType: DbType.String, ParameterDirection.Output);
                    List<StartUploadResults> l = new List<StartUploadResults>();
                    db.Execute("[dbo].[uspLoadData]", p, commandType: CommandType.StoredProcedure);
                }
            }
            catch (Exception e)
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

                    pm = db.Query<ProjectModel>("dbo.uspGetProjects", null, commandType: CommandType.StoredProcedure).ToList();

                }
            }
            catch (SqlException e)
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