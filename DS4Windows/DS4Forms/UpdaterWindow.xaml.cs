/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using DS4WinWPF.DS4Control;
using DS4WinWPF.DS4Forms.ViewModels;
using DS4WinWPF.Translations;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using DS4Windows;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for UpdaterWindow.xaml
    /// </summary>
    public partial class UpdaterWindow : Window
    {
        private MessageBoxResult result = MessageBoxResult.No;
        public MessageBoxResult Result { get => result; }

        /// <summary>
        /// Verified installer to run when Result is Yes; null means the update
        /// has to be downloaded from the release page
        /// </summary>
        public string InstallerPath { get; private set; }

        private UpdaterWindowViewModel updaterWinVM;
        private readonly string version;
        private CancellationTokenSource downloadCancel;
        private bool downloading;

        public UpdaterWindow(string newversion)
        {
            InitializeComponent();

            version = newversion;

            Title = Properties.Resources.DS4Update;
            captionTextBlock.Text = Properties.Resources.DownloadVersion.Replace("*number*",
                newversion);
            updaterWinVM = new UpdaterWindowViewModel(newversion);
            updaterWinVM.BlankSkippedVersion();

            DataContext = updaterWinVM;

            Task.Run(async () =>
            {
                await Dispatcher.InvokeAsync(async () => await updaterWinVM.DisplayChangelog());
            });
        }

        private async void YesBtn_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.Yes;
            if (!AppUpdater.IsInstalledCopy)
            {
                MessageBox.Show(this, Strings.UpdaterWin_PortableCopy, Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
                return;
            }

            yesBtn.IsEnabled = false;
            skipVersionBtn.IsEnabled = false;
            downloadPanel.Visibility = Visibility.Visible;
            ShowDownloadProgress(0.0);

            downloadCancel = new CancellationTokenSource();
            Progress<double> progress = new(ShowDownloadProgress);
            downloading = true;
            try
            {
                string installer = await AppUpdater.DownloadInstallerAsync(version, progress,
                    downloadCancel.Token);
                downloadStatusText.Text = Strings.UpdaterWin_StartingInstaller;
                InstallerPath = installer;
            }
            catch (OperationCanceledException) when (downloadCancel.IsCancellationRequested)
            {
                // Window closed during the download
                return;
            }
            catch (Exception ex)
            {
                AppLogger.LogToGui($"Automatic update to {version} failed: {ex.Message}", true);
                string reason = ex is AppUpdateException ? ex.Message : $"{ex.GetType().Name}: {ex.Message}";
                MessageBox.Show(this, string.Format(Strings.UpdaterWin_AutoUpdateFailed, reason), Title,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                downloading = false;
            }

            Close();
        }

        private void ShowDownloadProgress(double fraction)
        {
            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
            {
                downloadProgress.IsIndeterminate = true;
                downloadStatusText.Text = string.Format(Strings.UpdaterWin_Downloading, "").TrimEnd();
                return;
            }

            fraction = Math.Clamp(fraction, 0.0, 1.0);
            downloadProgress.IsIndeterminate = false;
            downloadProgress.Value = fraction;
            downloadStatusText.Text = string.Format(Strings.UpdaterWin_Downloading,
                fraction.ToString("P0"));
        }

        private void NoBtn_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.No;
            Close();
        }

        private void SkipVersionBtn_Click(object sender, RoutedEventArgs e)
        {
            result = MessageBoxResult.No;
            updaterWinVM.SetSkippedVersion();
            Close();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            // Closing the window cancels a running download
            if (downloading)
            {
                result = MessageBoxResult.No;
                downloadCancel.Cancel();
            }

            DataContext = null;
        }
    }
}
