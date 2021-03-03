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
            ResponseModel rm = new ResponseModel() { messages = new List<ValidateDataModel>() };
            BatchHead form = new BatchHead();

            //Check that we have at least 1 file and a full batch header of email, siteid, projectid before we validate form.
            rm = PreValidation(rm, files);
            if (rm.messages.Count(x => x.error != "") != 0) { rm.valid = false; return Json(rm); }


            form = JsonSerializer.Deserialize<BatchHead>(Request.Form["batchHeader"].ToString());
            //Get the Project data based on the ProjectID
            ProjectModel pm = GetProjects().Find(x => x.ProjectID == form.ProjectID);

            rm = StepOneValidation(rm, files, form);   ///CSV, FileName, DupFileName
            if (rm.messages.Count(x => x.error != "") != 0) { rm.valid = false; return Json(rm); }

            rm = StepTwoValidation(rm, form);   ///Can open file? Column names, Site IDs in First COL
            if (rm.messages.Count(x => x.error != "") != 0) { rm.valid = false; return Json(rm); }

            rm = StepThreeValidation(rm, form, pm);  ///Data types and values
            if (rm.messages.Count(x => x.error != "") != 0) { rm.valid = false; return Json(rm); }

            #region "DROPBOX UPLOAD DB ROW STORAGE"
            System.Guid UploadID = StartUpload(form);

            foreach (var file in _files)
                UploadFileDataToDatabase(UploadID, file);


            //rm.messages can be warning and errors.  If its warnings and no errors then the upload will 
            //continue and the user will see warnings in the user interface after their upload is 
            //completed with no errors.
            rm.messages = ValidateData(UploadID);

            if (rm.messages.Count(x => x.error != null) != 0) { rm.valid = false; return Json(rm); }

            bool test = false;

            try
            {
                Task<bool> saveToArchiveTask = SaveToArchive(UploadID, pm.FilePath);
                test = await saveToArchiveTask;
                if (!test)
                {
                    rm.valid = false;
                    return Json(rm);
                }

            }
            catch (Exception ex)
            {
                rm.valid = false;
                rm.messages.Add(new ValidateDataModel { error = ex.Message });
                return Json(rm);
            }

            try
            {
                Task<bool> saveToLatestTask = SaveToLatest(UploadID, pm.FilePath);
                test = await saveToLatestTask;
                if (!test)
                {
                    rm.valid = false;
                    rm.messages.Add(new ValidateDataModel { error = "Failure Saving File to Latest." });
                    return Json(rm);
                }
            }
            catch (Exception ex)
            {
                rm.valid = false;
                rm.messages.Add(new ValidateDataModel { error = ex.Message });
                return Json(rm);
            }

            #endregion

            rm.valid = true;
            //success
            rm.messages.Add(new ValidateDataModel { success = "Your files were successfully saved." });

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
            ResponseModel rm = new ResponseModel() { messages = new List<ValidateDataModel>() };

            if (batch.PersonsName.Trim() == "")
                rm.messages.Add(new ValidateDataModel { error = "Name field required." });

            if (!RegexUtilities.IsValidEmail(batch.Email))
                  rm.messages.Add(new ValidateDataModel { error = "Email is not valid." });

            if (batch.SiteID.Trim() == "")
                  rm.messages.Add(new ValidateDataModel { error = "SiteID field required." });
            else
            if (batch.SiteID.Trim().Any(ch => !Char.IsLetterOrDigit(ch)) || batch.SiteID.Trim().Contains(" ") || batch.SiteID.Substring(0, 1).Any(ch => !Char.IsLetter(ch)))
                  rm.messages.Add(new ValidateDataModel { error = "SiteID must be up to 20 letters or numbers, starting with a letter, and with no spaces or special characters." });

            rm.valid = (rm.messages.Count == 0 ? true : false);


            return Json(rm);
        }
        private ResponseModel PreValidation(ResponseModel rm, IFormFileCollection files)
        {
            BatchHead form;


            //check that the json format is correct
            try
            {
                form = JsonSerializer.Deserialize<BatchHead>(Request.Form["batchHeader"].ToString());
                rm = ((ResponseModel)((Microsoft.AspNetCore.Mvc.JsonResult)ValidateForm(form)).Value);


            }
            catch (JsonException ex)
            {
                Console.WriteLine(ex.Message);
                  rm.messages.Add(new ValidateDataModel { error = "Form data is not correct. Please check Name, Email, SiteID and Project fields." });


            }

            if (files.Count() == 0)
            {
                  rm.messages.Add(new ValidateDataModel { error = "Upload must include at least one file." });
            }
            return rm;
        }
        private ResponseModel StepOneValidation(ResponseModel rm, IFormFileCollection files, BatchHead form)
        {
            List<ValidateDataModel> messages = new List<ValidateDataModel>();
            List<ProjectFiles> pfs = GetProjectFiles(form.ProjectID);
            try
            {

                foreach (ProjectFiles pf in pfs)
                {

                    foreach (IFormFile f in files)
                    {
                        if (FileNameManager.FileName(f.FileName).Length >= pf.FileID.Length)  //cant slip in a file name with 3 char and substring 10 char from it.
                            if (FileNameManager.FileName(f.FileName).Substring(0, pf.FileID.Length).ToLower() == pf.FileID.ToLower())
                            {//If the file matches to its FileID then its added to _files for upload
                                _files.Add(new Files { FileID = pf.FileID, LatestFileName = FileNameManager.FileName(f.FileName), File = f, FileProperties = GetFileProperties(form.ProjectID, pf.FileID) });
                                //if the same file has been added twice then return the error message
                                if (_files.FindAll(x => x.LatestFileName.ToLower().Contains(pf.FileID.ToLower())).Count > 1)
                                {
                                    MessageValidationManager.Check(ref messages, $"Upload cannot contain duplicate {pf.FileID} files.");
                                }
                            }

                    }


                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }

            //This checks if any files exist in the uploaded batch but not in _files collection that is valid based on the database FileID values
            foreach (IFormFile f in files)
            {
                if (!FileNameManager.FileName(f.FileName).ToLower().Contains(".csv"))
                {
                    MessageValidationManager.Check(ref messages, $"<span class='file-col'>{FileNameManager.FileName(f.FileName)}</span> is not a valid file format. Must be .csv.");
                }
                else if (!_files.Exists(s => s.LatestFileName == FileNameManager.FileName(f.FileName)))
                {
                    MessageValidationManager.Check(ref messages, $"<span class='file-col'>{FileNameManager.FileName(f.FileName)}</span> has an incorrect file name. It must begin one of the following words: {ConvertToFileListString(pfs)}.");

                }
            }

            rm.messages = messages;

            return rm;

        }
        private ResponseModel StepTwoValidation(ResponseModel rm, BatchHead form)
        {
            List<ValidateDataModel> messages  = new List<ValidateDataModel>();
            bool log = true;
            foreach (Models.Files f in _files)
            {
                //Raw text data in lists of srings
                List<string> data = CSVReader.ReadFormFile(f.File);
                List<string> colheaders = CSVReader.ParseLine(data[0]);


                //remove the col headers from the data;
                data.Remove(data[0]);

                //check the number of cols are a match with what comes back from the DB file properties
                if (!(colheaders.Count() == f.FileProperties.Count()) && f.FileProperties.Count() != 0)
                {
                    MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains {CSVReader.ParseLine(data[0]).Count()} columns, but {f.FileProperties.Count()} were expected.");
                    log = false;
                }

                //check the col names match with what comes back from the DB file properties
                if (log)
                {
                    int cnt = 1;
                    foreach (string col in colheaders)
                    {
                        if (log)
                        {
                            if (f.FileProperties[cnt - 1].ColumnName.ToLower() != col.ToLower())
                            {
                                MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains incorrect column headers. They must be {GetColumnList(f.FileProperties)}, and <u>in that order</u>.");
                                log = false;
                            }
                        }
                        if (log)
                        {
                            if (f.FileProperties[cnt - 1].SortOrder != cnt.ToString())
                            {
                                MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains incorrect order of columns. They must be {GetColumnList(f.FileProperties)}, and <u>in that order</u>.");
                                log = false;
                            }
                        }
                        ++cnt;
                    }
                    log = true;
                }

                log = true;

            }
            rm.messages = messages;
            return rm;

        }
        private ResponseModel StepThreeValidation(ResponseModel rm, BatchHead form, ProjectModel pm)
        {
            List<ValidateDataModel> messages = new List<ValidateDataModel>();

            foreach (Models.Files f in _files)
            {
                FileProperties fcp;

                List<string> data = CSVReader.ReadFormFile(f.File);
                List<string> colheaders = CSVReader.ParseLine(data[0]);
                int colcnt = 0;
                data.Remove(data[0]);
                try
                {
                    foreach (var s in data)
                    {
                        colcnt = 0;
                        var row = CSVReader.ParseLine(s);

                        foreach (string c in row)
                        {
                            //every property of a column from the database
                            fcp = f.FileProperties.Find(x => x.ColumnName.ToLower() == colheaders[colcnt].ToLower());

                            if (fcp != null)
                            {
                                if (fcp.SortOrder != (colcnt + 1).ToString()) { MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains incorrect column header order.They must be {GetColumnList(f.FileProperties)}."); }

                                if (fcp.ColumnName.ToLower() == "siteid")
                                    if (c.Substring(0,form.SiteID.Length-1).ToLower() != form.SiteID.ToLower()) // ZAP do a substring to make sure the siteid is prefixed to what they key into the column.  BIDMC_YY and BIDCM_XX are both ok for example.
                                    {
                                        MessageValidationManager.Check(ref messages, $"The siteid values in <span class='file-col'>{f.LatestFileName}</span> do not match the Siteid in the form.");
                                    }

                                //validate no nulls
                                if (c.Trim() == "" || c.Trim().ToLower() == "(null)" || c.Trim().ToLower() == "null" || c.Trim().ToLower() == "na" || c.Trim().ToLower() == "n/a" || c.Trim().ToLower() == "n.a.")
                                {
                                    MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains missing or null values in column {colheaders[colcnt]}. Use {pm.NullCode} to indicate missing data.");
                                }
                                else
                                {
                                    //validate datatypes are ok  
                                    //validate ranges in fields in each datatype test as well.  Max and Min can be
                                    //date, int, etc..
                                    switch (fcp.DataType.ToLower())
                                    {
                                        case "string":
                                            if (fcp.ValueList != null)
                                            {
                                                if (!fcp.ValueList.Split("|").ToList().Exists(x => x == c))
                                                    MessageValidationManager.Check(ref messages, $"There are invalid values in column {fcp.ColumnName} in <span class='file-col'>{f.LatestFileName}</span>.");
                                            }
                                            break;
                                        case "date":
                                            if (!Helpers.DateValidation.IsValidDate(c) == true)
                                            {
                                                MessageValidationManager.Check(ref messages, $"The dates in column {fcp.ColumnName} in <span class='file-col'>{f.LatestFileName }</span> must be in the format YYYY-MM-DD.");

                                            }
                                            else if (!Helpers.DateValidation.DateRange(c, fcp.MaxValue, fcp.MinValue))
                                            {
                                                MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in <span class='file-col'>{f.LatestFileName}</span> that are outside the allowed range.");
                                            }
                                            break;
                                        case "int":
                                            int parsedResult;
                                            if (!int.TryParse(c, out parsedResult))
                                            {
                                                MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}.");
                                            }
                                            else if (!Helpers.RangeValidation.IntRanges(parsedResult, fcp.MaxValue, fcp.MinValue))
                                            { MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in <span class='file-col'>{f.LatestFileName}</span> that are outside the allowed range."); }
                                            break;
                                        case "real":
                                            float val;
                                            if (!float.TryParse(c, out val))
                                                MessageValidationManager.Check(ref messages, $"<span class='file-col'>{f.LatestFileName}</span> contains invalid data in column {fcp.ColumnName}, which should only contain values of type {fcp.DataType}.");
                                            else if (!Helpers.RangeValidation.FloatRanges(val, fcp.MaxValue, fcp.MinValue))
                                            { MessageValidationManager.Check(ref messages, $"There are values in column {fcp.ColumnName} in <span class='file-col'>{f.LatestFileName}</span> that are outside the allowed range."); }
                                            break;
                                    }
                                }
                            }
                            colcnt++;
                        }

                    }



                }
                catch (Exception e)
                {
                    MessageValidationManager.Check(ref messages, $"An unexpected error occured in <span class='file-col'>{f.LatestFileName}</span>.");
                    Console.WriteLine(e.Message);
                }




            }

            rm.messages = messages;

            return rm;
        }
        private List<ValidateDataModel> ValidateData(System.Guid UploadID)
        {
            List<ValidateDataModel> retVal = new List<ValidateDataModel>();
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
                        retVal.Add(v);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                retVal.Add(new ValidateDataModel { error = e.Message });
            }
            return retVal;
        }
        #endregion
        
        
        
        #region "DropBox"
        async Task<bool> SaveToArchive(System.Guid UploadID, string projectpath)
        {
            using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            {
                string directory = $"{projectpath}archive";

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
                Console.WriteLine(e.Message);
                return false;
            }
            return true;
        }
        async Task<bool> SaveToLatest(System.Guid UploadID, string projectpath)
        {
            using (var dbx = new DropboxClient(_configuration.GetSection("Dropbox")["key"]))
            {
                string directory = $"{projectpath}latest";

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







        #region "Private Methods/Functions"
        private string ConvertToFileListString(List<ProjectFiles> pf)
        {
            string rtn = "";

            foreach (var f in pf)
            {
                rtn += $"{f.FileID}, ";

            }
            return rtn.Substring(0, rtn.Length - 2);

        }
        private string GetColumnList(List<FileProperties> fp)
        {

            string s = "";
            foreach (FileProperties f in fp)
            {
                s += $"{f.ColumnName}, ";
            }
            s = s.Substring(0, s.Length - 2);

            return s;
        }
        private System.Guid StartUpload(BatchHead form)
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
                        p.Add("@OriginalFileName" + i, FileNameManager.FileName(file.File.FileName), dbType: DbType.String);
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
                    db.Close();
                }
            }
            catch (Exception e)
            {
                return new System.Guid();
            }
            return uploadID;
        }
        private bool UploadFileDataToDatabase(System.Guid UploadID, Files dataFile)
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
        private bool UploadLineDataToDatabase(System.Guid UploadID, string fileID, int lineNum, string line)
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
                    db.Close();
                }
            }
            catch (Exception e)
            {
                return false;
            }

            return true;
        }
        private List<ProjectModel> GetProjects()
        {
            List<ProjectModel> pm = new List<ProjectModel>();
            try
            {
                using (IDbConnection db = new SqlConnection(_configuration.GetConnectionString("4CE")))
                {
                    db.Open();
                    pm = db.Query<ProjectModel>("dbo.uspGetProjects", null, commandType: CommandType.StoredProcedure).ToList();
                    db.Close();
                }
            }
            catch (SqlException e)
            {
                return pm;
            }

            return pm;
        }
        private List<ProjectFiles> GetProjectFiles(string projectid)
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
                    db.Close();
                }
            }
            catch (SqlException)
            {
                return fns;
            }


            return fns;

        }
        private List<FileProperties> GetFileProperties(string projectid, string fileid)
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
                    db.Close();
                }
            }
            catch (SqlException)
            {
                return fp;
            }
            return fp;




        }
        #endregion 
    }
}