using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Rinex_Scan
{
    public partial class Form1 : Form
    {
        //There is a small bug that counts -1 for satellites, it will be fixed soon
        public bool resultsSaved = false;
        public bool processDone = false;
        public static atxProcessor atxProcessor = new atxProcessor();
        public class ListViewItemData
        {
            public string FileName { get; set; }
            public string Progress { get; set; }
            public string Status { get; set; }
            public string RinexVersion { get; set; }

        }
        public List<string> importedObservationFiles = new List<string>();
        private BufferedListView importedFilesListView;
        public static Dictionary<string, List<string>> blockSpecificFrequencies = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> PRNBased = new Dictionary<string, List<string>>();
        public static List<DateRange> dateRanges = new List<DateRange>();
        List<string> userSelectedSys = new List<string>();
        public string[] beidou2Satellites = { "C01", "C02", "C03", "C03", "C05", "C06", "C07", "C08", "C09", "C10", "C11", "C12", "C13", "C14", "C16" };
        public string[] beidou3Satellites = { "C19", "C20", "C21", "C22", "C23", "C24", "C25", "C26", "C27", "C28", "C29", "C30", "C31", "C32", "C33", "C34", "C35", "C36", "C37", "C38", "C39", "C40", "C41", "C42", "C43", "C44", "C45", "C46", "C48", "C50", "C56", "C57", "C58", "C59", "C60", "C61", "C62" };
        List<string> availabilityOutput = new List<string>();
        List<string> missingOutput = new List<string>();
        public Dictionary<string, HashSet<string>> allSpecificFrequenciesyConstellation = new Dictionary<string, HashSet<string>>();
        string softwareVersion = "1.0.64";
        public Form1()
        {
            InitializeComponent();
            importedFilesListView = new BufferedListView
            {
                Location = new Point(220, 20),
                Name = "importedFilesListView",
                Size = new Size(950, 500),
                TabIndex = 1,
                UseCompatibleStateImageBehavior = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details

            };
            importedFilesListView.Resize += listview_Resize;
            importedFilesListView.Columns.Add("File Name", -2, HorizontalAlignment.Left);
            importedFilesListView.Columns.Add("Rinex Version", 100, HorizontalAlignment.Left);
            importedFilesListView.Columns.Add("Epoch Availability", 150, HorizontalAlignment.Left);
            importedFilesListView.Columns.Add("Observation Duration", 150, HorizontalAlignment.Left);
            importedFilesListView.Columns.Add("Progress", 150, HorizontalAlignment.Left);
            importedFilesListView.Columns.Add("Status", 100, HorizontalAlignment.Left);
            importedFilesListView.ColumnWidthChanging += new ColumnWidthChangingEventHandler(listView_ColumnWidthChanging);
            importedFilesListView.OwnerDraw = true;
            importedFilesListView.DrawColumnHeader += (sender, e) => e.DrawDefault = true;
            importedFilesListView.DrawSubItem += ListView_DrawProgress;
            tabPage1.Controls.Add(importedFilesListView);
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            CultureInfo enUSSet = new CultureInfo("en-US"); // Use the "en-US" culture, which uses a dot decimal separator
            Thread.CurrentThread.CurrentCulture = enUSSet; // Set the current culture to the custom culture
            string startuppath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            this.Text = "Rinex Scan " + softwareVersion;
            constellationsListBox.SetItemChecked(0, true);
            constellationsListBox.SetItemChecked(1, true);
            constellationsListBox.SetItemChecked(2, true);
            constellationsListBox.SetItemChecked(3, true);
            constellationsListBox.SetItemChecked(4, true);
            constellationsListBox.SetItemChecked(5, true);
            //Check neccessary files beforehand
            /*string[] necessaryFiles = {
            "BLOCK_TYPES_SPECIFIC_FREQUENCIES.txt",
            "CRX2RNX.exe",
            "RINEX_conversions.txt"
            };*/
            string[] necessaryFiles = {
            "BLOCK_TYPES_SPECIFIC_FREQUENCIES.txt",
            "CRX2RNX.exe",
            };
            // Check dependencies
            foreach (var fileName in necessaryFiles)
            {
                string filePath = Path.Combine(startuppath, fileName);
                if (!File.Exists(filePath))
                {
                    // Show a MessageBox if any currentRNXFile is missing
                    MessageBox.Show($"The necessary currentRNXFile {fileName} does not exist in the startup directory. The application will now exit.", "File Missing", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1); // Exit the application with an error code
                }
            }
            //get the atx files in the program directory
            string[] atxFiles = Directory.GetFiles(startuppath, "*.atx");
            if (atxFiles.Length > 0)
            {
                foreach (string file in atxFiles)
                {
                    atxComboBox.Items.Add(Path.GetFileName(file));
                }
                atxComboBox.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("No ATX files found in program directory");
                startProcessButton.Enabled = false;
            }
            try
            {
                //Try to read block specific frequencies
                blockSpecificFrequencies = atxProcessor.SpecificTypes();
            }
            catch
            {
                MessageBox.Show("Initialize Error, close the application, check the atx and specific blocks files.");
                startProcessButton.Enabled = false;
            }

            foreach (var kvp in blockSpecificFrequencies)
            {
                // Get the system prefix (G, R, J)
                var system = kvp.Key.Split('-')[0];

                if (!allSpecificFrequenciesyConstellation.ContainsKey(system))
                {
                    allSpecificFrequenciesyConstellation[system] = new HashSet<string>();
                }

                foreach (var code in kvp.Value)
                {
                    allSpecificFrequenciesyConstellation[system].Add(code);
                }
            }

            
            listview_Resize(importedFilesListView, EventArgs.Empty);
        }
        private void ListView_DrawProgress(object sender, DrawListViewSubItemEventArgs e)
        {
            //using custom listview
            if (e.ColumnIndex == 4) // this is progress column in the list view
            {
                float percent = 0f;
                if (float.TryParse(e.SubItem.Text.Replace("%", ""), out percent))
                {
                    Rectangle bounds = e.Bounds;
                    bounds.Width = (int)(bounds.Width * (percent / 100f));
                    e.Graphics.FillRectangle(System.Drawing.Brushes.Green, bounds);
                }
            }
            else
            {
                e.DrawDefault = true;
            }
        }
        private void listview_Resize(object sender, EventArgs e)
        {
            if (importedFilesListView.Columns.Count > 0)
            {
                int totalColumnWidth = 0;
                for (int i = 1; i < importedFilesListView.Columns.Count; i++)
                {
                    totalColumnWidth += importedFilesListView.Columns[i].Width;
                }
                int remainingWidth = importedFilesListView.ClientSize.Width - totalColumnWidth;
                importedFilesListView.Columns[0].Width = remainingWidth > 0 ? remainingWidth : importedFilesListView.Columns[0].Width;
            }
        }
        private void listView_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            e.Cancel = true;
            e.NewWidth = importedFilesListView.Columns[e.ColumnIndex].Width;
        }
        private void importButton_Click(object sender, EventArgs e)
        {
            //To import files, open a dialog
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Multiselect = true;
                openFileDialog.Filter = "Rinex files (*.??O;*.RNX;*.??D;*.CRX)|*.??O;*.??o;*.??D;*.??d;*.RNX;*.rnx;*.CRX;*.crx|All files (*.*)|*.*";
                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    availabilityOutput.Clear();
                    missingOutput.Clear();
                    saveResultsButton.Enabled = false;
                    startProcessButton.Enabled = false;
                    importedObservationFiles.Clear();
                    importedFilesListView.Items.Clear();
                    //Since open file dialog has All Files option, check if user selected non-rinex files
                    foreach (string dialogSelectedFile in openFileDialog.FileNames.Where(f => new FileInfo(f).Length > 0))
                    {
                        float rinexVersion = GetRinexVersion(dialogSelectedFile);
                        if (rinexVersion == 0.0f) continue;
                        var itemData = CreateListViewItemData(dialogSelectedFile, rinexVersion);
                        AddListViewItem(itemData);
                        importedObservationFiles.Add(dialogSelectedFile);
                    }
                }
                else
                {
                    return;
                }
            }
            startProcessButton.Enabled = true;
        }
        private void ClearListButton_Click(object sender, EventArgs e)
        {
            importedObservationFiles.Clear();
            importedFilesListView.Items.Clear();
            availabilityOutput.Clear();
            missingOutput.Clear();
            saveResultsButton.Enabled = false;
        }
        private async void startProcess_Click(object sender, EventArgs e)
        {
            string startuppath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            try
            {
                //atx file is big and complex, formatting it and removing unneccessary lines
                PRNBased = atxProcessor.GetPRNBasedBlockDict(Path.Combine(startuppath, atxComboBox.SelectedItem.ToString()));
                dateRanges = atxProcessor.ReadRangesFromFormattedATX(Path.Combine(startuppath, ("formatted_" + Path.GetFileNameWithoutExtension(atxComboBox.SelectedItem.ToString()))));
            }
            catch
            {
                MessageBox.Show("Initialize Error, close the application, check the atx and specific blocks files.");
                startProcessButton.Enabled = false;
            }
            availabilityOutput.Clear();
            //Stopwatch stopwatch_1 = new Stopwatch();
            //stopwatch_1.Start();

            //Take the user selected constellations as input option
            foreach (object itemChecked in constellationsListBox.CheckedItems)
            {
                string itemName = itemChecked.ToString();
                switch (itemName)
                {
                    case "GPS":
                        userSelectedSys.Add("G");
                        break;
                    case "GLONASS":
                        userSelectedSys.Add("R");
                        break;
                    case "GALILEO":
                        userSelectedSys.Add("E");
                        break;
                    case "BEIDOU-2":
                        userSelectedSys.Add("C2");
                        break;
                    case "BEIDOU-3":
                        userSelectedSys.Add("C3");
                        break;
                    case "QZSS":
                        userSelectedSys.Add("J");
                        break;
                }
            }
            if (importedFilesListView.Items.Count == 0) return;
            //Setting the max parallel option below, experimental with core count
            int hostProcessorCount = Environment.ProcessorCount;
            int maxSemaphoreConcurrent = 4; //default
            if (checkBox8.Checked)
            {
                maxSemaphoreConcurrent = hostProcessorCount-1;
            }
            else
            {
                maxSemaphoreConcurrent = hostProcessorCount / 2;
            }
            SemaphoreSlim semaphore = new SemaphoreSlim(maxSemaphoreConcurrent);

            List<Task<(List<string> List1, List<string> List2)>> tasks = new List<Task<(List<string> List1, List<string> List2)>>();
            StreamWriter errorLogger = new StreamWriter("Error.log");
            foreach (var currentRNXFile in importedObservationFiles)
            {
                // Check the currentRNXFile for non-ascii chars, it will throw error if tried to be read
                // or remove those chars from currentRNXFile and try process, implemented
                await semaphore.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    (List<string> List1, List<string> List2) results = (new List<string>(), new List<string>());
                    try
                    {
                        float rinexVersion = GetRinexVersion(currentRNXFile);
                        if (rinexVersion >= 2 && rinexVersion < 3) //Rinex 2.xx
                        {
                            int FileIndex = importedObservationFiles.IndexOf(currentRNXFile);
                            try
                            {
                                if (HasNullCharacters(currentRNXFile))
                                {
                                    bool success = RemoveNullChars(currentRNXFile);
                                    if (success)
                                    {
                                        results = ProcessRinex2(currentRNXFile, FileIndex);
                                    }
                                    else
                                    {
                                        UpdateStatus(FileIndex, "File Error", "", "");
                                    }
                                }
                                else
                                {
                                    results = ProcessRinex2(currentRNXFile, FileIndex);
                                }
                            }
                            catch (Exception ex)
                            {
                                errorLogger.WriteLine(ex.ToString() + ":" + currentRNXFile);
                                UpdateStatus(FileIndex, "File Error", "", "");
                            }
                        }
                        else if (rinexVersion >= 3 && rinexVersion < 5) //Rinex 3.xx 4.xx basically same structure
                        {
                            int FileIndex = importedObservationFiles.IndexOf(currentRNXFile);
                            try
                            {
                                if (HasNullCharacters(currentRNXFile))
                                {
                                    UpdateStatus(FileIndex, "File Error", "", "");
                                }
                                else
                                {
                                    results = ProcessRinex3(currentRNXFile, FileIndex);

                                }
                            }
                            catch (Exception ex)
                            {
                                errorLogger.WriteLine(ex.ToString() + ":" + currentRNXFile);
                                UpdateStatus(FileIndex, "File Error", "", "");
                            }
                        }
                        if (checkBox7.Checked)
                        {
                            importedFilesListView.Invoke((MethodInvoker)(() =>
                            {
                                if (importedObservationFiles.IndexOf(currentRNXFile) + 10 < importedFilesListView.Items.Count)
                                {
                                    importedFilesListView.Items[importedObservationFiles.IndexOf(currentRNXFile) + 10].EnsureVisible();
                                }
                            }));
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                    return results;
                }));
            }
            // Await all tasks and process the results
            var resultLists = await Task.WhenAll(tasks);
            // After all tasks are complete
            // Process results here
            availabilityOutput.Add("### Rinex Scan v" + softwareVersion + " Frequency Availability File###");
            availabilityOutput.Add("### Reserved ###");
            availabilityOutput.Add("### Reserved ###");
            missingOutput.Add("### Rinex Scan v" + softwareVersion + " Missing Frequencies File");
            missingOutput.Add("### Reserved ###");
            missingOutput.Add("### Reserved ###");
            foreach (var result in resultLists)
            {
                List<string> list1 = result.List1;
                availabilityOutput.AddRange(list1);
                List<string> list2 = result.List2;
                missingOutput.AddRange(list2);
            }

            errorLogger.Close();
            await Task.WhenAll(tasks);
            saveResultsButton.Enabled = true;
            //stopwatch_1.Stop();
            userSelectedSys.Clear();
            //TimeSpan elapsed = stopwatch_1.Elapsed;
            //string elapsedTimeFormatted = string.Format("{0} hours, {1} minutes, and {2} seconds",elapsed.Hours, elapsed.Minutes, elapsed.Seconds);
            //string title = "Elapsed Time";
            processDone = true;
            //MessageBox.Show(elapsedTimeFormatted, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void saveResults_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Filter = "Text Files (*.txt)|*.txt";
            saveFileDialog.DefaultExt = "txt";
            saveFileDialog.AddExtension = true;

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                File.WriteAllLines(saveFileDialog.FileName, availabilityOutput);
                if (missingOutput.Count > 3) //static! -todo (only have comment lines which is currently 3.
                {
                    File.WriteAllLines(saveFileDialog.FileName + "_Missing.txt", missingOutput);
                }

                MessageBox.Show("Files Saved");
            }
            else
            {
                Console.WriteLine("File saving was canceled.");
            }
            resultsSaved = true;
        }
        private void maxParallelFileInfo(object sender, EventArgs e)
        {
            //Parallel processing may not necessarily use high CPU, its bound to many criteria ie. IO operations, task scheduling, HDD/SSD speed etc
            MessageBox.Show("This option will likely cause very high CPU usage when scanning Epochs+Frequencies. " +
                "\n\n If you are performing other tasks simultaneously, avoid selecting this option. ", "Caution", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
        }
        private float GetRinexVersion(string filename)
        {
            using (var reader = new StreamReader(filename))
            {
                for (int i = 0; i <= 10; i++) //Rinex version is in the first line(Generally), in any case it will look upto 10
                {
                    string line = reader.ReadLine();
                    if (line.Contains("RINEX VERSION"))
                    {
                        string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && float.TryParse(parts[0], out float version))
                        {
                            return version;
                        }
                        return 0.0f;
                    }
                }
            }
            return 0.0f;
        }
        private void AddListViewItem(ListViewItemData itemData)
        {
            ListViewItem item = new ListViewItem(itemData.FileName);
            item.SubItems.AddRange(new[] { itemData.RinexVersion.ToString(), string.Empty, string.Empty, itemData.Progress, itemData.Status });
            importedFilesListView.Items.Add(item);
        }
        private ListViewItemData CreateListViewItemData(string file, double rinexVersion)
        {
            string formattedRinexVersion = rinexVersion.ToString("0.00");
            return new ListViewItemData
            {
                FileName = Path.GetFileName(file),
                RinexVersion = formattedRinexVersion,
                Progress = "0%",
                Status = "Waiting...",
            };
        }
        void IncrementCount(Dictionary<string, Dictionary<string, Dictionary<string, int>>> data, string constellationID, string satelliteID, string frequencyName)
        {
            //increment stored frequency count for both available and empty frequencies in the corresponding dictionaries
            if (!data[constellationID][frequencyName].ContainsKey(satelliteID))
            {
                data[constellationID][frequencyName][satelliteID] = 0;
            }
            data[constellationID][frequencyName][satelliteID]++;
        }
        private (List<string> AvailabilityOutputLines, List<string> MissingOutputLines) ProcessRinex3(string file, int index)
        {
            //QZSS specific dates for frequency identification, not available in the atx files (2024.10.22)
            DateTime qzss_defined_1 = new DateTime(2023, 01, 01);
            DateTime qzss_defined_2 = new DateTime(2022, 03, 22);

            //Some of these variables(below) are not used in this version,
            //they can be used when LLI (Loss of Lock Indicator) information is desired.
            List<string> LLI_output = new List<string>();
            LLI_output.Add("slipped_epochs    slipped_satellites");

            string rectype = "N/A";
            string anttype = "N/A";
            //Main output vars
            List<string> AvailabilityOutputLines = new List<string>();
            List<string> MissingOutputLines = new List<string>();
            Dictionary<String, List<String>> frequencyDictionary = new Dictionary<String, List<String>>();
            const int frequencyCharLength = 16;
            
            string fileExtension = Path.GetExtension(file).ToUpper();
            string rinexFile = "";
            bool compressedFile = false;
            //is it raw or compressed? Check it and mark it, later we'll delete the extracted file if its compressed in the beginning
            if (fileExtension == ".CRX" || Regex.IsMatch(fileExtension, @"^\.\d{2}D$"))
            {
                Process decompressionRinex = new Process();
                ProcessStartInfo info3 = new ProcessStartInfo();
                info3.FileName = "CRX2RNX.exe";
                info3.UseShellExecute = false;
                info3.CreateNoWindow = true;
                decompressionRinex.StartInfo = info3;
                string args = "-f \"" + file + "\"";
                info3.Arguments = args;
                decompressionRinex.Start();
                decompressionRinex.WaitForExit();
                int exitCode = decompressionRinex.ExitCode;
                if (exitCode == 0 || exitCode == 2)
                {
                    rinexFile = Path.ChangeExtension(file, ".rnx"); // Change the extension to .rnx
                    compressedFile = true;
                }
                else
                {
                    return (null, null);
                }
            }
            else
            {
                rinexFile = file;
            }
            long totalBytes = new FileInfo(rinexFile).Length;
            long bytesRead = 0;
            long bytesRead2 = 0;
            List<DateTime> epochsAsDateTime = new List<DateTime>();
            //int epochcount = 0;
            UpdateStatus(index, "Working...", "", "");
            double observationInterval = 0;
            using (StreamReader reader = new StreamReader(rinexFile))
            {

                //Reading the header lines to get important information like interval, observation types in the rinex etc.
                string currentLine;
                List<string> obsTypesHeaderLines = new List<string>();
                while ((currentLine = reader.ReadLine()) != null)
                {
                    if (currentLine.Contains("END OF HEADER"))
                    {
                        break;
                    }
                    if (currentLine.Contains("INTERVAL"))
                    {
                        observationInterval = Convert.ToDouble(currentLine.Split(new char[0], StringSplitOptions.RemoveEmptyEntries)[0]);
                    }
                    if (currentLine.Contains("SYS / # / OBS TYPES") || currentLine.Contains("TYPES OF OBSERV"))
                    {
                        obsTypesHeaderLines.Add(currentLine);
                    }
                    if (currentLine.Contains("REC #"))
                    {
                        if (!string.IsNullOrWhiteSpace(currentLine.Substring(20, 40)))
                        {
                            rectype = currentLine.Substring(20, 40);
                        }
                    }
                    if (currentLine.Contains("ANT #"))
                    {
                        if (!string.IsNullOrWhiteSpace(currentLine.Substring(20, 40)))
                        {
                            anttype = currentLine.Substring(20, 40);
                        }
                    }
                }
                //Based on header information, parse frequencies into empty dictionaries
                //Using three dictionaries to store the available, empty and total frequencies, this part will be refactored for performance -todo
                frequencyDictionary = parseObservationFrequencies2dataSet(obsTypesHeaderLines);
                Dictionary<string, Dictionary<string, Dictionary<string, int>>> CountedFrequencies = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
                Dictionary<string, Dictionary<string, Dictionary<string, int>>> TotalFrequencies = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
                Dictionary<string, Dictionary<string, Dictionary<string, List<DateTime>>>> MissingFrequencies = new Dictionary<string, Dictionary<string, Dictionary<string, List<DateTime>>>>();
                CountedFrequencies = parseObservationFrequenciesFromRinex3Header(obsTypesHeaderLines);
                TotalFrequencies = parseObservationFrequenciesFromRinex3Header(obsTypesHeaderLines);
                MissingFrequencies = parseObservationFrequenciesMissing(obsTypesHeaderLines);
                while ((currentLine = reader.ReadLine()) != null)
                {
                    //Update progress
                    if (radioButton1.Checked)
                    {
                        bytesRead2 += currentLine.Length;
                        if (bytesRead2 % 10000 == 0)
                        {
                            int progress = (int)((bytesRead2 / (float)totalBytes) * 100);
                            UpdateProgress(index, progress);
                        }
                    }
                    if (currentLine.StartsWith(">"))
                    {
                        double TD(string content, int start, int length) => Convert.ToDouble(content.Substring(start, length));
                        int ToInt32(string content, int start, int length) => Convert.ToInt32(content.Substring(start, length));
                        List<string> LLI_Satellites = new List<string>();
                        try
                        {
                            //try to read epoch information, if it fails, nex line will be read and try
                            int year = ToInt32(currentLine, 1, 5);
                            int month = ToInt32(currentLine, 6, 3);
                            int day = ToInt32(currentLine, 9, 3);
                            int hour = ToInt32(currentLine, 12, 3);
                            int minute = ToInt32(currentLine, 15, 3);
                            double second = TD(currentLine, 18, 11);
                            int epochFlag = ToInt32(currentLine, 29, 3);
                            int satelliteCount = ToInt32(currentLine, 32, 3);
                            DateTime epochDate = new DateTime();
                            if (epochFlag == 0)
                            {
                                epochDate = new DateTime(year, month, day, hour, minute, (int)second);
                                epochsAsDateTime.Add(epochDate);
                            }
                            else
                            {
                                continue;
                            }
                            if (radioButton2.Checked)
                            {
                                for (int i = 0; i < satelliteCount; i++)
                                {
                                    string satelliteDataLine = reader.ReadLine();
                                    if (bytesRead % 10000 == 0)
                                    {
                                        int progress = (int)((bytesRead / (float)totalBytes) * 100);
                                        UpdateProgress(index, progress);
                                    }
                                    bytesRead += satelliteDataLine.Length;
                                    string constellationID = satelliteDataLine.Substring(0, 1);
                                    string satelliteID = satelliteDataLine.Substring(0, 3);
                                    if (satelliteID.Substring(1, 1) == " ")
                                    {
                                        // Replace the space with '0' for one-digit satellite PNR's written in this format "G 6", "R 1" > G06, G01
                                        //
                                        satelliteID = satelliteID.Substring(0, 1) + "0" + satelliteID.Substring(2, 1);
                                    }
                                    int frequencyIndex = 0;
                                    int expectedFrequencyCount = frequencyDictionary[constellationID].Count;

                                    for (int j = 3; j < satelliteDataLine.Length; j += frequencyCharLength)
                                    {
                                        string frequencyName = frequencyDictionary[constellationID][frequencyIndex];
                                        int substringLength = Math.Min(frequencyCharLength, satelliteDataLine.Length - j);
                                        string frequencyString = satelliteDataLine.Substring(j, substringLength).Substring(0, 14);
                                        double frequencyValue = (string.IsNullOrWhiteSpace(frequencyString) ? double.NaN : double.Parse(frequencyString));
                                        string LLI = "";
                                        if (Double.IsNaN(frequencyValue) || frequencyValue == 0) // frequency value is either 0.0000 or not available
                                        {
                                            IncrementCount(TotalFrequencies, constellationID, satelliteID, frequencyName);
                                            if (!MissingFrequencies[constellationID][frequencyName].ContainsKey(satelliteID))
                                            {
                                                MissingFrequencies[constellationID][frequencyName][satelliteID] = new List<DateTime>();
                                            }
                                            MissingFrequencies[constellationID][frequencyName][satelliteID].Add(epochDate);
                                        }
                                        else if (!Double.IsNaN(frequencyValue) && frequencyValue != 0) //frequency value is available and is a number
                                        {
                                            IncrementCount(CountedFrequencies, constellationID, satelliteID, frequencyName);
                                            IncrementCount(TotalFrequencies, constellationID, satelliteID, frequencyName);
                                        }
                                        frequencyIndex++;
                                    }
                                    while (frequencyIndex < expectedFrequencyCount) //If the dataline is shorter than expected,//this check must be provided for continuing frequencies to fill them empty
                                    {
                                        string frequencyName = frequencyDictionary[constellationID][frequencyIndex];
                                        IncrementCount(TotalFrequencies, constellationID, satelliteID, frequencyName);
                                        if (!MissingFrequencies[constellationID][frequencyName].ContainsKey(satelliteID))
                                        {
                                            MissingFrequencies[constellationID][frequencyName][satelliteID] = new List<DateTime>();
                                        }
                                        MissingFrequencies[constellationID][frequencyName][satelliteID].Add(epochDate);
                                        frequencyIndex++;
                                    }
                                }
                            }
                            if (LLI_Satellites.Count > 0)
                            {
                                // Convert LLI_Satellites items to a single string
                                string satellitesString = string.Join(" ", LLI_Satellites);

                                // Add to LLI_output list
                                LLI_output.Add(epochDate.ToString("HH:mm:ss") + "          " + satellitesString);
                            }

                        }
                        catch
                        {
                            continue;
                        }
                    }
                    //if there is no observationInterval comment line in the header, we'll find it in the epochs
                    //Since there might be missing epochs, we get the minimum difference between two epochs(by checking "numberOfEpochsToCheck" epochs) as observationInterval
                    //also it should not be 0, since some rinex files can have same epoch lines, checking epoch flag is important
                    int numberOfEpochsToCheck = 20; //number of epochs to count
                    if (observationInterval == 0 && epochsAsDateTime.Count == numberOfEpochsToCheck + 1)
                    {
                        double[] intervalDifferences = new double[numberOfEpochsToCheck];
                        for (int i = 1; i <= numberOfEpochsToCheck; i++)
                        {
                            TimeSpan timeDifference = epochsAsDateTime[i] - epochsAsDateTime[i - 1];
                            double DifferenceInSeconds = timeDifference.TotalSeconds;
                            intervalDifferences[i - 1] = DifferenceInSeconds;
                        }
                        observationInterval = intervalDifferences.Where(x => x > 0).DefaultIfEmpty().Min();
                    }
                }
                //print missing frequencies
                //loss of lock print
                goto skipLLI;
                string LLIFileName = Path.GetFileNameWithoutExtension(file);
                string folder = Directory.GetParent(file).FullName;
                using (StreamWriter writer = new StreamWriter(Path.Combine(folder, "00_LossLock_" + LLIFileName + ".txt")))
                {
                    foreach (string s in LLI_output)
                    {
                        writer.WriteLine(s);
                    }
                }
            skipLLI:
                //--end
                DateTime firstEpoch = epochsAsDateTime.First();
                DateTime lastEpoch = epochsAsDateTime.Last();
                List<DateTime> expectedEpochs = new List<DateTime>();
                DateTime current_time = firstEpoch;
                while (current_time <= lastEpoch)
                {
                    expectedEpochs.Add(current_time);
                    current_time = current_time.AddSeconds(observationInterval);
                }
                List<DateTime> missing_epochs = expectedEpochs.Except(epochsAsDateTime).ToList();
                TimeSpan timeSpan2 = TimeSpan.FromSeconds(observationInterval);
                List<(DateTime Start, DateTime End)> missingList2 = GroupConsecutiveMissingEpochs(missing_epochs, timeSpan2);
                string resultLine2 = "";
                if (missing_epochs.Count == 0)
                {
                    //resultLine2 = "Full"; //no need to write this file to missing epochs output since its full
                }
                else
                {
                    MissingOutputLines.Add("");
                    MissingOutputLines.Add("File Name: " + Path.GetFileName(file));
                    resultLine2 = "Total : " + missing_epochs.Count + " Epochs : " + PrintMissingIntervals(missingList2);
                    MissingOutputLines.Add(resultLine2);
                }
                TimeSpan totalDifferenceSpan = lastEpoch - firstEpoch;
                string totalObservationDuration = totalDifferenceSpan.ToString(@"d\-hh\:mm\:ss");
                double totalDifferenceInSeconds = totalDifferenceSpan.TotalSeconds + 30;
                double requiredEpochsCount = totalDifferenceInSeconds / observationInterval;
                double availableEpochsRatio = Math.Round(epochsAsDateTime.Count / requiredEpochsCount * 100, 2);
                UpdateProgress(index, 100);
                UpdateStatus(index, "Finished", availableEpochsRatio.ToString("F2"), totalObservationDuration);

                // satellite averages for each frequency
                AvailabilityOutputLines.Add("");
                AvailabilityOutputLines.Add("File Name: " + Path.GetFileName(file) + " Epoch Availability(%): " + availableEpochsRatio.ToString("F2") + " Duration: " + totalObservationDuration);
                AvailabilityOutputLines.Add("REC: " + rectype);
                AvailabilityOutputLines.Add("ANT: " + anttype);
                //Frequency output selected, before writing the results, checking the corresponding blocks for specific frequencies
                if (radioButton2.Checked)
                {
                    Dictionary<string, string> correspondingBlocks = new Dictionary<string, string>();

                    foreach (var constellation in TotalFrequencies)
                    {
                        foreach (var frequency in constellation.Value)
                        {
                            foreach (var satellite in frequency.Value)
                            {
                                string satID = satellite.Key;
                                string correspondingBlock = atxProcessor.GetCorrespondingBlock(dateRanges, firstEpoch, satID);
                                if (!correspondingBlocks.ContainsKey(satID))
                                {
                                    correspondingBlocks.Add(satID, correspondingBlock);
                                }
                            }
                        }
                    }
                    foreach (var constellationKey in userSelectedSys)
                    {
                        if (CountedFrequencies.ContainsKey(constellationKey.Substring(0, 1)))
                        {
                            AvailabilityOutputLines.Add($"+++{constellationKey}+++");
                            var CountedfrequencyDictionary = CountedFrequencies[constellationKey.Substring(0, 1)];
                            HashSet<string> allSatellitesCounted = new HashSet<string>();
                            foreach (var frequencyKeys in CountedfrequencyDictionary.Values)
                            {
                                foreach (var SatellitesKeys in frequencyKeys.Keys)
                                {
                                    if (constellationKey == "C2")
                                    {
                                        if (beidou2Satellites.Contains(SatellitesKeys))
                                        {
                                            allSatellitesCounted.Add(SatellitesKeys.ToString());
                                        }
                                    }
                                    else if (constellationKey == "C3")
                                    {
                                        if (beidou3Satellites.Contains(SatellitesKeys))
                                        {
                                            allSatellitesCounted.Add(SatellitesKeys.ToString());
                                        }
                                    }
                                    else
                                    {
                                        allSatellitesCounted.Add(SatellitesKeys.ToString());
                                    }

                                }
                            }

                            List<string> sortedAllSatellitesCounted = allSatellitesCounted.ToList();
                            sortedAllSatellitesCounted.Sort();
                            string spaceBetweenElements = "     "; //5 char length of frequenct title
                            string joinedString = string.Join(spaceBetweenElements, sortedAllSatellitesCounted);
                            string formattedString = "       Average     " + joinedString + spaceBetweenElements;
                            AvailabilityOutputLines.Add(formattedString);
                            string[] J01_2023 = { "J_C1Z", "J_L1Z", "J_D1Z", "J_S1Z", "J_C6S", "J_L6S", "J_D6S", "J_S6S", "J_C6L", "J_L6L", "J_D6L", "J_S6L", "J_C6X", "J_L6X", "J_D6X", "J_S6X" };
                            string[] J02_03_07_2022 = { "J_C1Z", "J_L1Z", "J_D1Z", "J_S1Z" };
                            foreach (var frequencyKeys in CountedfrequencyDictionary.Keys)
                            {
                                List<string> writtenResultsFrequencies = new List<string>();
                                StringBuilder fline = new StringBuilder();
                                fline.Append($"{frequencyKeys,5}");
                                var CountedsatellitesDictionary = CountedfrequencyDictionary[frequencyKeys];
                                foreach (var satelliteInDictionaryCounted in sortedAllSatellitesCounted)
                                {
                                    string constellationInDic = constellationKey.Substring(0, 1);
                                    if (CountedsatellitesDictionary.TryGetValue(satelliteInDictionaryCounted, out int value) && TotalFrequencies.ContainsKey(constellationInDic) &&
                                        TotalFrequencies[constellationInDic].ContainsKey(frequencyKeys) && TotalFrequencies[constellationInDic][frequencyKeys].ContainsKey(satelliteInDictionaryCounted))
                                    {
                                        int totalEpochsInFrequency = TotalFrequencies[constellationKey.Substring(0, 1)][frequencyKeys][satelliteInDictionaryCounted];
                                        if (totalEpochsInFrequency != 0) // if its not zero write the count
                                        {
                                            
                                            //J01: C6S, L6S, D6S, S6S, C6L, L6L, D6L, S6L, C6X, L6X, D6X, S6X  (Before 2023 !)
                                            //J01: C1Z, L1Z, D1Z, S1Z(Before March 24, 2022!)
                                            //J02: C1Z, L1Z, D1Z, S1Z(Before March 24, 2022!)
                                            //J03: C1Z, L1Z, D1Z, S1Z(Before March 24, 2022!)
                                            //J07: C1Z, L1Z, D1Z, S1Z(Before March 24, 2022!)
                                            if (constellationInDic == "G" || constellationInDic == "R")
                                            {
                                                try
                                                {
                                                    //its not in the specific blocks but there is specific frequency data read from file where it shouldn't, should we get the data or write N/A -todo
                                                    if (!blockSpecificFrequencies.ContainsKey(correspondingBlocks[satelliteInDictionaryCounted]) && allSpecificFrequenciesyConstellation[constellationInDic].Contains(frequencyKeys.Substring(2, 3)))
                                                    {
                                                        fline.Append("N/A".PadLeft(8));
                                                    }
                                                    else
                                                    {
                                                        string fwrite = Math.Round(value * 100.0 / totalEpochsInFrequency, 2).ToString("F2");
                                                        fline.Append($"{fwrite,8}");
                                                        writtenResultsFrequencies.Add(fwrite);
                                                    }
                                                }
                                                catch
                                                {
                                                    fline.Append("ER_ATX".PadLeft(8));
                                                }
                                                
                                            }
                                            else if (constellationInDic == "J")
                                            {
                                                if (satelliteInDictionaryCounted != "J01" && firstEpoch < qzss_defined_1)
                                                {
                                                    if (J01_2023.Contains(frequencyKeys))
                                                    {
                                                        fline.Append("N/A".PadLeft(8));
                                                    }
                                                    else
                                                    {
                                                        string fwrite = Math.Round(value * 100.0 / totalEpochsInFrequency, 2).ToString("F2");
                                                        fline.Append($"{fwrite,8}");
                                                        writtenResultsFrequencies.Add(fwrite);
                                                    }
                                                }
                                                else if (satelliteInDictionaryCounted == "J04" && firstEpoch < qzss_defined_2)
                                                {
                                                    if (J02_03_07_2022.Contains(frequencyKeys))
                                                    {
                                                        fline.Append("N/A".PadLeft(8));
                                                    }
                                                    else
                                                    {
                                                        string fwrite = Math.Round(value * 100.0 / totalEpochsInFrequency, 2).ToString("F2");
                                                        fline.Append($"{fwrite,8}");
                                                        writtenResultsFrequencies.Add(fwrite);
                                                    }
                                                }
                                                else
                                                {
                                                    string fwrite = Math.Round(value * 100.0 / totalEpochsInFrequency, 2).ToString("F2");
                                                    fline.Append($"{fwrite,8}");
                                                    writtenResultsFrequencies.Add(fwrite);
                                                }
                                            }
                                            else
                                            {
                                                string fwrite = Math.Round(value * 100.0 / totalEpochsInFrequency, 2).ToString("F2");
                                                fline.Append($"{fwrite,8}");
                                                writtenResultsFrequencies.Add(fwrite);
                                            }
                                        }
                                        else//it is zero, check if its in the specific blocks, if so, it's zero, if not, write N/A because that frequency is N/A in that block
                                        { //some receivers write .000000 when there is no data
                                            //this part might not be needed check the TotalFrequencies fill codes -todo

                                            if (correspondingBlocks[satelliteInDictionaryCounted] != null)
                                            {
                                                //is it in specific block
                                                if (blockSpecificFrequencies.ContainsKey(correspondingBlocks[satelliteInDictionaryCounted]))
                                                {
                                                    //is it a specific frequency in specific block

                                                    if (blockSpecificFrequencies[correspondingBlocks[satelliteInDictionaryCounted]].Contains(frequencyKeys.Substring(2, 3)))
                                                    {
                                                        fline.Append("0.00".PadLeft(8));
                                                        writtenResultsFrequencies.Add("0.00");
                                                    }
                                                    else
                                                    {
                                                        fline.Append("N/A".PadLeft(8));
                                                    }
                                                }
                                                else
                                                {
                                                    fline.Append("0.00".PadLeft(8));
                                                    writtenResultsFrequencies.Add("0.00");
                                                }
                                            }
                                            else
                                            {
                                                fline.Append("ER_ATX".PadLeft(8));
                                            }
                                        }
                                    }
                                    else // countedsatellites dictionary does not have that frequency for the selected Satellite in the file
                                    {
                                        if (constellationInDic == "G" || constellationInDic == "R")
                                        {
                                            //is it in the specific blocks?
                                            //string blockCheck = correspondingBlocks[satelliteInDictionaryCounted].ToString();
                                            if (correspondingBlocks[satelliteInDictionaryCounted] != null)
                                            {
                                                //is block specific
                                                if (blockSpecificFrequencies.ContainsKey(correspondingBlocks[satelliteInDictionaryCounted]))
                                                {
                                                    if (blockSpecificFrequencies[correspondingBlocks[satelliteInDictionaryCounted]].Contains(frequencyKeys.Substring(2, 3)))
                                                    {
                                                        fline.Append("0.00".PadLeft(8));
                                                        writtenResultsFrequencies.Add("0.00");
                                                    }
                                                    else
                                                    {
                                                        if (!allSpecificFrequenciesyConstellation[constellationInDic].Contains(frequencyKeys.Substring(2, 3)))
                                                        {
                                                            fline.Append("0.00".PadLeft(8));
                                                            writtenResultsFrequencies.Add("0.00");
                                                        }
                                                        else
                                                        {
                                                            fline.Append("N/A".PadLeft(8));
                                                        }

                                                    }

                                                }
                                                else
                                                {
                                                    //its not in specific block, but is the frequency specific?
                                                    //if (constellationInDic == "G" || constellationInDic == "R" || constellationInDic == "J") //QZSS defined individually
                                                    if (constellationInDic == "G" || constellationInDic == "R")
                                                    {
                                                        if (allSpecificFrequenciesyConstellation[constellationInDic].Contains(frequencyKeys.Substring(2, 3)))
                                                        {
                                                            fline.Append("N/A".PadLeft(8));
                                                        }
                                                        else
                                                        {
                                                            fline.Append("0.00".PadLeft(8));
                                                            writtenResultsFrequencies.Add("0.00");
                                                        }
                                                    }
                                                    else//no need to check for Beidou/Galileo
                                                    {
                                                        fline.Append("0.00".PadLeft(8));
                                                        writtenResultsFrequencies.Add("0.00");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                fline.Append("ER_ATX".PadLeft(8)); // cannot find the corresponding block for the satellite in the ATX file
                                            }
                                        }
                                        else if(constellationInDic == "J")
                                        {
                                            if (satelliteInDictionaryCounted != "J01" && firstEpoch < qzss_defined_1)
                                            {
                                                if (J01_2023.Contains(frequencyKeys))
                                                {
                                                    fline.Append("N/A".PadLeft(8));
                                                }
                                                else
                                                {
                                                    fline.Append("0.00".PadLeft(8));
                                                    writtenResultsFrequencies.Add("0.00");
                                                }
                                            }
                                            else if (satelliteInDictionaryCounted == "J04" && firstEpoch < qzss_defined_2)
                                            {
                                                if (J02_03_07_2022.Contains(frequencyKeys))
                                                {
                                                    fline.Append("N/A".PadLeft(8));
                                                }
                                                else
                                                {
                                                    fline.Append("0.00".PadLeft(8));
                                                    writtenResultsFrequencies.Add("0.00");
                                                }
                                            }
                                            else
                                            {
                                                fline.Append("0.00".PadLeft(8));
                                                writtenResultsFrequencies.Add("0.00");
                                            }
                                        }
                                           
                                    }
                                }
                                //take average, append fline add to output
                                if (writtenResultsFrequencies == null || !writtenResultsFrequencies.Any())
                                {
                                    //Console.WriteLine("The list is null or empty.");
                                }
                                else
                                {
                                    var validDoubles = writtenResultsFrequencies.Select(s =>
                                    {
                                        bool success = double.TryParse(s, out double result);
                                        return new { success, result };
                                    })
                                                                .Where(x => x.success) // Filter out unsuccessful parsing attempts
                                                                .Select(x => x.result); // Select the successfully parsed doubles

                                    if (!validDoubles.Any())
                                    {
                                        //Console.WriteLine("No valid double values were found in the list.");
                                    }
                                    else
                                    {
                                        double average = validDoubles.Average();
                                        string averageString = $"{average.ToString("F2"),6}";
                                        //fline.Append($"{average.ToString("F2"),12}");
                                        fline.Insert(7, " "+averageString+"  ");
                                    }
                                }
                                AvailabilityOutputLines.Add(fline.ToString().TrimEnd(','));
                            }

                            AvailabilityOutputLines.Add($"---{constellationKey}---");
                            AvailabilityOutputLines.Add("");
                        }


                    }
                }

            }
            //delete rinex currentRNXFile if it was compressed in the beginning
            if (compressedFile)
            {
                File.Delete(rinexFile);
            }
            return (AvailabilityOutputLines, MissingOutputLines);
        }
        private (List<string> AvailabilityOutputLines, List<string> MissingOutputLines) ProcessRinex2(string file, int index)
        {
            string fileExtension = Path.GetExtension(file).ToUpper();
            string rinexFile = "";
            string newExtension = fileExtension.Replace('D', 'O');
            bool compressedFile = false;
            if (Regex.IsMatch(fileExtension, @"^\.\d{2}D$"))
            {
                Process decompressionRinex = new Process();
                ProcessStartInfo info3 = new ProcessStartInfo();
                info3.FileName = "CRX2RNX.exe";
                info3.UseShellExecute = false;
                info3.CreateNoWindow = true;
                //info3.RedirectStandardOutput = true;
                decompressionRinex.StartInfo = info3;
                string args = "-f \"" + file + "\"";
                info3.Arguments = args;
                decompressionRinex.Start();
                decompressionRinex.WaitForExit();
                int exitCode = decompressionRinex.ExitCode;
                if (exitCode == 0 || exitCode == 2)
                {

                    rinexFile = Path.ChangeExtension(file, newExtension);
                    compressedFile = true;
                }
                else
                {
                    return (null, null);
                }
            }
            else
            {
                rinexFile = file;
            }
            string recType = "N/A";
            string antType = "N/A";
            string rinexVersion;
            double observationInterval = 0;
            bool intervalFound = false;
            string filename = Path.GetFileName(file);
            long totalBytes = new FileInfo(file).Length;
            int codecount = 0;
            int datablockLineCount4EachSat = 0;
            List<string> AvailabilityOutputLines = new List<string>();
            List<string> MissingOutputLines = new List<string>();
            List<DateTime> epochsAsDateTime = new List<DateTime>();
            Dictionary<string, Dictionary<string, int>> CountedFrequencies = new Dictionary<string, Dictionary<string, int>>();
            Dictionary<string, Dictionary<string, int>> TotalFrequencies = new Dictionary<string, Dictionary<string, int>>();
            Dictionary<string, Dictionary<string, double>> RatioFrequencies = new Dictionary<string, Dictionary<string, double>>();
            Dictionary<string, Dictionary<string, List<DateTime>>> MissingFrequencies = new Dictionary<string, Dictionary<string, List<DateTime>>>();
            

            List<string> codelist = new List<string>();

            long bytesRead = 0;
            long bytesRead2 = 0;
            using (StreamReader reader = new StreamReader(rinexFile))
            {
                //Get information from header
                string currentLine;
                while ((currentLine = reader.ReadLine()) != null)
                {
                    if (currentLine.Contains("END OF HEADER"))
                    {
                        break;
                    }
                    if (currentLine.Contains("INTERVAL"))
                    {
                        string[] parts = currentLine.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                        string intervalString = parts[0];
                        if (double.TryParse(intervalString, out double intervalValue))
                        {
                            observationInterval = intervalValue;
                            intervalFound = true;
                        }
                        else
                        {
                            //Cannot parse observationInterval
                        }

                    }
                    if (currentLine.Contains("RINEX VERSION"))
                    {
                        string[] parts = currentLine.Split(new char[0], StringSplitOptions.RemoveEmptyEntries);
                        rinexVersion = parts[0];
                    }
                    if (currentLine.Contains("TYPES OF OBSERV"))
                    {
                        try
                        {
                            codecount = Convert.ToInt32(currentLine.Substring(0, 6));
                        }
                        catch
                        {

                        }

                        for (int j = 6; j <= 55; j += 6)
                        {
                            string code = currentLine.Substring(j, 6);
                            if (code != "      ")
                            {
                                codelist.Add(code.Trim());
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                    if (currentLine.Contains("REC #"))
                    {
                        if (string.IsNullOrWhiteSpace(currentLine.Substring(20, 40)))
                        {

                        }
                        else
                        {
                            recType = currentLine.Substring(20, 40);
                        }

                    }
                    if (currentLine.Contains("ANT #"))
                    {
                        if (string.IsNullOrWhiteSpace(currentLine.Substring(20, 40)))
                        {

                        }
                        else
                        {
                            antType = currentLine.Substring(20, 40);
                        }

                    }
                }
                datablockLineCount4EachSat = (codecount + 5 - 1) / 5;
                while ((currentLine = reader.ReadLine()) != null)
                {
                    if (radioButton1.Checked)
                    {
                        bytesRead2 += currentLine.Length;
                        if (bytesRead2 % 10000 == 0)
                        {
                            int progress = (int)((bytesRead2 / (float)totalBytes) * 100);
                            UpdateProgress(index, progress);
                        }
                    }
                    if (isEpochLineRnx2(currentLine))
                    {
                        //Check if epoch is OK
                        string epochFlag = currentLine.Substring(26, 3);
                        if (int.TryParse(epochFlag, out int epochFlagValue))
                        {
                            if (epochFlagValue == 0)
                            {
                                int year = int.Parse(currentLine.Substring(1, 2).Trim());
                                int month = int.Parse(currentLine.Substring(4, 2).Trim());
                                int day = int.Parse(currentLine.Substring(7, 2).Trim());
                                int hour = int.Parse(currentLine.Substring(10, 2).Trim());
                                int minute = int.Parse(currentLine.Substring(13, 2).Trim());
                                double second = double.Parse(currentLine.Substring(15, 11).Trim());
                                int satelliteCountInepoch = int.Parse(currentLine.Substring(29, 3));
                                DateTime epochDateTime = new DateTime();
                                if (year >= 0 && year <= 91) // check back later, cddis daily rinex from 92 to 99
                                {
                                    year += 2000;
                                }
                                else
                                {
                                    year += 1900;
                                }
                                // The epochFlag is zero which means it OK continue processing.
                                bool isInteger = second % 1 == 0;
                                if (isInteger)
                                {
                                    epochDateTime = new DateTime(year, month, day, hour, minute, (int)second);
                                }
                                else
                                {
                                    epochDateTime = fixInterval(year, month, day, hour, minute, second);
                                }


                                epochsAsDateTime.Add(epochDateTime);
                                List<string> PNRList = new List<string>();
                                PNRList.AddRange(ReadPNRFromEpochRnx2(currentLine));
                                int additionalLinesToRead = 0;
                                if (satelliteCountInepoch > 12 && satelliteCountInepoch <= 24)
                                {
                                    additionalLinesToRead = 1;
                                }
                                else if (satelliteCountInepoch > 24 && satelliteCountInepoch <= 36)
                                {
                                    additionalLinesToRead = 2;
                                }
                                else if (satelliteCountInepoch > 36 && satelliteCountInepoch <= 48)
                                {
                                    additionalLinesToRead = 3;
                                }
                                else if (satelliteCountInepoch > 48 && satelliteCountInepoch <= 60)
                                {
                                    additionalLinesToRead = 4;
                                }
                                for (int i = 0; i < additionalLinesToRead; i++)
                                {
                                    string nextLine = reader.ReadLine();
                                    if (nextLine != null)
                                    {
                                        PNRList.AddRange(ReadPNRFromEpochRnx2(nextLine));
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                InitializeRnx2Dictionary(CountedFrequencies, PNRList, codelist);
                                InitializeRnx2Dictionary(TotalFrequencies, PNRList, codelist);

                                if (radioButton2.Checked)
                                {
                                    for (int i = 0; i < satelliteCountInepoch; i++)
                                    {
                                        string satOfIn = PNRList[i];
                                        List<string> totalFrequenciesOfSatellite = new List<string>();
                                        try
                                        {
                                            for (int j = 0; j < datablockLineCount4EachSat; j++)
                                            {
                                                string satelliteDataLine = reader.ReadLine();
                                                if (satelliteDataLine == null)
                                                {
                                                    break;
                                                }
                                                List<string> frequenciesInLine = new List<string>();
                                                int lineLength = satelliteDataLine.Length;
                                                int segment = 16;
                                                for (int k = 0; k < lineLength; k += segment)
                                                {
                                                    if (k + segment <= lineLength)
                                                    {
                                                        frequenciesInLine.Add(satelliteDataLine.Substring(k, segment));
                                                    }
                                                    else
                                                    {
                                                        frequenciesInLine.Add(satelliteDataLine.Substring(k, lineLength - k));
                                                    }
                                                }
                                                if (frequenciesInLine.Count < 5)
                                                {
                                                    int itemsToAdd = 5 - frequenciesInLine.Count;
                                                    for (int k = 0; k < itemsToAdd; k++)
                                                    {
                                                        frequenciesInLine.Add(" ");
                                                    }
                                                }
                                                foreach (string frequency in frequenciesInLine)
                                                {
                                                    if (totalFrequenciesOfSatellite.Count != codelist.Count)
                                                    {
                                                        totalFrequenciesOfSatellite.Add(frequency);
                                                    }

                                                }
                                                if (bytesRead % 10000 == 0)
                                                {
                                                    int progress = (int)((bytesRead / (float)totalBytes) * 100);
                                                    UpdateProgress(index, progress);
                                                }
                                                bytesRead += satelliteDataLine.Length;
                                            }
                                        }
                                        catch
                                        {
                                            break;
                                        }

                                        //populate dictionaries based on readings
                                        for (int k = 0; k < totalFrequenciesOfSatellite.Count; k++)
                                        {
                                            if (!string.IsNullOrWhiteSpace(totalFrequenciesOfSatellite[k]))
                                            {
                                                try
                                                {
                                                    double valueCheck = Convert.ToDouble(totalFrequenciesOfSatellite[k].Substring(0, 14));
                                                    if (valueCheck != 0)
                                                    {
                                                        CountedFrequencies[satOfIn][codelist[k]] += 1;
                                                    }
                                                }
                                                catch
                                                {

                                                }
                                                TotalFrequencies[satOfIn][codelist[k]] += 1;//using same structure unneccessary for the total count,
                                                                                            //count the epochs for each satellite, thats adequate
                                                                                            //-todo
                                            }
                                            else
                                            {
                                                TotalFrequencies[satOfIn][codelist[k]] += 1; //same...
                                            }
                                        }

                                    }
                                }
                            }
                            else
                            {
                                //epoch has either power failure or other problems check Rinex Manual for Epoch Flag
                            }
                        }
                        else
                        {
                            //Epoch flag couldnt be read, ?
                        }
                    }
                    else
                    {
                        continue;
                    }

                }
                int numberOfEpochsToCheck = 20;
                if (!intervalFound && epochsAsDateTime.Count > numberOfEpochsToCheck + 1)
                {
                    double[] intervalDifferences = new double[numberOfEpochsToCheck];
                    for (int i = 1; i <= numberOfEpochsToCheck; i++)
                    {
                        TimeSpan timeDifference = epochsAsDateTime[i] - epochsAsDateTime[i - 1];
                        double secondsDifference = timeDifference.TotalSeconds;
                        intervalDifferences[i - 1] = secondsDifference;
                    }
                    observationInterval = intervalDifferences.Where(x => x > 0).DefaultIfEmpty().Min();
                }
                DateTime firstEpoch = epochsAsDateTime.First();
                DateTime lastEpoch = epochsAsDateTime.Last();
                List<DateTime> expected_epochs = new List<DateTime>();
                DateTime current_time = firstEpoch;
                while (current_time <= lastEpoch)
                {
                    expected_epochs.Add(current_time);
                    current_time = current_time.AddSeconds(observationInterval);
                }
                List<DateTime> missing_epochs = expected_epochs.Except(epochsAsDateTime).ToList();
                TimeSpan timeSpan2 = TimeSpan.FromSeconds(observationInterval);
                List<(DateTime Start, DateTime End)> missingList2 = GroupConsecutiveMissingEpochs(missing_epochs, timeSpan2);
                string resultLine2 = "";
                if (missing_epochs.Count == 0)
                {
                    //resultLine2 = "Full";
                }
                else
                {
                    MissingOutputLines.Add("");
                    MissingOutputLines.Add("File Name: " + Path.GetFileName(file));
                    resultLine2 = "Total : " + missing_epochs.Count + " Epochs : " + PrintMissingIntervals(missingList2);
                    MissingOutputLines.Add(resultLine2);
                }
                TimeSpan totalDifferenceSpan = lastEpoch - firstEpoch;
                string totalObservationDuration = totalDifferenceSpan.ToString(@"d\-hh\:mm\:ss");
                double totalDifferenceInSeconds = totalDifferenceSpan.TotalSeconds + 30;
                double requiredEpochsCount = totalDifferenceInSeconds / observationInterval;
                double availableEpochsRatio = Math.Round(epochsAsDateTime.Count / requiredEpochsCount * 100, 2);
                UpdateProgress(index, 100);
                UpdateStatus(index, "Finished", availableEpochsRatio.ToString("F2"), totalObservationDuration);
                AvailabilityOutputLines.Add("");
                AvailabilityOutputLines.Add("File Name: " + Path.GetFileName(file) + " Epoch Availability(%): " + availableEpochsRatio.ToString("F2") + " Duration: " + totalObservationDuration);
                AvailabilityOutputLines.Add("REC: " + recType);
                AvailabilityOutputLines.Add("ANT: " + antType);
                //Calculate epoch availability
                if (radioButton2.Checked)
                {
                    //Availablity dictionary
                    foreach (var FrequencyDictionary in CountedFrequencies)
                    {
                        string FD_Key = FrequencyDictionary.Key;
                        Dictionary<string, int> CountedValues = FrequencyDictionary.Value;

                        if (TotalFrequencies.ContainsKey(FD_Key))
                        {
                            Dictionary<string, int> TotalValues = TotalFrequencies[FD_Key];
                            Dictionary<string, double> Availability = new Dictionary<string, double>();

                            foreach (var Counted in CountedValues)
                            {
                                string CKey = Counted.Key;
                                int CValue = Counted.Value;

                                if (TotalValues.ContainsKey(CKey))
                                {
                                    int totalValue = TotalValues[CKey];
                                    if (totalValue != 0) // Avoid division by zero
                                    {
                                        double ratio = ((double)CValue / totalValue) * 100.00;
                                        Availability[CKey] = ratio;
                                    }
                                    else
                                    {
                                        // Zero or N/A Check -todo
                                        Availability[CKey] = 0; // or handle as needed
                                    }
                                }
                            }

                            RatioFrequencies[FD_Key] = Availability;
                        }
                    }
                }
                Dictionary<string, Dictionary<string, Dictionary<string, double>>> ConstellationBasedDict = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>();

                foreach (var entry in RatioFrequencies)
                {
                    string key = entry.Key;
                    string category = key.Substring(0, 1);

                    if (!ConstellationBasedDict.ContainsKey(category))
                    {
                        ConstellationBasedDict[category] = new Dictionary<string, Dictionary<string, double>>();
                    }

                    ConstellationBasedDict[category][key] = entry.Value;
                }
                // Sort the dictionaries within each FrequencyDictionary
                foreach (var category in ConstellationBasedDict.Keys.ToList())
                {
                    var sortedDict = ConstellationBasedDict[category]
                        .OrderBy(kvp => kvp.Key)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                    ConstellationBasedDict[category] = sortedDict;
                }
                foreach (var constellationKey in userSelectedSys)
                {
                    if (ConstellationBasedDict.ContainsKey(constellationKey.Substring(0, 1)))
                    {
                        List<string> allSatellites = ConstellationBasedDict[constellationKey.Substring(0, 1)].Keys.ToList();
                        AvailabilityOutputLines.Add($"+++{constellationKey}+++");
                        string spaceBetweenElements = "     ";
                        string joinedString = string.Join(spaceBetweenElements, allSatellites);
                        //string formattedString = spaceAtStart + joinedString + spaceBetweenElements + "Average";
                        string formattedString = "       Average     " +joinedString+spaceBetweenElements;
                        AvailabilityOutputLines.Add(formattedString);
                        var SelectedConstellation = ConstellationBasedDict[constellationKey.Substring(0, 1)];
                        var frequencyTitles = SelectedConstellation.SelectMany(dict => dict.Value.Keys).Distinct().ToList();
                        // Prepare the table header
                        var table = new List<List<string>>();
                        foreach (var title in frequencyTitles)
                        {
                            var row = new List<string> { (constellationKey + "_" + title.Trim()) };
                            foreach (var Satellite in SelectedConstellation)
                            {
                                row.Add(Satellite.Value.ContainsKey(title) ? Satellite.Value[title].ToString("0.00").PadLeft(6) : string.Empty);
                            }
                            table.Add(row);
                        }
                        for (int i = 0; i < table.Count; i++)
                        {
                            double sum = 0;
                            int count = 0;
                            // Calculate the sum and count for averages
                            for (int j = 1; j < table[i].Count; j++)
                            {
                                if (double.TryParse(table[i][j], out double value))
                                {
                                    sum += value;
                                    count++;
                                }
                            }
                            // Calculate the average
                            string average = count > 0 ? (sum / count).ToString("F2") : string.Empty;

                            // Insert the average after the title (which is at index 0)
                            table[i].Insert(1, average);
                        }
                        // Now, add rows to AvailabilityOutputLines with the updated table
                        foreach (var row in table)
                        {
                            AvailabilityOutputLines.Add(string.Join("\t", row));
                        }

                        AvailabilityOutputLines.Add($"---{constellationKey}---");
                        AvailabilityOutputLines.Add("");
                    }
                }

            } // The StreamReader is automatically disposed here
            //delete rinex currentRNXFile if it was compressed in the beginning
            if (compressedFile)
            {
                File.Delete(rinexFile);
            }
            return (AvailabilityOutputLines, MissingOutputLines);
        }
        static void InitializeRnx2Dictionary(Dictionary<string, Dictionary<string, int>> dictionary, List<string> keys, List<string> subKeys)
        {
            foreach (string key in keys)
            {
                string pr = key.Substring(0, 1);
                if (!dictionary.ContainsKey(key))
                {
                    dictionary[key] = new Dictionary<string, int>();
                }
                foreach (string subKey in subKeys)
                {
                    if (!dictionary[key].ContainsKey(subKey))
                    {
                        dictionary[key][subKey] = 0;
                    }
                }
            }
        }
        static Dictionary<string, List<string>> parseFrequencies(List<string> dataLines)
        {
            Dictionary<string, List<string>> systemsDictionary = new Dictionary<string, List<string>>();
            for (int i = 0; i < dataLines.Count;)
            {
                string systemId = dataLines[i].Substring(0, 1);
                if (!systemsDictionary.ContainsKey(systemId))
                {
                    systemsDictionary.Add(systemId, new List<string>());
                }
                int codeCount = int.Parse(dataLines[i].Substring(1, 5));
                int linesToProcess = (int)Math.Ceiling((double)codeCount / 13);
                int startOffset = 6;
                for (int line = 0; line < linesToProcess; line++)
                {
                    for (int offset = startOffset; offset < startOffset + 4 * Math.Min(13, codeCount - line * 13); offset += 4)
                    {
                        string code = dataLines[i + line].Substring(offset, 4).Trim();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            systemsDictionary[systemId].Add(systemId + "_" + code);
                        }
                    }
                }
                i += linesToProcess;
            }
            return systemsDictionary;
        }
        static Dictionary<string, Dictionary<string, Dictionary<string, int>>> parseObservationFrequenciesFromRinex3Header(List<string> dataLines)
        {
            Dictionary<string, Dictionary<string, Dictionary<string, int>>> headerOBSFRQDict =
            new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();
            for (int i = 0; i < dataLines.Count;)
            {
                string systemId = dataLines[i].Substring(0, 1);
                if (!headerOBSFRQDict.ContainsKey(systemId))
                {
                    headerOBSFRQDict[systemId] = new Dictionary<string, Dictionary<string, int>>();
                }
                int codeCount = int.Parse(dataLines[i].Substring(1, 5));
                int linesToProcess = (int)Math.Ceiling((double)codeCount / 13);
                int startOffset = 6;
                for (int line = 0; line < linesToProcess; line++)
                {
                    for (int offset = startOffset; offset < startOffset + 4 * Math.Min(13, codeCount - line * 13); offset += 4)
                    {
                        string code = systemId + "_" + dataLines[i + line].Substring(offset, 4).Trim();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            if (!headerOBSFRQDict[systemId].ContainsKey(code))
                            {
                                headerOBSFRQDict[systemId][code] = new Dictionary<string, int>();
                            }
                        }
                    }
                }
                i += linesToProcess;
            }
            return headerOBSFRQDict;
        }
        static Dictionary<string, Dictionary<string, Dictionary<string, List<DateTime>>>> parseObservationFrequenciesMissing(List<string> dataLines)
        {
            Dictionary<string, Dictionary<string, Dictionary<string, List<DateTime>>>> tempOBSDict =
            new Dictionary<string, Dictionary<string, Dictionary<string, List<DateTime>>>>();

            for (int i = 0; i < dataLines.Count;)
            {
                string systemId = dataLines[i].Substring(0, 1);
                if (!tempOBSDict.ContainsKey(systemId))
                {
                    tempOBSDict[systemId] = new Dictionary<string, Dictionary<string, List<DateTime>>>();
                }
                int codeCount = int.Parse(dataLines[i].Substring(1, 5));
                int linesToProcess = (int)Math.Ceiling((double)codeCount / 13);
                int startOffset = 6;
                for (int line = 0; line < linesToProcess; line++)
                {
                    for (int offset = startOffset; offset < startOffset + 4 * Math.Min(13, codeCount - line * 13); offset += 4)
                    {
                        string code = systemId + "_" + dataLines[i + line].Substring(offset, 4).Trim();
                        if (!string.IsNullOrWhiteSpace(code))
                        {
                            if (!tempOBSDict[systemId].ContainsKey(code))
                            {
                                tempOBSDict[systemId][code] = new Dictionary<string, List<DateTime>>();
                            }
                        }
                    }
                }
                i += linesToProcess;
            }
            return tempOBSDict;
        }
        static Dictionary<String, List<String>> parseObservationFrequencies2dataSet(List<string> dataLines)
        {
            Dictionary<String, List<String>> consellationFrequencies = new Dictionary<String, List<String>>();

            for (int i = 0; i < dataLines.Count;)
            {
                string consellationId = dataLines[i].Substring(0, 1);

                consellationFrequencies.Add(consellationId, new List<String>());

                int frequencyCount = int.Parse(dataLines[i].Substring(1, 5));
                int linesToProcess = (int)Math.Ceiling((double)frequencyCount / 13);
                int startOffset = 6;
                for (int line = 0; line < linesToProcess; line++)
                {
                    for (int offset = startOffset; offset < startOffset + 4 * Math.Min(13, frequencyCount - line * 13); offset += 4)
                    {
                        string frequency = consellationId + "_" + dataLines[i + line].Substring(offset, 4).Trim();
                        if (!string.IsNullOrWhiteSpace(frequency))
                        {
                            if (!consellationFrequencies[consellationId].Contains(frequency))
                            {
                                consellationFrequencies[consellationId].Add(frequency);
                            }
                        }
                    }
                }
                i += linesToProcess;
            }
            return consellationFrequencies;
        }
        static bool isEpochLineRnx2(string line)
        {
            // Example epoch currentLine format: 23 10 29 00 00 30.0000000
            string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 6) // There is not enough data for date string, pass
            {
                return false;
            }
            string dateString = $"{parts[0]} {parts[1]} {parts[2]} {parts[3]} {parts[4]} {parts[5]}";
            DateTime dateValue;
            string[] formats = {
            "yy M d H m s.fffffff",
            "yy MM dd HH mm ss.fffffff",
            "yy M d H m s.ffffff",
            "yy MM dd HH mm ss.ffffff",
            // will check later for ss 
            //yy MM dd HH mm ss?
        };
            bool success = false;

            // Try each format until one succeeds
            foreach (string format in formats)
            {
                success = DateTime.TryParseExact(dateString,
                                                 format,
                                                 CultureInfo.InvariantCulture,
                                                 DateTimeStyles.None,
                                                 out dateValue);
                if (success)
                {
                    break;
                }
            }

            return success;
        }
        static List<string> ReadPNRFromEpochRnx2(string epochLine)
        {
            // Define constants for magic numbers
            const int startIdx = 32;
            const int pnrLength = 3;
            const int maxPNRCount = 12;
            const int maxReadLength = maxPNRCount * pnrLength;
            int readLength = Math.Min(epochLine.Length - startIdx, maxReadLength);
            // Extract the PNR segment from the epoch currentLine starting from the 32nd character
            string pnrSegment = epochLine.Substring(startIdx, readLength).Trim();
            var pnrList = new List<string>();
            // Iterate over the PNR segment and extract PNRs of 3 characters in length
            for (int i = 0; i < readLength; i += pnrLength)
            {
                string pnr = pnrSegment.Substring(i, pnrLength).Trim();
                if (string.IsNullOrEmpty(pnr))
                {
                    break;
                }
                pnrList.Add(pnr);
            }
            return pnrList;
        }
        static bool HasNullCharacters(string filePath)
        {
            try
            {
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    int b;
                    while ((b = fs.ReadByte()) != -1)
                    {
                        if (b == 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading currentRNXFile: {ex.Message}");
                return true;
            }
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (processDone && !resultsSaved)
            {
                // Ask the user if they are sure they want to exit
                var response = MessageBox.Show("Results not saved. Are you sure you want to exit?",
                                               "Confirm Exit",
                                               MessageBoxButtons.YesNo,
                                               MessageBoxIcon.Question);

                // If the user clicked 'No', cancel the closing process
                if (response == DialogResult.No)
                {
                    e.Cancel = true;
                }
            }
        }
        public static bool RemoveNullChars(string filePath)
        {
            try
            {
                byte[] allBytes;
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    allBytes = new byte[fs.Length];
                    fs.Read(allBytes, 0, (int)fs.Length);
                }

                byte[] nonNullBytes = Array.FindAll(allBytes, b => b != 0);

                using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    fs.Write(nonNullBytes, 0, nonNullBytes.Length);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing currentRNXFile: {ex.Message}");
                return false;
            }
        }
        static List<(DateTime Start, DateTime End)> GroupConsecutiveMissingEpochs(List<DateTime> missingEpochs, TimeSpan interval)
        {
            List<(DateTime Start, DateTime End)> missingIntervals = new List<(DateTime Start, DateTime End)>();
            if (missingEpochs.Count == 0) return missingIntervals;
            DateTime intervalStart = missingEpochs[0];
            DateTime intervalEnd = intervalStart;
            for (int i = 1; i < missingEpochs.Count; i++)
            {
                if (missingEpochs[i] == intervalEnd.Add(interval))
                {
                    intervalEnd = missingEpochs[i];
                }
                else
                {
                    if (intervalStart == intervalEnd)
                    {
                        missingIntervals.Add((intervalStart, intervalStart));
                    }
                    else
                    {
                        missingIntervals.Add((intervalStart, intervalEnd));
                    }
                    intervalStart = missingEpochs[i];
                    intervalEnd = intervalStart;
                }
            }
            // Add the last observationInterval
            if (intervalStart == intervalEnd)
            {
                missingIntervals.Add((intervalStart, intervalStart));
            }
            else
            {
                missingIntervals.Add((intervalStart, intervalEnd));
            }
            return missingIntervals;
        }
        static string PrintMissingIntervals(List<(DateTime Start, DateTime End)> missingIntervals)
        {
            List<string> formattedIntervals = new List<string>();

            foreach (var interval in missingIntervals)
            {
                string startTime = interval.Start.ToString("HH:mm:ss");
                string endTime = interval.End.ToString("HH:mm:ss");

                if (interval.Start == interval.End)
                {
                    formattedIntervals.Add(startTime);
                }
                else
                {
                    formattedIntervals.Add($"from({startTime}) to({endTime})");
                }
            }
            string outLine = string.Join(" ", formattedIntervals);
            return outLine;
            //Console.WriteLine(string.Join(" ", formattedIntervals));
        }
        private static DateTime fixInterval(int year, int month, int day, int hour, int minute, double second)
        {
            // Round the seconds
            int roundedSeconds = (int)Math.Round(second);
            if (roundedSeconds == 60)
            {
                roundedSeconds = 0;
                minute += 1;
                if (minute == 60)
                {
                    minute = 0;
                    hour += 1;
                    if (hour == 24)
                    {
                        hour = 0;
                        day += 1;
                    }
                }
            }
            // Handle month/day rollover
            while (true)
            {
                int daysInMonth = DateTime.DaysInMonth(year, month);
                if (day <= daysInMonth)
                {
                    break;
                }
                day -= daysInMonth;
                month += 1;
                if (month > 12)
                {
                    month = 1;
                    year += 1;
                }
            }
            return new DateTime(year, month, day, hour, minute, roundedSeconds);
        }
        private void UpdateProgress(int itemIndex, int progress)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateProgress(itemIndex, progress)));
                return;
            }

            importedFilesListView.Items[itemIndex].SubItems[4].Text = $"{progress}%";
            importedFilesListView.Invalidate(); // Force a complete redraw
        }
        private void UpdateStatus(int itemIndex, string status, string availability, string duration)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => UpdateStatus(itemIndex, status, availability, duration)));
                return;
            }

            // Update the status column
            importedFilesListView.Items[itemIndex].SubItems[5].Text = status;
            importedFilesListView.Items[itemIndex].SubItems[2].Text = availability;
            importedFilesListView.Items[itemIndex].SubItems[3].Text = duration;
            // Set the background color based on the status
            if (status == "Waiting...")
            {
                importedFilesListView.Items[itemIndex].SubItems[5].BackColor = Color.Tomato;
            }
            else if (status == "Working...")
            {
                importedFilesListView.Items[itemIndex].SubItems[5].BackColor = Color.Green;
            }
            else if (status == "Finished")
            {
                importedFilesListView.Items[itemIndex].SubItems[5].BackColor = Color.Wheat;
            }
            else if (status == "File Error")
            {
                importedFilesListView.Items[itemIndex].SubItems[5].BackColor = Color.Red;
            }
            else
            {
                // Set the default background color if status doesn't match any condition
                importedFilesListView.Items[itemIndex].SubItems[5].BackColor = Color.White;
            }


        }
    }
}
