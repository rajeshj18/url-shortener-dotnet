using System;
using System.IO;

namespace UrlShortener.IntegrationTests.Infrastructure
{
    /// <summary>Finds files of the repository from the test output folder.</summary>
    public static class RepositoryPaths
    {
        /// <summary>The repository root: the nearest parent folder that holds <c>UrlShortener.sln</c>.</summary>
        /// <returns>The absolute path.</returns>
        public static string Root()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Join(directory.FullName, "UrlShortener.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null)
            {
                throw new InvalidOperationException("Repository root (UrlShortener.sln) not found.");
            }

            return directory.FullName;
        }
    }
}
