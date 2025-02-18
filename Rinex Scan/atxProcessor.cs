using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rinex_Scan
{
    public class atxProcessor
    {
        public static List<DateRange> ReadRangesFromFormattedATX(string filePath)
        {
            //R04, R05, R12, R21 Glonass M+ SV755+
            //These satellites in Glonass M block, if SV755+ they are M+, make an exception for these
            List<DateRange> dateRanges = new List<DateRange>();
            if (File.Exists(filePath))
            {
                using (StreamReader reader = new StreamReader(filePath))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] parts = line.Split('&');
                        if (parts.Length == 6)
                        {
                            string satellitePRN = parts[2].Trim();
                            DateTime startDate = DateTime.ParseExact(parts[4].Trim(), "yyyy-M-d-H-m-s.fffffff", CultureInfo.InvariantCulture);
                            DateTime endDate;
                            if (!string.IsNullOrEmpty(parts[5].Trim()))
                                endDate = DateTime.ParseExact(parts[5].Trim(), "yyyy-M-d-H-m-s.fffffff", CultureInfo.InvariantCulture);
                            else
                                endDate = DateTime.MaxValue;
                            string whichBlock = parts[1].Trim();
                            dateRanges.Add(new DateRange(startDate, endDate, whichBlock, satellitePRN));
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("File not found.");
            }
            return dateRanges;
        }
        public static string GetCorrespondingBlock(List<DateRange> dateRanges, DateTime date, string satellitePRN)
        {
            foreach (var range in dateRanges)
            {
                string idendificationMark = range.SatellitePRN.Substring(0, 1);
                if (range.SatellitePRN.Equals(satellitePRN, StringComparison.OrdinalIgnoreCase) && range.IsDateInRange(date))
                {
                    return idendificationMark + "-" + range.SatBlock;
                }
            }
            return null;
        }
        public Dictionary<string, List<string>> SpecificTypes()
        {
            //Read the GNSS_Types file and define frequencies for specifig Blocks
            Dictionary<string, List<string>> satTypes2 = new Dictionary<string, List<string>>();
            string[] types = File.ReadAllLines("BLOCK_TYPES_SPECIFIC_FREQUENCIES.txt");
            foreach (var line in types)
            {
                if (!line.Contains("#"))
                {
                    var parts = line.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2)
                    {
                        throw new FormatException("Line is not in correct format");
                    }
                    var key = parts[0].Trim();
                    var values = parts[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(p => p.Trim()).ToList();
                    satTypes2[key] = values;
                }

            }
            return satTypes2;
        }
        public Dictionary<string, List<string>> GetPRNBasedBlockDict(string filepath)
        {
            string atxfile = filepath;
            Console.WriteLine("Read Started :" + atxfile);
            string[] atxcontent = File.ReadAllLines(atxfile);
            Dictionary<string, List<string>> PRNBasedBlockDict = new Dictionary<string, List<string>>();
            //Find the End of Header then, extract Types and Valid Dates
            #region validdates
            List<string> lines = new List<string>();
            int EoH = 0;
            for (int i = 0; i < atxcontent.Length; i++)
            {
                if (atxcontent[i].Contains("END OF HEADER"))
                {
                    EoH = i; break;
                }
            }
            string type = "";
            string validFrom = "";
            string validUntil = "";
            for (int i = EoH; i < atxcontent.Length; i++)
            {
                string line = atxcontent[i];
                if (line.Contains("TYPE / SERIAL NO"))
                {
                    string block = line.Substring(0, 20).Trim();
                    string PRN = line.Substring(20, 3).Trim();
                    string SVN = line.Substring(41, 3).Trim();

                    if (PRN == "R04" || PRN == "R05" || PRN == "R12" || PRN == "R21")
                    {
                        if (int.TryParse(SVN, out int checkSVN) && block == "GLONASS-M")
                        {
                            if (checkSVN > 755)
                            {
                                block = "GLONASS-M+";
                            }
                            type = block + " & " + PRN + " & " + SVN;
                        }
                        else
                        {
                            type = block + " & " + PRN + " & " + "Invalid SVN";
                        }
                    }
                    else
                    {
                        type = block + " & " + PRN + " & " + SVN;
                    }
                }

                else if (line.Contains("VALID FROM"))
                {
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 6)
                    {
                        validFrom = parts[0] + "-" + parts[1] + "-" + parts[2] + "-" + parts[3] + "-" + parts[4] + "-" + parts[5];
                    }
                }
                else if (line.Contains("VALID UNTIL"))
                {
                    string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 6)
                    {
                        validUntil = parts[0] + "-" + parts[1] + "-" + parts[2] + "-" + parts[3] + "-" + parts[4] + "-" + parts[5];
                    }
                }
                if ((i + 1) < atxcontent.Length && atxcontent[i + 1].Contains("SINEX CODE"))
                {
                    if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(validFrom))
                    {
                        lines.Add(i.ToString() + "&" + type + " & " + validFrom + " & " + validUntil);
                        type = "";
                        validFrom = "";
                        validUntil = "";
                    }
                }
                string SatCheck = line.Substring(20, 1).Trim();
                if (line.Contains("TYPE / SERIAL NO") && (SatCheck != "G" && SatCheck != "R" && SatCheck != "E" && SatCheck != "C" && SatCheck != "J"))
                {
                    Console.WriteLine("Read until line:" + i);
                    break;
                }
            }
            #endregion
            //Organize in dictionary based on PNR and return PNRBasedBlockDict
            #region pnr_dictionary
            foreach (string line in lines)
            {
                string[] parts = line.Split('&');
                string key = parts[2].Trim();
                if (!PRNBasedBlockDict.ContainsKey(key))
                {
                    PRNBasedBlockDict[key] = new List<string>();
                }
                PRNBasedBlockDict[key].Add(line);
            }
            File.WriteAllLines("formatted_" + Path.GetFileNameWithoutExtension(atxfile), lines);
            return PRNBasedBlockDict;

            #endregion
        }
    }
    public class DateRange
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string SatBlock { get; set; }
        public string SatellitePRN { get; set; }

        public DateRange(DateTime startDate, DateTime endDate, string satelliteBlock, string satellitePRN)
        {
            StartDate = startDate;
            EndDate = endDate;
            SatBlock = satelliteBlock;
            SatellitePRN = satellitePRN;
        }

        public bool IsDateInRange(DateTime date)
        {
            return date >= StartDate && date <= EndDate;
        }
    }
}
