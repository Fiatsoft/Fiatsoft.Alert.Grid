using Hardcodet.Wpf.TaskbarNotification;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Media;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace Fiatsoft.Alert.Grid {
    public partial class App : Application {

        #region Properties
        private string MutexName = $"{assemblyImage}.Default.Mutex";
        public Mutex? mutex;
        private TaskbarIcon? taskbarIcon;
        private MainWindow? mainWindow;
        private const string DefaultTopicName = "Default";
        public string TopicName { get; private set; } = DefaultTopicName;
        public string PipeName { get; private set; } = $"{assemblyImage}.Default.Pipe";
        public string Action { get; private set; } = "";
        public string Title { get; set; } = "";
        public double Width { get; private set; }
        public double Height { get; private set; }
        public bool IsRunning { get; private set; } = true;
        public string AlarmSound { get; private set; } = "%windir%\\media\\Alarm03.wav";
        public bool IsAlertSoundMuted { get; set; } = false;
        public string? Mode { get; private set; }
        public List<string?> Schema { get; set; } = [];
        public string? DefaultAction { get; private set; }
        public string? SortSpec { get; private set; }
        public string CreatedStringFormat { get; set; } = "G";
        public bool HideCreatedColumn { get; set; }

        readonly static string assemblyImage = Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "Fiatsoft.Alert.Grid";
        readonly Dictionary<string, string> Values = [];
        private NamedPipeServerStream? pipeServer;
        private NamedPipeClientStream? pipeClient;

        public string DefaultTitle { get; } = $"{assemblyImage} v{Assembly.GetExecutingAssembly().GetName().Version}";
        public string TopicTitle {
            get {
                return DefaultTopicName.Equals(TopicName) ? $"{DefaultTitle}" : $"{TopicName} ({DefaultTitle})";
            }
        }

        public string? TopicNameSafe { get; private set; }

        private const int TimeoutFactor = 3;
        public static int Timeout => (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).Milliseconds * TimeoutFactor;

        public CancellationTokenSource CancellationTokenSource { get; private set; } = new CancellationTokenSource();
        public bool Topmost { get; private set; }

        static readonly string UsageInformation = $"Usage: {Path.GetFileName(Environment.ProcessPath)} -mode <push|pull> < [-topic {"\"topic name\""}] [{{column-key column-value}}... |  -title {{\"Title\"}}  |  -width {{width|300}} | -height {{height|300}} | -size {{widthxheight|300x300}} | -action {{command}} | -defaultaction {{command @ITEM@}} | -schema {{['allowed','columns']}} | -sort {{+Column|-Column|Column}} | -dateformat {{G|\"MM/dd/yyyy HH:mm:ss\"}} | -hidecreated ] > | [-h|--h|-help|--help|/?|-?|/h|/help] \n\n Segements will have \\n, \\t, etc unescaped and %variables% expanded";
        static readonly string HelpContent = UsageInformation;
        private string[]? args;
        #endregion

        private static string PrepCLIArgs(List<string> Args) {
            StringBuilder commandLineBuilder = new();
            foreach (string arg in Args) {
                if (arg.Contains(' ') || arg.Contains('"')) {
                    commandLineBuilder.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
                }
                else {
                    commandLineBuilder.Append(arg);
                }
                commandLineBuilder.Append(' ');
            }
            string commandLine = commandLineBuilder.ToString().TrimEnd();
            return (commandLine);
        }

        private void ProcessArgs(string[] args) {
            this.args = args;
            try {
                for (int i = 1; i < args.Length; i++) {
                    var arg = PrepArg(args[i]);
                    if (Fiatsoft.Alert.Grid.GeneratedRegex.Mode().IsMatch(arg))
                        Mode = args[(i += 1)];
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Topic().IsMatch(arg))
                        TopicName = args[(i += 1)];
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Schema().IsMatch(arg))
                        Schema.AddRange(JsonConvert.DeserializeObject<List<string>>(args[i += 1]) ?? []);
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Sort().IsMatch(arg)) {
                        SortSpec = args[(i += 1)];
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Action().IsMatch(arg))
                        Action = args[(i += 1)];
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Sound().IsMatch(arg))
                        AlarmSound = args[(i += 1)];
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.DefaultAction().IsMatch(arg)) {
                        DefaultAction = args[(i += 1)];
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Title().IsMatch(arg))
                        Title = args[(i += 1)];
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Size().IsMatch(arg)) {
                        var bounds = PrepArg(args[(i += 1)]).Split("x");
                        this.Height = Math.Min(SystemParameters.PrimaryScreenHeight, Int32.Parse(bounds[0]));
                        this.Width = Math.Min(SystemParameters.PrimaryScreenWidth, Int32.Parse(bounds[1]));
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Height().IsMatch(arg)) {
                        this.Height = Math.Min(SystemParameters.PrimaryScreenHeight, Int32.Parse(PrepArg(args[(i += 1)])));
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Width().IsMatch(arg)) {
                        this.Width = Math.Min(SystemParameters.PrimaryScreenWidth, Int32.Parse(PrepArg(args[(i += 1)])));
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.Help().IsMatch(arg))
                        throw new InvalidUsageException();
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.JSON().IsMatch(arg)) {
                        Dictionary<string, string> json = prepJSON(args[(i += 1)]);
                        foreach (var kvp in json) {
                            Values[kvp.Key] = kvp.Value;
                        }
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.DateFormat().IsMatch(arg)) {
                        CreatedStringFormat = args[(i += 1)];
                    }
                    else if (Fiatsoft.Alert.Grid.GeneratedRegex.HideCreated().IsMatch(arg)) {
                        HideCreatedColumn = true;
                    }
                    else {
                        if (args.Length<= i+1)
                            throw new UnmatchedKVPException($"Found no value for alert-property {args[i]}{Environment.NewLine}");
                        Values.Add(Regex.Unescape(Environment.ExpandEnvironmentVariables(args[i])), args[(i += 1)]);
                    }
                }
            }
            catch (InvalidUsageException) {
                MessageBox.Show(HelpContent, assemblyImage);
                Shutdown(0);
            }
            catch (Exception ex) {
                HandleException(ex);
            }

            static Dictionary<string, string> prepJSON(string v) {
                JObject jsonObject = JObject.Parse(v);
                Dictionary<string, string> parsedJson = [];
                foreach (JProperty property in jsonObject.Properties()) {
                    if (property.Value.Type == JTokenType.Object || property.Value.Type == JTokenType.Array) {
                        string valueAsString = property.Value.ToString(Newtonsoft.Json.Formatting.None);
                        parsedJson.Add(property.Name, valueAsString);
                    }
                    else {
                        parsedJson.Add(property.Name, property.Value.ToString());
                    }
                }
                return parsedJson;
            }
        }

        static string PrepArg(string v) {
            return v.ToLower().Trim();
        }

        protected override void OnStartup(StartupEventArgs e) {
            base.OnStartup(e);
            
            AppDomain.CurrentDomain.UnhandledException += OnUnhandleException;

            var now = DateTime.Now;
            if (Environment.GetEnvironmentVariable("DATE_") == null)
                Environment.SetEnvironmentVariable("DATE_", now.ToString("dd-MM-yyyy"));
            if (Environment.GetEnvironmentVariable("TIME_") == null)
                Environment.SetEnvironmentVariable("TIME_", Fiatsoft.Alert.Grid.GeneratedRegex.Time().Replace(now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.LongTimePattern), @"hh:"));

            ProcessArgs(Environment.GetCommandLineArgs());

            if (string.IsNullOrEmpty(TopicName))
                TopicName = DefaultTopicName;
            TopicNameSafe = $"{assemblyImage}.{TopicName[..Math.Min(TopicName.Length, 247 - assemblyImage.Length - 1 - "Daemon".Length - 1)]}";

            MutexName = $"{TopicNameSafe}.{"Daemon"}";
            PipeName = MutexName;

            var thread = new Thread(() => {
                if (Mode == "push") {
                    if (!IsDaemonRunning()) {
                        //LAUNCH NOW-ABSENT DAEMON:
                        var modifiedArgs = new List<string>(args?.Skip(1) ?? []);
                        //switch to -mode pull
                        {
                            int idx = modifiedArgs.IndexOf("-mode");
                            if (idx != -1 && idx + 1 < modifiedArgs.Count) {
                                modifiedArgs[idx + 1] = "pull";
                            }
                            else {
                                if (idx == -1)
                                    modifiedArgs.Add("-mode");
                                modifiedArgs.Add(WrapInQuotesIfContainsSpace("pull"));
                            }
                        }
                        //remove message, if present (to avoid duplicate after shared-signal to send message)
                        foreach (var key in Values.Keys) {
                            int idx;
                            while ((idx = modifiedArgs.IndexOf(key)) != -1) {
                                modifiedArgs.RemoveAt(idx + 1);
                                modifiedArgs.RemoveAt(idx);
                            }
                        }
                        //add schema, if absent
                        if (Array.FindIndex<string>([.. modifiedArgs], element => element.Equals("-schema", StringComparison.OrdinalIgnoreCase)) == -1) { //modifiedArgs.IndexOf("-schema");
                            modifiedArgs.Add("-schema");
                            modifiedArgs.Add(JsonConvert.SerializeObject(Values.Keys.ToArray()));
                        }
                         
                        using EventWaitHandle waitHandle = new(false, EventResetMode.ManualReset, $"{TopicNameSafe}.{"Launched"}");
                        Process.Start(Environment.ProcessPath ?? $".{Path.PathSeparator}Fiatsoft.Alert.Grid.exe", PrepCLIArgs(modifiedArgs));
                        try {
                            if (!waitHandle.WaitOne(Timeout * 20)) {
                                HandleException(new Exception("Could not start still-absent daemon, exiting"));
                                Shutdown(0);
                                return;
                            }
                        }
                        catch (Exception ex) {
                            HandleException(ex);
                            Shutdown(2);
                        }
                    }
                    RunAsMessenger(); //daemon exists or newly-created by another recently-started messenger
                }
                else {
                    try {
                        if (!TryRunExclusively(MutexName, () => { RunAsDaemon(); }))
                            HandleDaemonAlreadyRunning(); //run as messenger (if message prescripted); daemon already running
                    }
                    catch (Exception ex) {
                        HandleException(ex);
                    }
                }
                this.Dispatcher.Invoke(() => { Shutdown(0); });
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            static string WrapInQuotesIfContainsSpace(string newValue) {
                if (newValue.Contains(' ') && !Fiatsoft.Alert.Grid.GeneratedRegex.Quoted().IsMatch(newValue)) {
                    return "\"" + newValue + "\"";
                }
                return newValue;
            }
        }

        private void HandleDaemonAlreadyRunning() {
            if (Values!=null && Values.Count > 0) {
                RunAsMessenger();
            }
            else {
                return; //exit silently given no message pre-scripted, and probable inter-process race-condition
            }
        }

        private bool IsDaemonRunning() => !TryRunExclusively(MutexName, () => Debug.WriteLine($"{DateTime.Now:MMddyyyyZHHmmsszzz}: Daemon not currently running"));
        private bool TryRunExclusively(string mutexName, Action action) {
            using (mutex = new Mutex(false, mutexName, out bool createdNew)) {
                if (!createdNew) {
                    return false; //Mutex already created
                }
                bool acquiredLock = mutex.WaitOne(0); //without waiting, for exclusivity (not syncronization)
                if (acquiredLock) {
                    action();
                    mutex.ReleaseMutex();
                    return true;
                }
                return false; //Mutex lock stolen in likely race-condition vs another process/thread, very recently spawning daemon
            }
        }

        private void RunAsDaemon() {
            if (EventWaitHandle.TryOpenExisting($"{TopicNameSafe}.{"Launched"}", out var waitHandle)) {
                waitHandle.Set();
            }

            Schema = [.. Schema.Concat([.. Values.Keys]).Distinct()];

            this.Dispatcher.Invoke(() => {
                InitializeContextMenu();
                try {
                    mainWindow = new MainWindow();
                    if (!string.IsNullOrEmpty(CreatedStringFormat))
                        mainWindow.CreatedStringFormat = this.CreatedStringFormat;
                    mainWindow.HideCreatedColumn = this.HideCreatedColumn;
                    mainWindow.Schema = this.Schema;
                    mainWindow.DefaultActionString = this.DefaultAction;
                    mainWindow.TopicName = this.TopicName;
                    if (!string.IsNullOrEmpty(Title))
                        mainWindow.Title = Title;
                    else
                        mainWindow.Title = this.TopicTitle;
                    mainWindow.Topmost = this.Topmost;

                    if (!string.IsNullOrEmpty(SortSpec)) {
                        mainWindow.SortSpec = this.SortSpec;
                    }

                    if (Values.Count > 0) {
                        mainWindow.Alerts.Insert(0, new Item(Values, Schema) { Action = Action });
                        PlayAlert();
                        ShowFrontend();
                    }
                    else
                        mainWindow.Hide();

                }
                catch (Exception ex) {
                    HandleException(ex);
                    this.IsRunning = false;
                    CancellationTokenSource.Cancel();
                }
            });

            using (pipeServer = new NamedPipeServerStream(PipeName, PipeDirection.InOut)) {
                try {
                    while (this.IsRunning) {
                        Task waitForConnectionTask = pipeServer.WaitForConnectionAsync(CancellationTokenSource.Token);
                        var _ = Task.WhenAny(waitForConnectionTask, Task.Delay(-1, CancellationTokenSource.Token)).Result;
                        if (CancellationTokenSource.Token.IsCancellationRequested) {
                            break;
                        }
                        try {
                            byte[] buffer = new byte[(int)(Fiatsoft.Alert.Grid.Properties.Settings.Default.AppBufferSize)];
                            int bytesRead = pipeServer.Read(buffer, 0, buffer.Length);
                            string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            byte[] response = Encoding.UTF8.GetBytes("ACK");
                            pipeServer.Write(response, 0, response.Length);

                            var alert = new Item(JsonConvert.DeserializeObject<Item>(message) ?? Item.Empty, Schema); //ignore orphaned properties 
                            this.mainWindow?.Dispatcher.Invoke(() => {
                                if (mainWindow.Alerts.Count == 0 && mainWindow.Schema.Count <= (HideCreatedColumn?0:1)) { //missing schema, inhereit from first alert
                                    mainWindow.Schema = this.Schema = [.. alert.Data.Keys];
                                }
                                mainWindow.Alerts.Insert(0, alert);
                            });
                            
                            if (!IsAlertSoundMuted)
                                PlayAlert();
                            this.mainWindow?.Dispatcher.Invoke(() => {
                                ShowFrontend();
                            });
                        }
                        catch (Exception ex) {
                            HandleException(ex);
                            break;
                        }
                        finally {
                            pipeServer.Disconnect();
                        }
                    }
                }
                catch (OperationCanceledException) {
                    return; //Normal operation
                }
            }
        }

        private void RunAsMessenger() {
            var alert = new Item(Values) { Action = Action };
            using (pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut)) {
                try {
                    pipeClient.Connect(10000);
                    byte[] buffer = Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(alert));
                    pipeClient.Write(buffer, 0, buffer.Length);
                    buffer = new byte[(int)(Fiatsoft.Alert.Grid.Properties.Settings.Default.AppBufferSize)];
                    int bytesRead = pipeClient.Read(buffer, 0, buffer.Length);
                    string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                }
                catch (Exception ex) {
                    HandleException(ex);
                }
            }
        }

        private void InitializeContextMenu() {
            var contextMenu = new System.Windows.Controls.ContextMenu();

            var showHideMenuItem = new System.Windows.Controls.MenuItem { Header = "Show Window"};
            showHideMenuItem.Click += (sender, e) => { ShowFrontend(); };
            contextMenu.Items.Add(showHideMenuItem);

            var alwaysOnTopCheckBox = new System.Windows.Controls.CheckBox { Content = "Window Always On-top" };
            alwaysOnTopCheckBox.Click += (sender, e) => {
                this.Dispatcher.Invoke(() => { if (this.mainWindow != null) { this.mainWindow.Topmost = !(this.mainWindow?.Topmost ?? false); } });
            };
            alwaysOnTopCheckBox.IsChecked = this.Topmost = Fiatsoft.Alert.Grid.Properties.Settings.Default.MainWindowAlwaysOnTop;
            contextMenu.Items.Add(alwaysOnTopCheckBox);

            var muteAlertSoundCheckBox = new System.Windows.Controls.CheckBox { Content = "Mute Alert-sound" };
            muteAlertSoundCheckBox.Click += (sender, e) => { IsAlertSoundMuted = !IsAlertSoundMuted; };
            contextMenu.Items.Add(muteAlertSoundCheckBox);

#if DEBUG
            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            var devMenuItem = new System.Windows.Controls.MenuItem { Header = "Dev action 0" };
            devMenuItem.Click += (sender, e) => {
                mainWindow?.Dispatcher.Invoke(() => {
                    Fiatsoft.Alert.Grid.MainWindow.DevMethod0();
                });
            };
            contextMenu.Items.Add(devMenuItem);
#endif

            contextMenu.Items.Add(new System.Windows.Controls.Separator());
            var exitMenuItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
            exitMenuItem.Click += (sender, e) => {
                this.IsRunning = false;
                this.CancellationTokenSource.Cancel();
            };
            contextMenu.Items.Add(exitMenuItem);

            taskbarIcon = new TaskbarIcon() {
                Icon = new Icon(Application.GetResourceStream(new Uri("pack://application:,,,/Fiatsoft.Alert.Grid;component/Fiatsoft.Company.Temporary.Branding.Logo.Icon.ico")).Stream),
                ToolTipText = TopicTitle
            };
            taskbarIcon.TrayLeftMouseUp += (sender, e) => { taskbarIcon.ContextMenu.IsOpen = true; };
            taskbarIcon.ContextMenu = contextMenu;
        }

        private void ShowFrontend() {
            this.mainWindow?.ShowFlashing();
        }

        private void PlayAlert() {
            try {
                SoundPlayer soundPlayer = new(Environment.ExpandEnvironmentVariables(this.AlarmSound) ?? "");
                soundPlayer.Play();
                soundPlayer.Dispose();
            }
            catch (Exception) {
                try {
                    SystemSounds.Beep.Play();
                }
                catch (Exception) { }
            }
        }

        public static void HandleException(Exception ex) {
            MessageBox.Show(@$"Error: {ex.Message}:{Environment.NewLine}{Environment.NewLine}{ex.StackTrace}", assemblyImage);
        }
        private void OnUnhandleException(object sender, UnhandledExceptionEventArgs args) {
            HandleException((Exception)args.ExceptionObject);
        }

        protected override void OnExit(ExitEventArgs e) {
            try {
                IsRunning = false;
                if (e.ApplicationExitCode == 0 || e.ApplicationExitCode == 2) {
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();
                    mainWindow?.Close();
                    taskbarIcon?.Dispose();
                    pipeClient?.Close();
                    pipeServer?.Close();
                    //mutex disposal handled in TryRunExclusively
                }
            }
            catch (Exception ex) {
                HandleException(ex);
            }
            finally {
                base.OnExit(e);
            }
        }

        [Serializable]
        private class InvalidUsageException : Exception {}
        [Serializable]
        private  class UnmatchedKVPException : Exception {
            public UnmatchedKVPException() {}
            public UnmatchedKVPException(string? message) : base(message) {}
            public UnmatchedKVPException(string? message, Exception? innerException) : base(message, innerException) {}
        }
    }

    partial class GeneratedRegex {
        [GeneratedRegex("^-mode")]
        public static partial Regex Mode();
        [GeneratedRegex("^-topic$")]
        public static partial Regex Topic();
        [GeneratedRegex("^-schema$")]
        public static partial Regex Schema();
        [GeneratedRegex("^-sort$")]
        public static partial Regex Sort();
        [GeneratedRegex("^-action$")]
        public static partial Regex Action();
        [GeneratedRegex("^-sound")]
        public static partial Regex Sound();
        [GeneratedRegex("^-defaultaction")]
        public static partial Regex DefaultAction();
        [GeneratedRegex("^-title$")]
        public static partial Regex Title();
        [GeneratedRegex("^-size$")]
        public static partial Regex Size();
        [GeneratedRegex("^-height$")]
        public static partial Regex Height();
        [GeneratedRegex("^-width$")]
        public static partial Regex Width();
        [GeneratedRegex(@"^(-h|--h|-help|--help|/\?|-\?|/h|/help)$", RegexOptions.IgnoreCase, "en-US")]
        public static partial Regex Help();
        [GeneratedRegex("^-json$")]
        public static partial Regex JSON();
        [GeneratedRegex("^-dateformat")]
        public static partial Regex DateFormat();
        [GeneratedRegex("^-hidecreated")]
        public static partial Regex HideCreated();
        [GeneratedRegex(@"h(?=[^h:]*(?::|$))")]
        public static partial Regex Time();
        [GeneratedRegex("^\".*?\"$")]
        public static partial Regex Quoted();
    }
}
