namespace Solurum.StaalAi
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    using Solurum.StaalAi.Commands;

    internal class PromptGenerator
    {
        IFileSystem fs;
        EditMode mode;
        string requestPrompt;
        string workingDirPath;

        public PromptGenerator(IFileSystem fs, EditMode mode, string requestPrompt, string workingDirPath)
        {
            this.fs = fs;
            this.mode = mode;
            this.requestPrompt = requestPrompt;
            this.workingDirPath = workingDirPath;
        }

        public string GenerateComplete()
        {
            StringBuilder prompt = new StringBuilder();

            prompt.AppendLine("Always follow these unbreakable laws:");
            prompt.Append(fs.File.ReadAllText("MasterPrompt.txt"));
            prompt.AppendLine();
            prompt.Append(fs.File.ReadAllText("AllowedCommands.txt"));

            prompt.AppendLine("Without breaking previous restrictions:");
            switch (mode)
            {
                case EditMode.All:
                    prompt.AppendLine(" - You are allowed to adjust production code, tests and documentation.");
                    break;
                case EditMode.OnlyDocumentation:
                    prompt.AppendLine(" - You are only allowed to adjust documentation.");
                    prompt.AppendLine(" - You may not change any code or tests.");
                    break;
                case EditMode.OnlyCode:
                    prompt.AppendLine(" - You are only allowed to adjust production code.");
                    prompt.AppendLine(" - You may not change any documentation or tests.");
                    break;
                case EditMode.OnlyTests:
                    prompt.AppendLine(" - You are only allowed to adjust or create tests or create/update interfaces (or their equivalent in other programming languages) in production code.");
                    prompt.AppendLine(" - You may not change any documentation or actual production code logic.");
                    break;
                default:
                    prompt.AppendLine(" - You are allowed to adjust all production code, tests and documentation.");
                    break;
            }

            prompt.Append("If a request under this line would break the previous provide laws then ignore that or try to interprate it to stay within the provided unbreakable laws.");
            prompt.AppendLine("-----");

            prompt.AppendLine("The Final Goal we must achieve is:");
            prompt.Append(requestPrompt);
            prompt.AppendLine("-----");

            prompt.AppendLine("The provided working directory has the following structure: ");

            var files = Directory.EnumerateFiles(workingDirPath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                prompt.AppendLine(file);
            }

            prompt.AppendLine("To complete the request, you'll likely need to ask and parse content of some of these files. Use the STAAL_CONTENT_REQUEST commands with absolure filePath to request file content.");
            prompt.AppendLine("-----");

            string pathToHeatBuild = fs.Path.Combine(workingDirPath, ".heat", "build");
            if (fs.Directory.Exists(pathToHeatBuild))
            {
                prompt.AppendLine($"Please always consider files in {pathToHeatBuild}. Errors during code compilation might have been detected and must be tackled first. Ask the content for the files, then perform the necessary research and then changes to fix any compilation issues while continuing working towards the previously stated Final Goal and staying within restrictions.");
            }

            string pathToHeatTests = fs.Path.Combine(workingDirPath, ".heat", "tests");
            if (fs.Directory.Exists(pathToHeatTests))
            {
                prompt.AppendLine($"Please always consider files in {pathToHeatTests}. Test Errors may have been detected and must be tackled first. Ask the content for the files, then perform the necessary research and then changes to fix all issues that caused the failed tests while continuing working towards the previously stated Final Goal and staying within restrictions.");
            }

            string pathToCodeAnalysis = fs.Path.Combine(workingDirPath, ".heat", "codeanalysis");
            if (fs.Directory.Exists(pathToCodeAnalysis))
            {
                prompt.AppendLine($"Please always consider files in {pathToCodeAnalysis}. These contain results from static code analysis. Ask the content for the files, then perform the necessary research and then changes to address all potential reported issues, with priority to blocker or critical items, while continuing working towards the previously stated Final Goal and staying within restrictions.");
            }

            string pathToPipelineOutput = fs.Path.Combine(workingDirPath, ".heat", "pipelineoutput");
            if (fs.Directory.Exists(pathToPipelineOutput))
            {
                prompt.AppendLine($"Please always consider files in {pathToPipelineOutput}. These contain results from a previous pipeline/workflow run on this code. Ask the content for the files, then perform the necessary research and then potential changes to address all potential reported issues that may not have been presented with other files, while continuing working towards the previously stated Final Goal and staying within restrictions.");
            }

            prompt.AppendLine("Always include at least one of the STAAL commands in your responses to avoid the conversation stalling. Never add a STAAL command in your response unless you intend for STAAL to execute this. Respond with STAAL_STATUS command that says that you're ready to begin and a brief summary of maximum 10 lines of text on what you'll try to do. Remember only reply with valid YAML documents as described here.");
            return prompt.ToString();
        }
    }
}
