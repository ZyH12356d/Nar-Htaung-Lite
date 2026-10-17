namespace MusicPlayer
{
    public partial class MainPage : ContentPage
    {
        private readonly Services.NavigationService _navigationService;

        public MainPage(Services.NavigationService navigationService)
        {
            InitializeComponent();
            _navigationService = navigationService;
        }

        protected override bool OnBackButtonPressed()
        {
            if (_navigationService.IsPlayerExpanded)
            {
                _navigationService.RequestCollapsePlayer();
                return true;
            }
            if (string.IsNullOrEmpty(_navigationService.CurrentPage) || _navigationService.CurrentPage == "/" || _navigationService.CurrentPage == "index.html")
            {
                // On Home page, ask to quit
                _ = CheckQuitAsync();
                return true; // Handle it here
            }
            else
            {
                // Go to Home page via Blazor navigation event
                _navigationService.RequestNavigateToHome();
                return true;
            }
        }

        private async Task CheckQuitAsync()
        {
            bool answer = await DisplayAlertAsync("Keep the music going?", "Minimize the app and continue listening in the background.", "Minimize", "Stay here");
            if (answer)
            {
#if ANDROID
                var activity = Platform.CurrentActivity;
                activity?.MoveTaskToBack(true);
#else
                Application.Current?.Quit();
#endif
            }
        }
    }
}
