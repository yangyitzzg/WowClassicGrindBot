using System.IO;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using System.Text.RegularExpressions;
using Game;
using WinAPI;

namespace Core
{
    public class AddonConfigurator
    {
        private readonly ILogger logger;
        private readonly WowProcess wowProcess;

        public AddonConfig Config { get; private set; }

        private const string DefaultAddonName = "DataToColor";
        private const string AddonSourcePath = @".\Addons\";

        private string AddonBasePath => Path.Join(Config.InstallPath, "Interface", "AddOns");

        private string DefaultAddonPath => Path.Join(AddonBasePath, DefaultAddonName);
        private string FinalAddonPath => Path.Join(AddonBasePath, Config.Title);

        public event Action? OnChange;

        public AddonConfigurator(ILogger logger)
        {
            this.logger = logger;
            Config = AddonConfig.Load();

            wowProcess = new WowProcess();
        }

        public bool Installed()
        {
            return GetInstalledVersion() != null;
        }

        public bool Validate()
        {
            if (!Directory.Exists(Config.InstallPath))
            {
                logger.LogError($"{nameof(Config)}.{nameof(Config.InstallPath)} - error - does not exists: '{Config.InstallPath}'");
                return false;
            }
            else
            {
                logger.LogInformation($"{nameof(Config)}.{nameof(Config.InstallPath)} - correct: '{Config.InstallPath}'");
                if (!Directory.Exists(AddonBasePath))
                {
                    logger.LogError($"{nameof(Config)}.{nameof(Config.InstallPath)} - error - unable to locate Interface\\Addons folder: '{Config.InstallPath}'");
                    return false;
                }
                else
                {
                    logger.LogInformation($"{nameof(Config)}.{nameof(Config.InstallPath)} - correct - Interface\\Addons : '{Config.InstallPath}'");
                }
            }

            if (string.IsNullOrEmpty(Config.Author))
            {
                logger.LogError($"{nameof(Config)}.{nameof(Config.Author)} - error - cannot be empty: '{Config.Author}'");
                return false;
            }

            if (!string.IsNullOrEmpty(Config.Title))
            {
                // this will appear in the lua code so
                // special character not allowed
                // also numbers not allowed
                Config.Title = Regex.Replace(Config.Title, @"[^\u0000-\u007F]+", string.Empty);
                Config.Title = new string(Config.Title.Where(char.IsLetter).ToArray());
                Config.Title = Config.Title.Trim();
                Config.Title = Config.Title.Replace(" ", "");

                if (Config.Title.Length == 0)
                {
                    logger.LogError($"{nameof(Config)}.{nameof(Config.Title)} - error - use letters only: '{Config.Title}'");
                    return false;
                }
            }
            else
            {
                logger.LogError($"{nameof(Config)}.{nameof(Config.Title)} - error - cannot be empty: '{Config.Title}'");
                return false;
            }

            return true;
        }

