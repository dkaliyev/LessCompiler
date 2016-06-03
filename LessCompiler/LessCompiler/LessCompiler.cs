using dotless.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LessCompiler
{
    public class LessCompiler
    {
        const string COMMENTS_REGEX = @"\/\*[^*]*\*+([^/*][^*]*\*+)*\/";
        const string RULES_REGEX = @"[-\\a-zA-Z0-9#>,.@:\s()\[\]""=_^*+~]+(\{[-=a-zA-Z0-9:#?();""%\s\\.',_/\{\}*!]*\}\s\}|\{[-=a-zA-Z0-9:#?();""%\s\\.',_/*!]*\})";
        const string INNER_RULES_REGEX = @"[-\\a-zA-Z0-9#>,.@:\s()\[\]""=_^*+~]+(\{[-=a-zA-Z0-9:#?();""%\s\\.',_/*!]*\})";
        const string RULES_BODY_REGEX = @"(\{[-=a-zA-Z0-9:#?();""%\s\\.',_/\{\}*!]*\}\s\}|\{[-=a-zA-Z0-9:#?();""%\s\\.',_/*!]*\})";
        const string MEDIA_REGEX = @"@media[-a-zA-Z0-9:()@.,/\s]*\{[-a-zA-Z0-9:#?+();""\^~=>%\s\\.',_\{\}\[\]/*!]*\}\s*\}";
        const string MEDIA_HEADER_REGEX = @"@media[-a-zA-Z0-9:@().,/\s]*";
        const string MEDIA_BODY_REGEX = @"\{[-a-zA-Z0-9:#?+();""\^~=>%\s\\.',_\{\}\[\]/*!]*\}\s*\}";
        const int RULES_LIMIT = 4095;

        ///<summary>
        ///Compiles less file into multiple css files if total number of css selectors is > 4095. 
        ///<para>Creates "ie9" folder at the same level as less file and puts css files in there.
        ///The output "style.css" will import other css files.</para>
        ///<param name="absolutePath">Absolute path to less file.</param>
        /// </summary>
        public static string CompileLess(string absolutePath)
        {
            Trace.WriteLine("Compilation process started...");
            var files_list = new List<string>();
            var dotlessConfig = new dotless.Core.configuration.DotlessConfiguration();
            //dotlessConfig.Web = true;
            dotlessConfig.RootPath = "../";
            var fileLocation = absolutePath;
            var relDir = absolutePath.Substring(0, absolutePath.LastIndexOf("\\"));
            var currentDir = relDir;
            var count = 0;
            if (File.Exists(fileLocation))
            {
                
                using (var file = new System.IO.StreamReader(fileLocation))
                {
                    Directory.SetCurrentDirectory(currentDir);
                    string parsed = Less.Parse(file.ReadToEnd(), dotlessConfig);
                    parsed = RemoveComments(parsed);
                    var rules = Regex.Matches(parsed, RULES_REGEX);
                    foreach (var rule in rules)
                    {
                        var value = rule.ToString();
                        count += value.Split(',').Count();
                    }
                    //replace with "count > RULES_LIMIT" later
                    if (count > RULES_LIMIT)
                    {
                        var tmp = "";

                        var file_and_contents_dic = new Dictionary<string, string>();

                        var styleSheet = new Stylesheet();
                        var medias = GetMedias(parsed, out tmp);

                        var reminder_rules = GetRules(tmp, RULES_REGEX);

                        styleSheet.Medias = medias;
                        styleSheet.Rules = reminder_rules;

                        var rules_files = MakeFilesFromRules(reminder_rules);
                        var media_files = MakeFilesFromMedia(medias);
                        rules_files.AddRange(media_files);
                        var files = BuildFileNames(rules_files);
                        files_list = files.Keys.ToList();
                        AssembleFiles(files, currentDir, relDir);
                    }
                    file.Close();
                }
                
                Trace.WriteLine("Compilation process ended. File is closed.");
            }
            return string.Format("{0}/ie9/style.css", relDir);
        }

        private static string RemoveComments(string input)
        {
            var comment_matches = Regex.Matches(input, COMMENTS_REGEX);

            var dev_string = input;

            foreach (var match in comment_matches)
            {
                var comment_match_string = match.ToString();
                var comment_index = dev_string.IndexOf(comment_match_string);

                dev_string = comment_index > 0 ? dev_string.Remove(comment_index, comment_match_string.Count()) : dev_string;
            }

            return dev_string;
        }

        private static List<Media> GetMedias(string input, out string reminder)
        {
            var medias = new List<Media>();

            var media_matches = Regex.Matches(input, MEDIA_REGEX);

            var dev_string = input;

            foreach (var media_match in media_matches)
            {
                var media_match_string = media_match.ToString();
                var media_header = Regex.Match(media_match_string, MEDIA_HEADER_REGEX).Value;
                var media_body = Regex.Match(media_match_string, MEDIA_BODY_REGEX).Value;

                var rules = GetRules(media_body, INNER_RULES_REGEX);

                medias.Add(new Media()
                {
                    EntireMedia = media_match_string,
                    MediaHeader = media_header,
                    MediaBody = media_body,
                    Rules = rules
                });

                var media_index = dev_string.IndexOf(media_match_string);

                dev_string = media_index > 0 ? dev_string.Remove(media_index, media_match_string.Count()) : dev_string;
            }
            reminder = dev_string;
            return medias;
        }

        private static List<Rule> GetRules(string input, string regex)
        {
            var rules_matches = Regex.Matches(input, regex);

            var rules = new List<Rule>();

            foreach (var match in rules_matches)
            {
                var rule = GetRule(match.ToString());
                rules.Add(rule);
            }

            return rules;
        }

        private static List<string> MakeFilesFromRules(List<Rule> rules)
        {
            var list = new List<string>();
            var current_selectors_count = 0;
            var file_content = "";
            foreach (var reminder_rule in rules)
            {
                current_selectors_count += reminder_rule.SelectorCount;
                if (current_selectors_count > RULES_LIMIT)
                {
                    current_selectors_count = reminder_rule.SelectorCount;
                    list.Add(file_content);
                    file_content = reminder_rule.EntireRule;
                }
                else
                {
                    file_content += reminder_rule.EntireRule;
                }
            }
            list.Add(file_content);
            return list;
        }

        private static List<string> MakeFilesFromMedia(List<Media> medias)
        {
            var list = new List<string>();
            var current_selectors_count = 0;
            var file_content = "";

            var splited_media_list = new List<Media>();
            foreach (var media in medias)
            {
                splited_media_list.AddRange(SplitMedias(media));
            }

            foreach (var media in splited_media_list)
            {
                media.Rules.ForEach(x => current_selectors_count += x.SelectorCount);

                if (current_selectors_count > RULES_LIMIT)
                {
                    list.Add(file_content);
                    current_selectors_count = 0;
                    media.Rules.ForEach(x => current_selectors_count += x.SelectorCount);
                    file_content = media.EntireMedia;
                }
                else
                {
                    file_content += media.EntireMedia;
                }
            }
            list.Add(file_content);

            return list;
        }

        private static List<Media> SplitMedias(Media media)
        {
            var list = new List<Media>();
            var count = 0;
            var newMedia = new Media();
            newMedia.MediaHeader = media.MediaHeader;
            newMedia.MediaBody = "";
            newMedia.Rules = new List<Rule>();
            list.Add(newMedia);
            foreach (var rule in media.Rules)
            {
                count += rule.SelectorCount;
                if (count > RULES_LIMIT)
                {
                    count = rule.SelectorCount;
                    newMedia = new Media();
                    newMedia.MediaHeader = media.MediaHeader;
                    newMedia.MediaBody = rule.EntireRule;
                    newMedia.Rules = new List<Rule>() { rule };
                    list.Add(newMedia);
                }
                else
                {
                    newMedia.MediaBody += rule.EntireRule;
                    newMedia.Rules.Add(rule);
                }
            }

            foreach (var m in list)
            {
                m.EntireMedia = string.Format("{0} {{ {1} }}", m.MediaHeader, m.MediaBody);
            }

            return list;
        }

        private static Dictionary<string, string> BuildFileNames(List<string> contents)
        {
            var file_name_counter = 0;
            string tmp_file_name = "";
            var file_and_contents_dic = new Dictionary<string, string>();

            foreach (var content in contents)
            {
                tmp_file_name = "style" + file_name_counter + ".css";
                file_and_contents_dic.Add(tmp_file_name, content);
                file_name_counter++;
            }
            return file_and_contents_dic;
        }

        private static void AssembleFiles(Dictionary<string, string> files, string baseDir, string relDir)
        {
            Trace.WriteLine("Assembling the files.");
            var dirName = string.Format("{0}\\ie9", baseDir);
            if (!Directory.Exists(dirName))
            {
                Directory.CreateDirectory(dirName);
            }
            else
            {
                DeleteFilesInDirectory(dirName);
            }

            Directory.SetCurrentDirectory(dirName);
            var final_style_content = "";

            foreach (var d in files)
            {
                File.WriteAllText(d.Key, d.Value);
                final_style_content += string.Format("@import url(\"{0}\"); \n", d.Key);
            }

            File.WriteAllText("style.css", final_style_content);
            Trace.WriteLine("Assembly process finished.");
        }

        private static void DeleteFilesInDirectory(string dirName)
        {
            var dir = new DirectoryInfo(dirName);
            foreach (var file in dir.GetFiles())
            {
                file.Delete();
            }
        }

        private static Rule GetRule(string input)
        {
            var rule_body = Regex.Match(input, RULES_BODY_REGEX).Value;
            var rule_header = input.Substring(0, input.IndexOf(rule_body));
            var selector_count = rule_header.Split(',').Count();
            return new Rule()
            {
                EntireRule = input,
                Body = rule_body,
                Header = rule_header,
                SelectorCount = selector_count
            };
        }

        private class Media
        {
            public string EntireMedia { get; set; }
            public string MediaHeader { get; set; }

            public string MediaBody { get; set; }

            public List<Rule> Rules { get; set; }
        }

        private class Rule
        {
            public string EntireRule { get; set; }
            public string Header { get; set; }
            public string Body { get; set; }

            public int SelectorCount { get; set; }
        }

        private class Stylesheet
        {
            public List<Media> Medias { get; set; }

            public List<Rule> Rules { get; set; }
        }
    }
}
