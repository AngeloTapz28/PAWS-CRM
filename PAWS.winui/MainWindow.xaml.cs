using Microsoft.UI.Xaml;
using PAWS.winui.Services;
using PAWS.winui.Views;
using System;

namespace PAWS.winui;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "PAWS CRM";
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "paws.ico"));

        RootNavigation.Initialize(RootFrame);

        // The app always starts at the login page.
        RootFrame.Navigate(typeof(LoginPage));
    }
}