        public void Install()
        {
            try
            {
                DeleteAddon();
                CopyAddonFiles();
                RenameAddon();
                MakeUnique();

                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(Install)} - Success");
            }
            catch (Exception e)
            {
                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(Install)} - Failed\n" + e.Message);
            }
        }

        private void DeleteAddon()
        {
            if (Directory.Exists(DefaultAddonPath))
            {
                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(DeleteAddon)} -> Default Addon Exists");
                Directory.Delete(DefaultAddonPath, true);
            }

            if (!string.IsNullOrEmpty(Config.Title) && Directory.Exists(FinalAddonPath))
            {
                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(DeleteAddon)} -> Unique Addon Exists");
                Directory.Delete(FinalAddonPath, true);
            }
        }

        private void CopyAddonFiles()
        {
            try
            {
                CopyFolder("");
                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(CopyAddonFiles)} - Success");
            }
            catch (Exception e)
            {
                logger.LogError(e.Message);

                // This only should be happen when running from IDE
                CopyFolder(".");
                logger.LogInformation($"{nameof(AddonConfigurator)}.{nameof(CopyAddonFiles)} - Success");
            }
        }

        private void CopyFolder(string parentFolder)
        {
            DirectoryCopy(Path.Join(parentFolder + AddonSourcePath), AddonBasePath, true);
        }

        private void RenameAddon()
        {
            string src = Path.Join(AddonBasePath, DefaultAddonName);
            if (src != FinalAddonPath)
                Directory.Move(src, FinalAddonPath);
        }

        private void MakeUnique()
        {
            BulkRename(FinalAddonPath, DefaultAddonName, Config.Title);
            EditToc();
            EditMainLua();
            EditModulesLua();
        }

        private static void BulkRename(string fPath, string match, string fNewName)
        {
            string fExt;
            string fFromName;
            string fToName;

            //copy all files from fPath to files array
            FileInfo[] files = new DirectoryInfo(fPath).GetFiles();
            //loop through all files
            foreach (var f in files)
            {
                //get the filename without the extension
                fFromName = Path.GetFileNameWithoutExtension(f.Name);

                if (!fFromName.Contains(match))
                    continue;

                //get the file extension
                fExt = Path.GetExtension(f.Name);

                //set fFromName to the path + name of the existing file
                fFromName = Path.Join(fPath, f.Name); //string.Format("{0}{1}", fPath, f.Name);
                //set the fToName as path + new name + _i + file extension
                fToName = Path.Join(fPath, fNewName) + fExt; //string.Format("{0}{1}{2}", fPath, fNewName, fExt);

                //rename the file by moving to the same place and renaming
                File.Move(fFromName, fToName);
            }
        }

        private void EditToc()
        {
            string tocPath = Path.Join(FinalAddonPath, Config.Title + ".toc");
            string text = File.ReadAllText(tocPath);
            text = text.Replace(DefaultAddonName, Config.Title);

            // edit author
            text = text.Replace("## Author: FreeHongKongMMO", "## Author: " + Config.Author);

            File.WriteAllText(tocPath, text);
        }

        private void EditMainLua()
        {
            string mainLuaPath = Path.Join(FinalAddonPath, Config.Title + ".lua");
            string text = File.ReadAllText(mainLuaPath);
            text = text.Replace(DefaultAddonName, Config.Title);

            //edit slash command
            Config.Command = Config.Title.Trim().ToLower();
            text = text.Replace("dc", Config.Command);
            text = text.Replace("DC", Config.Command);

            File.WriteAllText(mainLuaPath, text);
        }

        private void EditModulesLua()
        {
            FileInfo[] files = new DirectoryInfo(FinalAddonPath).GetFiles();
            foreach (var f in files)
            {
                if (f.Extension.Contains("lua"))
                {
                    string path = f.FullName;
                    string text = File.ReadAllText(path);
                    text = text.Replace(DefaultAddonName, Config.Title);

                    File.WriteAllText(path, text);
                }
            }
        }

        public void Delete()
        {
            DeleteAddon();
            AddonConfig.Delete();

            OnChange?.Invoke();
        }

        public void Save()
        {
            Config.Save();

            OnChange?.Invoke();
        }


        #region InstallPath

        public void FindPathByExecutable()
        {
            if (wowProcess.WarcraftProcess != null)
            {
                Config.InstallPath = ExecutablePath.Get(wowProcess.WarcraftProcess);
                if (!string.IsNullOrEmpty(Config.InstallPath))
                {
                    logger.LogInformation($"{nameof(Config)}.{nameof(Config.InstallPath)} - found running instance: '{Config.InstallPath}'");
                    return;
                }
            }

            logger.LogError($"{nameof(Config)}.{nameof(Config.InstallPath)} - game not running");
        }

        #endregion

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();

            // If the destination directory doesn't exist, create it.       
            Directory.CreateDirectory(destDirName);

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string tempPath = Path.Combine(destDirName, file.Name);
                file.CopyTo(tempPath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string tempPath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                }
            }
        }

        public bool UpdateAvailable()
        {
            if (Config.IsDefault())
                return false;

            Version? repo = GetRepoVerion();
            Version? installed = GetInstalledVersion();

            return installed != null && repo != null && repo > installed;
        }

        public Version? GetRepoVerion()
        {
            Version? repo = null;
            try
            {
                repo = GetVersion(Path.Join(AddonSourcePath, DefaultAddonName), DefaultAddonName);

                if (repo == null)
                {
                    string parentFolder = ".";

                    repo = GetVersion(Path.Join(parentFolder + AddonSourcePath, DefaultAddonName), DefaultAddonName);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e.Message);
            }
            return repo;
        }

        public Version? GetInstalledVersion()
        {
            return GetVersion(FinalAddonPath, Config.Title);
        }

        private static Version? GetVersion(string path, string fileName)
        {
            string tocPath = Path.Join(path, fileName + ".toc");

            if (!File.Exists(tocPath))
                return null;

            string begin = "## Version: ";
            var line = File
                .ReadLines(tocPath)
                .SkipWhile(line => !line.StartsWith(begin))
                .FirstOrDefault();

            string? versionStr = line?.Split(begin)[1];
            if (Version.TryParse(versionStr, out var version))
                return version;

            return null;
        }
    }
}