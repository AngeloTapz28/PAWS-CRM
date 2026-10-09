using Microsoft.UI.Xaml;
using QuestPDF.Infrastructure;

namespace PAWS.winui
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        // Exposed so any page can obtain a window handle for WinRT dialogs
        // (file pickers, etc.) that require one, e.g. the Excel/PDF export's
        // FileSavePicker. There is only ever one window in this app.
        public static Window? MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();

            // QuestPDF requires a license type to be chosen once at startup.
            // "Community" is the free license for students, open source and small projects.
            QuestPDF.Settings.License = LicenseType.Community;

            // QuestPDF 2026.9+ ignores Windows' installed fonts unless told otherwise.
            // The PDF layout asks for "Segoe UI", which every Windows PC has.
            QuestPDF.Settings.UseSystemFonts = true;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
        }
    }
}