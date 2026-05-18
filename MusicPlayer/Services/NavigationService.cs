namespace MusicPlayer.Services
{
    public class NavigationService
    {
        public string CurrentPage { get; set; } = "/";
        public event Action? NavigateToHomeRequested;

        public void RequestNavigateToHome()
        {
            NavigateToHomeRequested?.Invoke();
        }
    }
}
