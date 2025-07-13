using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NifDdsGrabber
{
    public partial class MainWindow : Window
    {
        private List<string> extractedDDS = new List<string>();
        private string ddsFolderPath = "";
        private const string SettingsFile = "user_settings.txt";

        public MainWindow()
        {
            InitializeComponent();
            AllowDrop = true;
            Drop += Window_Drop;

            LoadUserSettings(); // <-- Load checkbox states at startup

            // Load last DDS folder if it exists, and show output folder
            string memoryFile = "last_dds_path.txt";
            if (File.Exists(memoryFile))
            {
                ddsFolderPath = File.ReadAllText(memoryFile);
                TxtDDSFolder.Text = ddsFolderPath;

                if (!string.IsNullOrEmpty(ddsFolderPath) && Directory.Exists(ddsFolderPath))
                    TxtOutputFolder.Text = Path.Combine(ddsFolderPath, "OutputDDS");
                else
                    TxtOutputFolder.Text = "";
            }
            else
            {
                TxtOutputFolder.Text = "";
            }
        }

        // -- Save/load checkbox state logic --

        private void SaveUserSettings()
        {
            try
            {
                File.WriteAllLines(SettingsFile, new string[]
                {
                    ChkPreserveStructure.IsChecked == true ? "1" : "0",
                    ChkOpenAfterCopy.IsChecked == true ? "1" : "0"
                });
            }
            catch { /* ignore errors */ }
        }

        private void LoadUserSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var lines = File.ReadAllLines(SettingsFile);
                    if (lines.Length >= 1)
                        ChkPreserveStructure.IsChecked = lines[0] == "1";
                    if (lines.Length >= 2)
                        ChkOpenAfterCopy.IsChecked = lines[1] == "1";
                }
            }
            catch { /* ignore errors */ }
        }

        private void ChkBoxChanged(object sender, RoutedEventArgs e)
        {
            SaveUserSettings();
        }

        // -- Rest of your code --

        private void BtnLoadNif_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "NIF files (*.nif)|*.nif";
            if (ofd.ShowDialog() == true)
            {
                LoadDDSReferences(ofd.FileName);
            }
        }

        private void BtnSelectDDSFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                string memoryFile = "last_dds_path.txt";
                if (File.Exists(memoryFile))
                    dialog.SelectedPath = File.ReadAllText(memoryFile);

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ddsFolderPath = dialog.SelectedPath;
                    TxtDDSFolder.Text = ddsFolderPath;
                    File.WriteAllText(memoryFile, ddsFolderPath);

                    // Set default output folder when DDS folder changes
                    TxtOutputFolder.Text = Path.Combine(ddsFolderPath, "OutputDDS");
                    DisplayDDSMatches();
                }
            }
        }

        private void LoadDDSReferences(string nifPath)
        {
            try
            {
                byte[] data = File.ReadAllBytes(nifPath);
                var matches = Regex.Matches(System.Text.Encoding.UTF8.GetString(data), @"[\\/\w\.-]+\.dds", RegexOptions.IgnoreCase);
                extractedDDS = matches.Cast<Match>().Select(m => m.Value).Distinct().OrderBy(m => m).ToList();
                TxtNifPath.Text = nifPath;
                DisplayDDSMatches();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading NIF file: {ex.Message}");
            }
        }

        private async void DisplayDDSMatches()
        {
            if (string.IsNullOrEmpty(ddsFolderPath) || extractedDDS.Count == 0)
                return;

            LstDDS.Items.Clear();
            ProgressLoad.Visibility = Visibility.Visible;
            ProgressLoad.Minimum = 0;
            ProgressLoad.Maximum = extractedDDS.Count;
            ProgressLoad.Value = 0;

            await Task.Run(() =>
            {
                int i = 0;
                foreach (var dds in extractedDDS)
                {
                    string fileName = Path.GetFileName(dds);
                    string[] matches = Directory.GetFiles(ddsFolderPath, fileName, SearchOption.AllDirectories);
                    string foundPath = matches.FirstOrDefault();

                    Dispatcher.Invoke(() =>
                    {
                        var item = new ListBoxItem();
                        item.Tag = foundPath;
                        item.Content = new DDSDisplay(fileName, foundPath);
                        item.ContextMenu = new ContextMenu();

                        var openItem = new MenuItem { Header = "Open File Location" };
                        openItem.Click += (s, e) =>
                        {
                            if (!string.IsNullOrEmpty(foundPath))
                                Process.Start("explorer.exe", "/select,\"" + foundPath + "\"");
                        };
                        item.ContextMenu.Items.Add(openItem);

                        LstDDS.Items.Add(item);
                        ProgressLoad.Value = ++i;
                    });
                }
            });

            ProgressLoad.Visibility = Visibility.Collapsed;
        }

        private void BtnBrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(TxtOutputFolder.Text) && Directory.Exists(TxtOutputFolder.Text))
                    dialog.SelectedPath = TxtOutputFolder.Text;
                else if (!string.IsNullOrEmpty(ddsFolderPath))
                    dialog.SelectedPath = ddsFolderPath;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtOutputFolder.Text = dialog.SelectedPath;
                }
            }
        }

        public static string GetRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(AppendDirectorySeparatorChar(basePath));
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            // Appends a slash only if the path is a directory and does not have a slash
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
                return path + Path.DirectorySeparatorChar;
            else
                return path;
        }

        private void BtnCopyDDS_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ddsFolderPath) || extractedDDS.Count == 0)
                return;

            string outputDir = TxtOutputFolder.Text;
            if (string.IsNullOrWhiteSpace(outputDir))
                outputDir = Path.Combine(ddsFolderPath, "OutputDDS");

            string nifFileName = Path.GetFileNameWithoutExtension(TxtNifPath.Text);
            if (!string.IsNullOrEmpty(nifFileName))
                outputDir = Path.Combine(outputDir, nifFileName);

            bool preserveStructure = ChkPreserveStructure.IsChecked == true;

            foreach (var dds in extractedDDS)
            {
                string fileName = Path.GetFileName(dds);
                string[] matches = Directory.GetFiles(ddsFolderPath, fileName, SearchOption.AllDirectories);
                string sourcePath = matches.FirstOrDefault();

                string destPath;
                if (preserveStructure && !string.IsNullOrEmpty(sourcePath))
                {
                    // Use actual subdirectory structure from sourcePath, relative to ddsFolderPath
                    string relativeFsPath = GetRelativePath(ddsFolderPath, sourcePath);
                    destPath = Path.Combine(outputDir, relativeFsPath);
                }
                else
                {
                    destPath = Path.Combine(outputDir, fileName);
                }

                string destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath))
                    File.Copy(sourcePath, destPath, true);
            }

            MessageBox.Show("Copied found DDS files to OutputDDS.");

            // Only open the output folder if the user wants to
            if (ChkOpenAfterCopy.IsChecked == true)
            {
                Process.Start("explorer.exe", outputDir);
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && files[0].ToLower().EndsWith(".nif"))
                {
                    LoadDDSReferences(files[0]);
                }
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
                e.Handled = true;
            }
        }

        // Double-click to open file location
        private void LstDDS_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstDDS.SelectedItem is ListBoxItem item && item.Tag is string filePath && !string.IsNullOrEmpty(filePath))
            {
                Process.Start("explorer.exe", "/select,\"" + filePath + "\"");
            }
        }
    }

    public class DDSDisplay : StackPanel
    {
        private const string TexConvPath = @"tools\texconv.exe";

        public DDSDisplay(string fileName, string fullPath)
        {
            Orientation = Orientation.Horizontal;

            var img = new Image
            {
                Width = 64,
                Height = 64,
                Source = LoadDDSPreview(fullPath)
            };

            var border = new Border
            {
                Width = 66,
                Height = 66,
                Margin = new Thickness(0, 0, 10, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = System.Windows.Media.Brushes.Gray,
                Child = img
            };

            var textPanel = new StackPanel();
            textPanel.Children.Add(new TextBlock
            {
                Text = fileName,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = fullPath ?? "(Not Found)",
                FontSize = 10,
                Foreground = System.Windows.Media.Brushes.LightGray
            });

            Children.Add(border);
            Children.Add(textPanel);
        }

        private BitmapImage LoadDDSPreview(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                string tempDir = Path.Combine(Path.GetTempPath(), "DDSViewerCache");
                Directory.CreateDirectory(tempDir);

                string tempPng = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(path) + ".png");

                if (!File.Exists(tempPng))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = TexConvPath,
                        Arguments = $"-ft png -o \"{tempDir}\" \"{path}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    var proc = Process.Start(psi);
                    proc.WaitForExit();
                }

                if (File.Exists(tempPng))
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.UriSource = new Uri(tempPng);
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    return img;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }
    }
}
