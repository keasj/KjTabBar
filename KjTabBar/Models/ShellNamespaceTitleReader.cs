using System;

namespace KjTabBar.Models
{
    internal sealed class ShellNamespaceTitleReader
    {
        private readonly Func<object, string, object[], object> _invokeComMethod;
        private readonly Func<object, string, object> _getComProperty;
        private readonly Action<object> _releaseComObject;

        public ShellNamespaceTitleReader(
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

            object shellNamespace = null;
            try
            {
                shellNamespace = _invokeComMethod(shellObject, "NameSpace", new object[] { displayPath });
                if (shellNamespace == null)
                {
                    return null;
                }

                string title = _getComProperty(shellNamespace, "Title") as string;
                if (string.IsNullOrEmpty(title))
                {
                    return null;
                }

                return title;
            }
            finally
            {
                _releaseComObject(shellNamespace);
            }
        }
    }
}
