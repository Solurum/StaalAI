using System.IO;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Solurum.StaalAi.AICommands;

namespace Solurum.StaalAi.Tests.AICommands
{
    [TestClass]
    public class StaalGetWorkingDirectoryStructureTests
    {
        [TestMethod]
        public void GetAllFilePaths_ReturnsAllFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "StaalAI_GetAllFilePaths_" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            try
            {
                var d1 = Path.Combine(root, "a");
                var d2 = Path.Combine(root, "b");
                Directory.CreateDirectory(d1);
                Directory.CreateDirectory(d2);
                var f1 = Path.Combine(d1, "x.txt");
                var f2 = Path.Combine(d2, "y.txt");
                File.WriteAllText(f1, "x");
                File.WriteAllText(f2, "y");

                var result = StaalGetWorkingDirectoryStructure.GetAllFilePaths(root);

                result.Should().Contain(f1).And.Contain(f2);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [TestMethod]
        public void GetAllFilePaths_EmptyOrMissing_Throws()
        {
            Action a1 = () => StaalGetWorkingDirectoryStructure.GetAllFilePaths("");
            a1.Should().Throw<ArgumentException>();

            var missing = Path.Combine(Path.GetTempPath(), "no_such_dir_" + Guid.NewGuid());
            Action a2 = () => StaalGetWorkingDirectoryStructure.GetAllFilePaths(missing);
            a2.Should().Throw<DirectoryNotFoundException>();
        }
    }
}