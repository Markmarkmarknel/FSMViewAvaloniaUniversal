using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.DependencyInjection;
using FSMExpress.Services;
using FSMExpress.ViewModels;
using FSMExpress.Views;
using Microsoft.Extensions.DependencyInjection;

namespace FSMExpress;
public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Line below is needed to remove Avalonia data validation.
            // Without this line you will get duplicate validations from both Avalonia and CT
            BindingPlugins.DataValidators.RemoveAt(0);
            
            // Configure services before creating the MainWindowViewModel
            var viewLocator = new ViewLocator();
            var services = new ServiceCollection();
            
            // Create window first so we can pass it to the dialog service
            desktop.MainWindow = new MainWindow();
            
            // Register services
            services.AddSingleton<IDialogService>(new DialogService(desktop.MainWindow, viewLocator));
            services.AddSingleton<ISettingsService, SettingsService>();
            
            // Build and configure the service provider
            var provider = services.BuildServiceProvider();
            Ioc.Default.ConfigureServices(provider);
            
            // Create the view model after IoC is configured
            desktop.MainWindow.DataContext = new MainWindowViewModel();
        }

        base.OnFrameworkInitializationCompleted();
    }
}