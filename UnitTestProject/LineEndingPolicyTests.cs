using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UnitTestProject
{
    [TestClass]
    public class LineEndingPolicyTests
    {
        private static readonly HashSet<string> TargetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".csproj",
            ".xaml",
            ".sln",
            ".slnx",
            ".md"
        };

        [TestMethod]
        public void RepositoryTextFiles_DoNotContain_LfOnlyLineEndings()
        {
            string repositoryRoot = GetRepositoryRoot();
            List<string> violations = new List<string>();

            foreach (string filePath in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
            {
                if (ShouldSkipFile(filePath))
                {
                    continue;
                }

                if (!TargetExtensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                LineEndingScanResult scanResult = ScanLineEndings(filePath);
                if (scanResult.HasLfOnly)
                {
                    string relativePath = GetRelativePath(repositoryRoot, filePath);
                    violations.Add(relativePath + " (CRLF=" + scanResult.HasCrLf + ", LF-only=" + scanResult.HasLfOnly + ")");
                }
            }

            if (violations.Count > 0)
            {
                Assert.Fail(
                    "LF-only line endings detected in CRLF-managed files:" + Environment.NewLine +
                    string.Join(Environment.NewLine, violations));
            }
        }

        private static string GetRepositoryRoot()
        {
            string currentPath = AppDomain.CurrentDomain.BaseDirectory;

            while (!string.IsNullOrEmpty(currentPath))
            {
                string gitattributesPath = Path.Combine(currentPath, ".gitattributes");
                if (File.Exists(gitattributesPath))
                {
                    return currentPath;
                }

                DirectoryInfo parentDirectory = Directory.GetParent(currentPath);
                if (parentDirectory == null)
                {
                    break;
                }

                currentPath = parentDirectory.FullName;
            }

            Assert.Fail("Repository root was not found from test base directory.");
            return string.Empty;
        }

        private static bool ShouldSkipFile(string filePath)
        {
            string[] segments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string segment in segments)
            {
                if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("TestResults", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetRelativePath(string repositoryRoot, string filePath)
        {
            Uri rootUri = new Uri(AppendDirectorySeparator(repositoryRoot));
            Uri fileUri = new Uri(filePath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', '\\');
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static LineEndingScanResult ScanLineEndings(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            bool hasCrLf = false;
            bool hasLfOnly = false;

            for (int index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != 10)
                {
                    continue;
                }

                if (index > 0 && bytes[index - 1] == 13)
                {
                    hasCrLf = true;
                }
                else
                {
                    hasLfOnly = true;
                }

                if (hasCrLf && hasLfOnly)
                {
                    break;
                }
            }

            return new LineEndingScanResult(hasCrLf, hasLfOnly);
        }

        private struct LineEndingScanResult
        {
            public LineEndingScanResult(bool hasCrLf, bool hasLfOnly)
            {
                HasCrLf = hasCrLf;
                HasLfOnly = hasLfOnly;
            }

            public bool HasCrLf { get; private set; }

            public bool HasLfOnly { get; private set; }
        }
    }
}
