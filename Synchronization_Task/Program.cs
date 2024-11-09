using System;
using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using Serilog;
using System.Linq;


namespace Synchronization
{
    class Program
    {
        private static System.Timers.Timer? timer;

        static void Main()
        {
            string sourceFolder;
            string replicaFolder;
            string logFolder;
            double timerInterval;

            sourceFolder = ReadInput("source");
            replicaFolder = ReadInput("replica", sourceFolder);
            logFolder = ReadInput("log", replicaFolder);

            do
            {
                Console.WriteLine("Enter a number for the synchronization interval in minutes between 0 and 1440 (max 24 hours, decimal numbers accepted with the use of ',')");
                //24h * 60m = 1440
            }
            while (!double.TryParse(Console.ReadLine(), out timerInterval) || timerInterval<=0 || timerInterval > 1440);

            //setup serilog
            Log.Logger = new LoggerConfiguration()
               .WriteTo.Console()
               .WriteTo.File(@$"{logFolder}\log.txt", rollingInterval: RollingInterval.Day)
               .CreateLogger();

            RunPeriodically(sourceFolder, replicaFolder, timerInterval);
            Console.WriteLine("Press enter to exit");
            Console.ReadLine();
        }

        //second parameter to prevent the source and the replica folder from having the same exact path and the log folder from being in the replica folder and getting deleted
        private static string ReadInput(string folderName, string? previousPath = null)
        {
            string? output;
            do
            {
                Console.WriteLine($"Enter the full path for the {folderName} folder");
                output = Console.ReadLine();
            }
            while (!Directory.Exists(output) || (previousPath != null && output == previousPath));
            return output;
        }

        private static void RunPeriodically(string sourceFolder, string replicaFolder, double intervalInMinutes)
        {
            timer = new System.Timers.Timer(intervalInMinutes * 60 * 1000);
            timer.Elapsed += (sender, e) => Synch(sourceFolder, replicaFolder);
            timer.AutoReset = true;
            timer.Enabled = true;
        }

        public static void Synch(string source, string replica)
        {
            Console.WriteLine("Synched");
            var sourceDictionary = TurnFilesToHash(source);
            var replicaDictionary = TurnFilesToHash(replica);

            //comparing source and replica folders
            CopyMissingAndAlteredFiles(source, replica, sourceDictionary, replicaDictionary);

            //create empty subdirectories present in the source folder that are not in the replica folder
            ReplicateEmptyFolders(source, replica);

            //check for files that have been deleted in the source folder and are present only in the replica folder and remove them
            DeleteFilesPresentOnlyInReplica(replica, sourceDictionary, replicaDictionary);

            // delete the folders(subdirectories) that are no longer in the source folder
            DeleteDirectoriesNotInSource(source, replica);
        }

        private static void CopyMissingAndAlteredFiles(string source, string replica, Dictionary<string, byte[]> sourceDictionary, Dictionary<string, byte[]> replicaDictionary)
        {
            foreach (var file in sourceDictionary)
            {
                string sourceFilePath = Path.Combine(source, file.Key);
                string replicaFilePath = Path.Combine(replica, file.Key);

                //if the file is missing in the replica folder, copy it from the source folder
                if (!replicaDictionary.TryGetValue(file.Key, out var replicaFileContent))
                {
                    CheckDirectoryExistence(Path.GetDirectoryName(replicaFilePath));
                    File.Copy(sourceFilePath, replicaFilePath);
                    Log.Information($"Copied: {sourceFilePath} to {replicaFilePath}");
                }
                else if (!replicaFileContent.SequenceEqual(file.Value))
                {
                    //if the file already exists but the content is different, replace it
                    CheckDirectoryExistence(Path.GetDirectoryName(replicaFilePath));
                    File.Copy(sourceFilePath, replicaFilePath, true);
                    Log.Information($"Replaced: {replicaFilePath} in {sourceFilePath} due to changes in the file's contents");
                }
            }
        }

        private static void ReplicateEmptyFolders(string source, string replica)
        {
            foreach (var sourceDirectory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(source, sourceDirectory);
                string replicaPath = Path.Combine(replica, relativePath);

                CheckDirectoryExistence(replicaPath);
            }
        }

        private static void DeleteFilesPresentOnlyInReplica(string replica, Dictionary<string, byte[]> sourceDictionary, Dictionary<string, byte[]> replicaDictionary)
        {
            foreach (var file in replicaDictionary)
            {
                string replicaFilePath = Path.Combine(replica, file.Key);

                if (!sourceDictionary.ContainsKey(file.Key))
                {
                    Log.Information("Deleted file: {replicaFilePath}", replicaFilePath);
                    File.Delete(replicaFilePath);
                }
            }
        }

        private static void DeleteDirectoriesNotInSource(string source, string replica)
        {
            foreach (var directory in Directory.GetDirectories(replica,"*", SearchOption.AllDirectories))
            {
                string relativeReplicaPath = Path.GetRelativePath(replica, directory);
                string sourceDirectory = Path.Combine(source, relativeReplicaPath);

                if (!Directory.Exists(sourceDirectory))
                {
                    try
                    {
                        Log.Information("Deleted directory: {directory}", directory);
                        Directory.Delete(directory, true);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Exception: {e.Message}");
                    }
                }
            }
        }

        //creates any missing directories/folders
        private static void CheckDirectoryExistence(string? directoryPath)
        {
            if (directoryPath != null && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
                Log.Information($"Created directory: {directoryPath}");
            }
        }

        private static Dictionary<string, byte[]> TurnFilesToHash(string path)
        {
            var dictionary = new Dictionary<string, byte[]>();

            var dir = new DirectoryInfo(path);

            using SHA256 mySHA256 = SHA256.Create();
            FileInfo[] files = dir.GetFiles("*", SearchOption.AllDirectories);

            // create a hash value for every file in the directory which will save the integrity of each file, allowing us to check for any potential changes in the file's contents
            // and saves it in a dictionary along with the file's name
            foreach (FileInfo fInfo in files)
            {
                using FileStream fileStream = fInfo.Open(FileMode.Open);
                try
                {
                    byte[] hashValue = mySHA256.ComputeHash(fileStream);
                    string relativeFilePath = Path.GetRelativePath(path, fInfo.FullName);
                    dictionary.Add(relativeFilePath, hashValue);
                }
                catch (IOException e)
                {
                    Console.WriteLine($"I/O Exception: {e.Message}");
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine($"Access Exception: {e.Message}");
                }
            }
            return dictionary;
        }
    }
}