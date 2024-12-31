using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Fiatsoft.Alert.Grid {
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged {
        public MainWindow() {
            this.DataContext = this;
            ViewSource.Source = Alerts;
            ViewSource.Filter += (object sender, FilterEventArgs e) => { if (e.Item is Item item) e.Accepted = !item.Hidden; };

            InitializeComponent();
            Alerts.CollectionChanged += (object? sender, NotifyCollectionChangedEventArgs e) => {
                var sortDescriptions = ViewSource.SortDescriptions;
                if (!string.IsNullOrEmpty(SortSpec)) {
                    sortDescriptions.Clear();
                    sortDescriptions.Add(new SortDescription(SortSpecIndex, ListSortDirection.Ascending));
                }
                if (string.IsNullOrEmpty(SortSpec) && sortDescriptions.Count > 0) {
                    var propertyName = sortDescriptions[0].PropertyName;
                    var direction = sortDescriptions[0].Direction;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alerts)));
                    sortDescriptions.Clear();
                    sortDescriptions.Add(new SortDescription(propertyName, direction));
                }
                else {
                    PropertyChanged(this, new PropertyChangedEventArgs(nameof(Alerts)));
                }
                ViewSource.View.Refresh();
                AlertDataGrid.SelectedIndex = 0;
                RunButton.Focus();
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged = delegate { };

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {
            e.Cancel = true;
            this.Hide();
        }

        public List<string?> Schema {
            get { return (this.AlertDataGrid.Columns.Select(i => i.Header.ToString()).ToList()); }
            set {
                this.AlertDataGrid.Columns.Clear();
                foreach (var item in value.Distinct()) {
                    this.AlertDataGrid.Columns.Add(new DataGridTextColumn() { Header = item, Binding = new System.Windows.Data.Binding($"Data[{item}]") });
                }
                if (!HideCreatedColumn) {
                    this.AlertDataGrid.Columns.Add(
                        new DataGridTextColumn() {
                            Header = "Created",
                            Binding = new Binding("Created") { StringFormat = this.CreatedStringFormat }
                        }
                    );
                }
                AlertDataGrid.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            }
        }

        private ObservableCollection<Item> alerts = [];
        public ObservableCollection<Item> Alerts {
            get { return alerts; }
            set {
                alerts = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alerts)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewSource)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ViewSource.View)));
                ViewSource.View.Refresh();
            }
        }

        public string CreatedStringFormat { get; set; } = "O";
        public bool HideCreatedColumn { get; set; }

        private string? topicName;
        public string? TopicName {
            get { return topicName; }
            set {
                topicName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopicName)));
            }
        }

        const uint FLASHW_CAPTION = 1;
        const uint FLASHW_TRAY = 2;
        const uint FLASHW_ALL = FLASHW_CAPTION | FLASHW_TRAY;
        [StructLayout(LayoutKind.Sequential)]
        public struct FLASHWINFO {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }
        [LibraryImport("user32.dll", SetLastError = true, EntryPoint = "FlashWindowEx")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool FlashWindowEx(ref FLASHWINFO pwfi);
        public void ShowFlashing() {
            if (!this.IsVisible) {
                this.Show();
            }
            if (this.WindowState == WindowState.Minimized) {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
            FlashWindow();
        }
        private void FlashWindow() {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) {
                FLASHWINFO fi = new() {
                    cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO)),
                    hwnd = handle,
                    dwFlags = FLASHW_ALL,
                    uCount = 5,
                    dwTimeout = 0
                };
                FlashWindowEx(ref fi);
            }
        }

        private void RunButton_Click(object sender, RoutedEventArgs e) {
            var Action = (AlertDataGrid.SelectedItem as Item)?.Action;
            if (string.IsNullOrEmpty(Action)) {
                return;
            }

            Run(Action);
        }

        private static void Run(string action) {
            try {
                ParseCommandLine(action, out string processImage, out string arguments);
                if (!Path.IsPathRooted(processImage)) {
                    string[] pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
                    foreach (string pathDir in pathDirs) {
                        string fullPath = Path.Combine(pathDir, processImage);
                        if (File.Exists(fullPath)) {
                            processImage = fullPath;
                            break;
                        }
                    }
                }

                Process process = new() { StartInfo = new ProcessStartInfo() {
                    FileName = processImage,
                    Arguments = arguments,
                    WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    CreateNoWindow = true,
                    UseShellExecute = false
                }};
                process.Start();
            }
            catch (Exception ex) {
                Fiatsoft.Alert.Grid.App.HandleException(ex);
            }
        }

        static void ParseCommandLine(string command, out string processImage, out string arguments) {
            processImage = string.Empty;
            arguments = string.Empty;

            int quoteCount = 0;
            for (int i = 0; i < command.Length; i++) {
                char c = command[i];
                if (c == '"') {
                    quoteCount++;
                }
                else if (c == ' ' && quoteCount % 2 == 0) {
                    processImage = command[..i];
                    arguments = command[(i + 1)..].Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(processImage)) {
                processImage = command.Trim();
            }
        }

        public bool IsItemSelected => AlertDataGrid.SelectedIndex > -1 && AlertDataGrid.SelectedItem != null;
        public bool IsRunButtonEnabled => IsItemSelected && !string.IsNullOrEmpty(Alerts.ToArray()[AlertDataGrid.SelectedIndex].Action);
        public bool IsRunDefaultButtonEnabled => IsItemSelected && !string.IsNullOrEmpty(DefaultActionString);

        private void RunDefaultButton_Click(object sender, RoutedEventArgs e) {
            Run(DefaultAction);
        }

        private void Copy() => ApplicationCommands.Copy.Execute(null, AlertDataGrid);
        private void CopyOneOrMore(Func<Item, string> selection) {
            try {
                if (AlertDataGrid.SelectedItems.Count == 1)
                    Clipboard.SetText(selection(AlertDataGrid.SelectedItem as Item ?? Item.Empty));
                else
                    Clipboard.SetText(Newtonsoft.Json.JsonConvert.SerializeObject((from Item item in AlertDataGrid.SelectedItems.Cast<Item>() where item.Hidden is false select selection(item)).ToArray(), Newtonsoft.Json.Formatting.Indented));
            } catch (System.Runtime.InteropServices.COMException) { } //IGNORE: likely false clipboard failure created by WPF bug: https://github.com/dotnet/wpf/issues/9901 
        }

        private void CopyItem() => CopyOneOrMore((Item item) => item.JSON ?? "{}");
        private void CopyAction() => CopyOneOrMore((Item item) => item.Action ?? "");
        private void CopyItemButton_Click(object sender, RoutedEventArgs e) => CopyItem();
        private void CopyActionButton_Click(object sender, RoutedEventArgs e) => CopyAction();
        private void CopyButton_Click(object sender, RoutedEventArgs e) => Copy();
        
        private void DeleteButton_Click(object sender, RoutedEventArgs e) {
            var selectedIndex = AlertDataGrid.SelectedIndex;
            foreach (var item in AlertDataGrid.SelectedItems.Cast<Item>().ToList()) {
                Alerts.Remove(item);
            }
            ViewSource.View.Refresh();
            AlertDataGrid.SelectedIndex = Math.Min(selectedIndex, AlertDataGrid.Items.Count - 1);
        }

        private void HideButton_Click(object sender, RoutedEventArgs e) {
            foreach (Item item in AlertDataGrid.SelectedItems.Cast<Item>().ToList()) {
                item.Hidden = true;
            }
            ViewSource.View.Refresh();
            AlertDataGrid.SelectedIndex = -1;
        }

        public string? DefaultActionString { get; set; }
        public string DefaultAction { 
            get {
                if (IsRunDefaultButtonEnabled) {
                    return DefaultActionString?.Replace("@ITEM@", Newtonsoft.Json.JsonConvert.SerializeObject(AlertDataGrid.SelectedItem), StringComparison.OrdinalIgnoreCase) ?? "";
                }
                else {
                    if (alerts.Count==0)
                        return $"No items for: ${DefaultActionString}";
                    return $"No item selected for: ${DefaultActionString}";
                }
            }
        }

        private void AlertDataGridView_MouseDoubleClick(object sender, MouseButtonEventArgs e) {
            var action = (AlertDataGrid.SelectedItem as Item)?.Action ?? "";
            if (string.IsNullOrEmpty(action))
                return;
            Run(action);
        }

        private void AlertDataGridView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsItemSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunButtonEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunDefaultButtonEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentAction)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultAction)));
        }

        public string CurrentAction {
            get {
                if (AlertDataGrid.SelectedItem == null)
                    return "No Selection";
                else {
                    var action = (AlertDataGrid.SelectedItem as Item)?.Action;
                    return (string.IsNullOrEmpty(action)) ? "No Action" : action;
                }

            }
        }

        private string? sortSpec;
        public string? SortSpec {
            get {
                return sortSpec;
            }
            set {
                if ((sortSpec = value) != null) {
                    SortSpecName = value?.TrimStart('+', '-') ?? "";
                    SortSpecDirection = (value?.StartsWith('+')??false) ? ListSortDirection.Ascending : ListSortDirection.Descending;
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortSpec)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortSpecName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortSpecDirection)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SortSpecIndex)));
            }
        }

        /// <exception cref="Exception"/>
        public static void DevMethod0() { throw new NotImplementedException(); }

        public CollectionViewSource ViewSource { get; private set; } = new CollectionViewSource();

        private string? sortSpecName;
        public string? SortSpecName {
            get { return sortSpecName; }
            set { sortSpecName = value; }
        }

        private ListSortDirection? sortSpecDirection;
        public ListSortDirection? SortSpecDirection {
            get { return sortSpecDirection; }
            set { sortSpecDirection = value; }
        }

        public string? SortSpecIndex => string.IsNullOrEmpty(SortSpecName) ? null : $"Data[{SortSpecName}]";

        private void RestoreView_Click(object sender, RoutedEventArgs e) {
            if (ViewSource.View.SortDescriptions.Count > 0) {
                ViewSource.View.SortDescriptions.Clear();
            }

            Alerts.ToList().ForEach((a) => a.Hidden = false);
            ViewSource.View.Refresh();

            AlertDataGrid.SelectedItem = null;
            RunButton.Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                this.Close();
            }
            else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                ApplicationCommands.SelectAll.Execute(null, AlertDataGrid);
            }
            else if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) {
                ApplicationCommands.Copy.Execute(null, AlertDataGrid);
            }
        }
    }
}
