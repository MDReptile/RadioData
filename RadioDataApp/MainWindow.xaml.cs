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

            Loaded += (s, e) => 
            {
                CenterWindowOnPrimaryScreen();
                
                Dispatcher.InvokeAsync(() =>
                {
                    ChatScrollViewer?.ScrollToBottom();
                    LogScrollViewer?.ScrollToBottom();
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // Subscribe to ViewModel's PropertyChanged event for auto-scroll
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void CenterWindowOnPrimaryScreen()
        {
            var workArea = SystemParameters.WorkArea;
            
            this.Left = workArea.Left + (workArea.Width - this.ActualWidth) / 2;
            this.Top = workArea.Top + (workArea.Height - this.ActualHeight) / 2;

            if (this.Left < workArea.Left)
                this.Left = workArea.Left;
            if (this.Top < workArea.Top)
                this.Top = workArea.Top;

            if (this.Left + this.ActualWidth > workArea.Right)
                this.Left = workArea.Right - this.ActualWidth;
            if (this.Top + this.ActualHeight > workArea.Bottom)
                this.Top = workArea.Bottom - this.ActualHeight;
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
            else if (e.PropertyName == nameof(MainViewModel.ChatLog))
            {
                Dispatcher.InvokeAsync(() =>
                {
                    ChatScrollViewer?.ScrollToBottom();
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

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void EncryptionKeyBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Remove focus from the textbox
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Send the message
                if (ViewModel != null && ViewModel.StartTransmissionCommand.CanExecute(null))
                {
                    ViewModel.StartTransmissionCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void ClientNameBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Remove focus from the textbox
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
    }
}