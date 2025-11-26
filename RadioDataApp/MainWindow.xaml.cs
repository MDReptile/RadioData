using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using RadioDataApp.ViewModels;

namespace RadioDataApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainViewModel? ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();

            // Run loopback tests on startup (DEBUG only)
#if DEBUG
            Tests.LoopbackTest.RunTests();
#endif

            // Subscribe to ViewModel's PropertyChanged event for auto-scroll
            // Must be done after InitializeComponent when DataContext is set
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.DebugLog))
            {
                // Auto-scroll to bottom when DebugLog changes 
                Dispatcher.InvokeAsync(() =>
                {
                    LogScrollViewer?.ScrollToBottom();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void TestLogInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Hidden TextBox for UI test logging
            if (sender is TextBox textBox && !string.IsNullOrEmpty(textBox.Text))
            {
                ViewModel?.AddLogEntry(textBox.Text, "UI TEST");
                // Clear after processing to allow the same message again
                textBox.Text = string.Empty;
            }
        }
    }
}