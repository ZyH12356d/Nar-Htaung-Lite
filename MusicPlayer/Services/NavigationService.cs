namespace MusicPlayer.Services
{
    public class NavigationService
    {
        public string CurrentPage { get; set; } = "/";
        public bool IsPlayerExpanded { get; set; }
        public event Action? CollapsePlayerRequested;
        public void RequestCollapsePlayer() => CollapsePlayerRequested?.Invoke();
        public event Action? NavigateToHomeRequested;

        public void RequestNavigateToHome()
        {
            NavigateToHomeRequested?.Invoke();
        }
    }
}
