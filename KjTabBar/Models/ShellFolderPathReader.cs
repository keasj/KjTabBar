using System;

namespace KjTabBar.Models
{
    internal sealed class ShellFolderPathReader
    {
        private readonly Func<object, string, object> _getComProperty;
        private readonly Action<object> _releaseComObject;

        public ShellFolderPathReader(
            Func<object, string, object> getComProperty,
            Action<object> releaseComObject)
        {
            _getComProperty = getComProperty;
            _releaseComObject = releaseComObject;
        }

        public string ReadFolderPath(object window)
        {
            string folderPath = null;
            object document = null;
            object folder = null;
            object folderSelf = null;
            try
            {
                document = _getComProperty(window, "Document");
                if (document == null)
                {
                    return null;
                }

                folder = _getComProperty(document, "Folder");
                if (folder == null)
                {
                    return null;
                }

                folderSelf = _getComProperty(folder, "Self");
                if (folderSelf == null)
                {
                    return null;
                }

                string rawPath = _getComProperty(folderSelf, "Path") as string;
                if (rawPath != null)
                {
                    folderPath = rawPath.TrimEnd('\0');
                }
            }
            finally
            {
                _releaseComObject(folderSelf);
                _releaseComObject(folder);
                _releaseComObject(document);
            }

            return folderPath;
        }
    }
}
