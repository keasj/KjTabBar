using KjTabBar.Models;

namespace KjTabBar.ViewModels
{
    public class TabItemViewModel : ViewModelBase
    {
        private string _title;
        private Models.IExplorerService _explorerService;
        private string _path;
        private bool _isActive;

        public string Title
        {
            get { return _title; }
            set { _title = value; OnPropertyChanged("Title"); }
        }

        public string Path
        {
            get { return _path; }
            set { _path = value; OnPropertyChanged("Path"); }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { _isActive = value; OnPropertyChanged("IsActive"); }
        }

        public TabItemViewModel(string path, string title, Models.IExplorerService explorerService)
        {
            _path = path;
            _explorerService = explorerService;
            _title = string.IsNullOrEmpty(title) ? _explorerService.GetLocalizedHomeTitle() : title;
            _isActive = false;
        }
    }
}
