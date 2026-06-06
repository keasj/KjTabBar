using System;

namespace KjTabBar.Models
{
    internal sealed class ShellParentFolderTitleReader
    {
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly Func<object, string, object> _getComProperty;
        private readonly Action<object> _releaseComObject;

        public ShellParentFolderTitleReader(
            Func<object, string, object[], object> invokeComMethod,
            Func<object, string, object> getComProperty,
            Action<object> releaseComObject)
        {
            _invokeComMethod = invokeComMethod;
            _getComProperty = getComProperty;
            _releaseComObject = releaseComObject;
        }

        public string ReadTitle(object shellObject, string displayPath)
        {
            if (shellObject == null || string.IsNullOrEmpty(displayPath))
            {
                return null;
            }

            object folder = null;
            object parentFolder = null;
            try
            {
                folder = _invokeComMethod(shellObject, "NameSpace", new object[] { displayPath });
                if (folder == null)
                {
                    return null;
                }

                parentFolder = _getComProperty(folder, "ParentFolder");
                if (parentFolder == null)
                {
                    return null;
                }

                string title = _getComProperty(parentFolder, "Title") as string;
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }

                object parentItem = null;
                try
                {
                    parentItem = _getComProperty(parentFolder, "Self");
                    return _getComProperty(parentItem, "Name") as string;
                }
                finally
                {
                    _releaseComObject(parentItem);
                }
            }
            finally
            {
                _releaseComObject(parentFolder);
                _releaseComObject(folder);
            }
        }
    }
}
