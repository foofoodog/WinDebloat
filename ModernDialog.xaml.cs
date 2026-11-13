using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinDebloat
{
    public enum ModernDialogType
    {
        Info,
        Confirm,
        Warning,
        Shield,
        Reboot,
        Success
    }

    public enum ModernDialogResult
    {
        Yes,
        No,
        Cancel,
        Ok
    }

    public partial class ModernDialog : Window
    {
        public ModernDialogResult Result { get; private set; } = ModernDialogResult.Cancel;

        public ModernDialog()
        {
            InitializeComponent();
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
        }

        private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = ModernDialogResult.Yes;
            DialogResult = true;
            Close();
        }

        private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = ModernDialogResult.No;
            DialogResult = false;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = ModernDialogResult.Cancel;
            DialogResult = false;
            Close();
        }

        public static ModernDialogResult ShowPrompt(
            string title, 
            string message, 
            string primaryText = "Yes", 
            string? secondaryText = "No", 
            string? cancelText = null, 
            ModernDialogType type = ModernDialogType.Confirm,
            Window? owner = null)
        {
            var dialog = new ModernDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = message;
            dialog.PrimaryBtn.Content = primaryText;

            // Configure secondary button
            if (!string.IsNullOrEmpty(secondaryText))
            {
                dialog.SecondaryBtn.Content = secondaryText;
                dialog.SecondaryBtn.Visibility = Visibility.Visible;
            }
            else
            {
                dialog.SecondaryBtn.Visibility = Visibility.Collapsed;
            }

            // Configure cancel button
            if (!string.IsNullOrEmpty(cancelText))
            {
                dialog.CancelBtn.Content = cancelText;
                dialog.CancelBtn.Visibility = Visibility.Visible;
            }
            else
            {
                dialog.CancelBtn.Visibility = Visibility.Collapsed;
            }

            // Style by type
            switch (type)
            {
                case ModernDialogType.Info:
                    dialog.IconText.Text = "ℹ️";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B82F6"));
                    break;
                case ModernDialogType.Warning:
                    dialog.IconText.Text = "⚠️";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A03"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D97706"));
                    break;
                case ModernDialogType.Shield:
                    dialog.IconText.Text = "🛡️";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C2D12"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    break;
                case ModernDialogType.Reboot:
                    dialog.IconText.Text = "🔄";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4C1D95"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7C3AED"));
                    break;
                case ModernDialogType.Success:
                    dialog.IconText.Text = "✅";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                    break;
                case ModernDialogType.Confirm:
                default:
                    dialog.IconText.Text = "⚡";
                    dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#312E81"));
                    dialog.PrimaryBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
                    break;
            }

            owner ??= Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        public static bool ShowConfirm(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", ModernDialogType type = ModernDialogType.Confirm)
        {
            var res = ShowPrompt(title, message, primaryText: confirmText, secondaryText: cancelText, cancelText: null, type: type);
            return res == ModernDialogResult.Yes;
        }

        public static void ShowInfo(string title, string message, ModernDialogType type = ModernDialogType.Info)
        {
            ShowPrompt(title, message, primaryText: "OK", secondaryText: null, cancelText: null, type: type);
        }

        public static ModernDialog ShowProgressDialog(string title, string initialMessage, Window? owner = null)
        {
            var dialog = new ModernDialog();
            dialog.TitleText.Text = title;
            dialog.MessageText.Text = initialMessage;
            dialog.DialogProgressBar.Visibility = Visibility.Visible;
            dialog.PrimaryBtn.Visibility = Visibility.Collapsed;
            dialog.SecondaryBtn.Visibility = Visibility.Collapsed;
            dialog.CancelBtn.Visibility = Visibility.Collapsed;
            dialog.IconText.Text = "🛡️";
            dialog.IconBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E3A8A"));

            owner ??= Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.Show();
            dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            return dialog;
        }

        public void UpdateMessage(string message)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MessageText.Text = message;
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }
}
