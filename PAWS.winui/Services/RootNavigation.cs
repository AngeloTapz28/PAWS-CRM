using Microsoft.UI.Xaml.Controls;
using PAWS.winui.Views;

namespace PAWS.winui.Services;

// The ONE place that knows how to switch the whole window between the login
// page, the register page, the platform (Super Admin) login page, and the
// signed-in app.
public static class RootNavigation
{
    private static Frame? _rootFrame;

    public static void Initialize(Frame rootFrame)
    {
        _rootFrame = rootFrame;
    }

    public static void GoToLogin()
    {
        _rootFrame?.Navigate(typeof(LoginPage));
    }

    public static void GoToLoginAfterRegistration(string companyCode, string email)
    {
        _rootFrame?.Navigate(typeof(LoginPage), (companyCode, email));
    }

    public static void GoToRegister()
    {
        _rootFrame?.Navigate(typeof(RegisterPage));
    }

    public static void GoToPlatformLogin()
    {
        _rootFrame?.Navigate(typeof(PlatformLoginPage));
    }

    public static void GoToShell()
    {
        _rootFrame?.Navigate(typeof(ShellPage));
    }
}