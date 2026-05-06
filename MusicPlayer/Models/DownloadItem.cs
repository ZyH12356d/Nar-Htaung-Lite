using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MusicPlayer.Models
{
    public enum DownloadStatus
    {
        Waiting,
        Processing,
        Downloading,
        Completed,
        Failed
    }

    public class DownloadItem : INotifyPropertyChanged
    {
        private DownloadStatus _status;
        private double _progress;
        private string _errorMessage;

        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;

        public DownloadStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
            {
                return false;
            }

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
